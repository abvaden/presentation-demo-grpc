using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using RandomNameGeneratorLibrary;
using Voting.Record;
using Voting.Summary;
using Voting.Users;

namespace client
{
    public class Program
    {
        static object _locker = new object();
        static async Task Main(string[] args)
        {
            AppContext.SetSwitch(
    "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var interactive = args.Any(x => x == "interactive");

            if (args.Any(x => x == "vote")) {
                await Vote(interactive);
            } else {
                await Stream();
            }
            
            

            Console.WriteLine("Shutting down");
        }


        static async Task Vote(bool interactive)
        {

            var usersHost = GetEnvVariableOrDefault("USERS_SERVICE_HOST", "localhost");
            var usersPort = GetEnvVariableOrDefault("USERS_SERVICE_PORT", "50000");
            var recordHost = GetEnvVariableOrDefault("RECORD_SERVICE_HOST", "localhost");
            var recordPort = GetEnvVariableOrDefault("RECORD_SERVICE_PORT", "50000");
            var summaryHost = GetEnvVariableOrDefault("SUMMARY_SERVICE_HOST", "localhost");
            var summaryPort = GetEnvVariableOrDefault("SUMMARY_SERVICE_PORT", "50000");
            var channelUsers = GrpcChannel.ForAddress($"http://{usersHost}:{usersPort}", new GrpcChannelOptions() { Credentials = ChannelCredentials.Insecure });
            var channelRecord = GrpcChannel.ForAddress($"http://{recordHost}:{recordPort}");
            var channelSummary = GrpcChannel.ForAddress($"http://{summaryHost}:{summaryPort}");
            var clientUsers = new Users.UsersClient(channelUsers);
            var clientRecord = new RecordingService.RecordingServiceClient(channelRecord);
            var clientSummary = new PollSummaryService.PollSummaryServiceClient(channelSummary);


            var userName = "";
            if (interactive)
            {
                PrintPrompt("Enter a unique display name :");
                userName = Console.ReadLine();
            }
            else
            {
                var nameGenerator = new PersonNameGenerator();
                var firstName = nameGenerator.GenerateRandomFirstName();
                var lastName = nameGenerator.GenerateRandomLastName();
                userName = $"{firstName.Substring(0, 1).ToUpper()}. {lastName}";
                PrintInfo($"Using username : {userName}");
            }
            Console.WriteLine();

            PrintInfo($"Calling users service to create a new user {userName}");
            var userRequest = new UserRequest
            {
                Name = userName
            };
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(1));
            var userResponse = clientUsers.CreateUser(userRequest, new CallOptions(cancellationToken: cts.Token));

            if (userResponse.Error)
            {
                PrintError("Error while calling users service");
                PrintErrorAndExit(userResponse.ErrorMessage);
                return;
            }
            PrintInfo($"User created with code {userResponse.User.Code}");

            var userCode = userResponse.User.Code;

            PrintInfo("Getting open polls from record service");
            var pollsResponse = await clientRecord.GetPollsAsync(new Empty());
            if (pollsResponse.Error)
            {
                PrintErrorAndExit("Error while getting polls");
                return;
            }
            PrintInfo($"Got {pollsResponse.Polls.Count}");

            if (pollsResponse.Polls.Count == 0)
            {
                PrintInfo("Nothing to vote on :(");
                Console.WriteLine();
            }

            var random = new Random();
            foreach (var poll in pollsResponse.Polls)
            {
                Voting.Models.Option selectedOption;
                if (!interactive)
                {
                    PrintInfo($"Considering Poll : {poll.Summary}");
                    var randomOptionIdx = random.Next(poll.Options.Count);
                    selectedOption = poll.Options[randomOptionIdx];
                }
                else
                {
                    PrintInfo($"Cast your vote");
                    PrintPrompt(poll.Summary);
                    for (var i = 0; i < poll.Options.Count; i++)
                    {
                        PrintPrompt($"{i}) {poll.Options[i].Name}");
                    }
                    var selectedOptionInput = Console.ReadLine();
                    var selectedOptionIdx = 0;
                    if (!int.TryParse(selectedOptionInput, out selectedOptionIdx))
                    {
                        PrintErrorAndExit("Invalid input");
                        return;
                    }

                    if (selectedOptionIdx < 0 && selectedOptionIdx >= poll.Options.Count)
                    {
                        PrintErrorAndExit("Invalid input");
                        return;
                    }

                    selectedOption = poll.Options[selectedOptionIdx];
                }

                var vote = new Voting.Models.Vote()
                {
                    OptionId = selectedOption.Id,
                    PollId = poll.Id,
                    UserCode = userCode,
                    UserId = userName,
                };
                PrintInfo($"Voting for option : {selectedOption.Name}");
                PrintInfo("Recording vote");

                var recordVoteResponse = await clientRecord.RecordVoteAsync(vote);
                if (recordVoteResponse.Error)
                {
                    PrintErrorAndExit($"Error while recording vote : {recordVoteResponse.ErrorMessage}");
                    return;
                }

                var response = await clientSummary.GetPollSummaryAsync(poll);
                // PrintInfo($"{response.Value.Poll.Summary} : {response.Value.TotalVotes}");
                Console.WriteLine();
            }
        }

        static async Task Stream()
        {
            PrintInfo("Watching Polls");
            var recordHost = GetEnvVariableOrDefault("RECORD_SERVICE_HOST", "localhost");
            var recordPort = GetEnvVariableOrDefault("RECORD_SERVICE_PORT", "50000");
            var summaryHost = GetEnvVariableOrDefault("SUMMARY_SERVICE_HOST", "localhost");
            var summaryPort = GetEnvVariableOrDefault("SUMMARY_SERVICE_PORT", "50000");
            var channelRecord = GrpcChannel.ForAddress($"http://{recordHost}:{recordPort}");
            var channelSummary = GrpcChannel.ForAddress($"http://{summaryHost}:{summaryPort}");
            var clientRecord = new RecordingService.RecordingServiceClient(channelRecord);
            var clientSummary = new PollSummaryService.PollSummaryServiceClient(channelSummary);

            var pollsResponse = await clientRecord.GetPollsAsync(new Empty());
            if (pollsResponse.Error)
            {
                PrintErrorAndExit("Error while getting polls");
                return;
            }

            if (pollsResponse.Polls.Count == 0)
            {
                PrintErrorAndExit("No polls to watch");
                return;
            }

            var cts = new CancellationTokenSource();

            var tasks = new List<Task>();
            foreach (var poll in pollsResponse.Polls)
            {
                var stream = clientSummary.StreamPollSummary(poll, new CallOptions(cancellationToken: cts.Token));
                tasks.Add(ProcessStream(stream));
            }

            Task.WaitAll(tasks.ToArray());
        }

        static async Task ProcessStream(AsyncServerStreamingCall<PollSummary> call)
        {
            var stream = call.ResponseStream;
            await foreach (var poll in stream.ReadAllAsync())
            {
                PrintInfo($"{poll.Poll.Summary} : {poll.TotalVotes}");
            }
        }

        static void PrintErrorAndExit(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            System.Environment.Exit(1);
        }

        static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
        }

        static void PrintInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(message);
        }

        static void PrintPrompt(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine(message);
        }

        static string GetEnvVariableOrDefault(string variableName, string defaultValue)
        {
            var value = System.Environment.GetEnvironmentVariable(variableName);
            if (value == null || value == "")
            {
                return defaultValue;
            }

            return value;
        }
    }
}