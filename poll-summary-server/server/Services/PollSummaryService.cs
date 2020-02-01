

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Voting.Models;
using Voting.Record;
using Voting.Summary;
using static Voting.Record.RecordingService;

namespace server.Services
{
    public class PollSummaryService : Voting.Summary.PollSummaryService.PollSummaryServiceBase
    {

        private readonly ILogger _logger;
        private readonly PubSubService _pubSubService;
        private readonly StorageService _storageService;
        private readonly ShutdownService _shutdownService;
        private readonly RecordingServiceClient _recordServiceClient;
        public PollSummaryService(
            ILogger<PollSummaryService> logger,
            PubSubService pubSubService,
            StorageService redisService,
            ShutdownService shutdownService,
            RecordingServiceClient recordServiceClient,
            ComputeService computeService
        )
        {
            _logger = logger;
            _pubSubService = pubSubService;
            _storageService = redisService;
            _shutdownService = shutdownService;
            _recordServiceClient = recordServiceClient;

        }

        public override async Task<GetPollSummaryResponse> GetPollSummary(Poll request, ServerCallContext context)
        {
            ///NOTE: not currently implemented in grpc-net
            //var contextPropagationToken = context.CreatePropagationToken();
            try {
                var summary = await _storageService.GetPollSummary(request.Id);
                return new GetPollSummaryResponse(){
                    Error = summary == null,
                    Value = summary
                };
            } catch (Exception e) {
                _logger.LogError(e, "Error while handling GetPollSummary");
                return new GetPollSummaryResponse() {
                    Error = true
                };
            }
        }

        public override async Task StreamPollSummary(Poll request, IServerStreamWriter<PollSummary> responseStream, ServerCallContext context)
        {

            var close = _pubSubService.SubscribePollSummary(request.Id, (summary) =>
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }
                try
                {
                    var writeTask = responseStream.WriteAsync(summary);
                    writeTask.Wait();
                }
                catch (Exception e)
                {
                    _logger.LogDebug(e, "Error while sending summary to client");
                }
            });
            while (true)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    close();
                    _logger.LogDebug("Gracefully closing subscription");
                    break;
                }
                await Task.Delay(100);
            }
        }

        
    }
}