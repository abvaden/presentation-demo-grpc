using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Voting.Models;
using Voting.Record;
using Voting.Summary;
using static Voting.Record.RecordingService;

namespace server.Services
{
    public class ComputeService
    {

        private readonly ILogger _logger;
        private readonly PubSubService _pubSubService;
        private readonly StorageService _storageService;
        private readonly ShutdownService _shutdownService;
        private readonly RecordingServiceClient _recordServiceClient;
        
        public ComputeService(
            ILogger<ComputeService> logger,
            PubSubService pubSubService,
            StorageService redisService,
            ShutdownService shutdownService,
            RecordingServiceClient recordServiceClient
        )
        {
            _logger = logger;
            _pubSubService = pubSubService;
            _storageService = redisService;
            _shutdownService = shutdownService;
            _recordServiceClient = recordServiceClient;
            




        }

        public void Start()
        {
            var cts = new CancellationTokenSource();
            // Streaming from a sequence is currently not implemented
            var streamRecords = new Voting.Record.StreamRecordsRequest();
            var readStream = _recordServiceClient.StreamRecords(streamRecords, new CallOptions(cancellationToken: cts.Token));
            HandleResponseStream(readStream);
            _shutdownService.RegisterShutdownCallback(() => { cts.Cancel(); });
        }

        private async Task<bool> ProcessRecord(VotingRecord record)
        {
            try
            {
                _logger.LogDebug($"Processing voting record {record.Sequence} for user {record?.Vote?.UserId} on poll {record?.Vote?.PollId}");

                var vote = record.Vote;
                if (vote == null)
                {
                    _logger.LogDebug($"Not processing record, vote = null");
                    return false;
                }

                var pollSummary = await _storageService.GetPollSummary(vote.PollId);
                if (pollSummary == null)
                {
                    _logger.LogWarning($"Not processing record for poll {vote.PollId}, summary could not be identified");
                    return false;
                }

                pollSummary.TotalVotes++;

                var pollOption = pollSummary.Options.FirstOrDefault(x => x.Option.Id == vote.OptionId);
                if (pollOption == null)
                {
                    _logger.LogWarning($"Specified poll option {vote.OptionId} not found");
                    return false;
                }

                pollOption.VoteCounts++;

                // ReRank votes and compute ratios
                var rank = 0;
                foreach (var option in pollSummary.Options.OrderBy(x => x.VoteCounts))
                {
                    option.Rank = rank;
                    option.Ratio = option.VoteCounts / pollSummary.TotalVotes;
                    rank++;
                }

                var didUpsert = await _storageService.UpsertPollSummary(pollSummary);
                if (!didUpsert)
                {
                    _logger.LogWarning("Error while upserting PollSummary");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while processing voting record");
                return false;
            }
            finally
            {
                _logger.LogDebug($"Finished processing voting record {record.Sequence} for user {record.Vote.UserId} on poll {record.Vote.PollId}");
            }
        }

        private async void HandleResponseStream(AsyncServerStreamingCall<VotingRecord> call)
        {
            try
            {
                var readStream = call.ResponseStream;

                await foreach (var votingRecord in readStream.ReadAllAsync())
                {
                    var didProcess = await ProcessRecord(votingRecord);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while processing vote stream");
            }

            _shutdownService.HandleFailure("Response stream has ended");
        }
    }
}