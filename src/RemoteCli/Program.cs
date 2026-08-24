using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;
using Microsoft.Extensions.Logging;

namespace RemoteCli
{
    using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

    public class Program
    {
        static async Task Main(string[] args)
        {
            var address = args.FirstOrDefault()
                ?? Environment.GetEnvironmentVariable("LOG_ANALYZER_AGENT_ADDRESS")
                ?? "http://localhost:5000";
            Console.WriteLine($"Connecting to agent at {address}...");
            using var channel = GrpcChannel.ForAddress(address);
            var client = new LogAnalyzerAgentServiceClient(channel);
            _ = await client.PingAsync(new Empty());

            await ChooseAction(client);
        }

        private static async Task<bool> InputDirectory(LogAnalyzerAgentServiceClient client)
        {
            while (true)
            {
                Console.WriteLine("Please input directory containing log files:");
                var directory = Console.ReadLine()?.Trim();
                if (directory is null)
                {
                    return false;
                }
                var request = new ChangeDirectoryRequest()
                {
                    DirectoryPath = directory,
                };
                var response = await client.ChangeDirectoryAsync(request);
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}, please try again:");
                    continue;
                }
                break;
            }
            return true;
        }

        private static async Task ChooseAction(LogAnalyzerAgentServiceClient client)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("""
                Please choose:
                1. Show log files.
                2. Analyze specified log files.
                3. Analyze all log files.
                4. Get log file analysis result.
                5. Change directory.
                6. Exit.
                """);
                Console.Write(">>> ");
                Console.Out.Flush();

                int choice = 0;
                var choiceStr = Console.ReadLine();
                if (choiceStr is null)
                {
                    return;
                }
                try
                {
                    choice = int.Parse(choiceStr);
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid input, please try again.");
                    continue;
                }

                var actions = new Dictionary<int, Func<LogAnalyzerAgentServiceClient, Task>>
                {
                    { 1, ShowLogFiles },
                    { 2, AnalyzeFiles },
                    { 3, AnalyzeAll },
                    { 4, GetAnalysisResult }
                };
                switch (choice)
                {
                    case 1:
                        await actions[choice](client);
                        break;
                    case 2:
                        await actions[choice](client);
                        break;
                    case 3:
                        await actions[choice](client);
                        break;
                    case 4:
                        await actions[choice](client);
                        break;
                    case 5:
                        var success = await InputDirectory(client);
                        if (!success)
                        {
                            Console.WriteLine("Failed to change directory.");
                            return;
                        }
                        break;
                    case 6:
                        return;
                    default:
                        Console.WriteLine("Invalid choice, please try again.");
                        break;
                }
            }
        }

        private static async Task ShowLogFiles(LogAnalyzerAgentServiceClient client)
        {
            var response = await client.GetLogFilesAsync(new Empty());
            Console.WriteLine("Log files:");
            foreach (var fileName in response.FileNames)
            {
                Console.WriteLine($"  {fileName}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            int degreeOfParallelism = 0;
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism:");
                var input = Console.ReadLine()?.Trim();
                if (input is null)
                {
                    return 1;
                }
                try
                {
                    degreeOfParallelism = int.Parse(input);
                    if (degreeOfParallelism < 1 || degreeOfParallelism > 10)
                    {
                        Console.WriteLine("Invalid input, please try again.");
                        continue;
                    }
                    break;
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid input, please try again.");
                    continue;
                }
            }
            return degreeOfParallelism;
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input log file names, separated by space:");
            var input = Console.ReadLine()?.Trim();
            if (input is null)
            {
                return new List<string>();
            }
            return input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var request = new AnalyzeFilesRequest();

            request.FileNames.AddRange(ReadFileNames());
            request.DegreeOfParallelism = ReadDegreeOfParallelism();
            Console.WriteLine("Analyzing...");
            var response = await client.AnalyzeFilesAsync(request);
            Console.WriteLine($"Analysis result: {response.Status.Success}");
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var request = new AnalyzeAllRequest()
            {
                DegreeOfParallelism = ReadDegreeOfParallelism(),
            };
            Console.WriteLine("Analyzing...");
            var response = await client.AnalyzeAllAsync(request);
            Console.WriteLine($"Analysis result: {response.Status.Success}");
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Enter file name:");
            var fileName = Console.ReadLine();
            var request = new GetAnalysisResultRequest { FileName = fileName ?? string.Empty };
            using var call = client.GetAnalysisResult(request, cancellationToken: default);
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                switch (response.PayloadCase)
                {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        Console.WriteLine($"File: {response.Header.FileName}, State: {response.Header.State}, ErrorMessage: {response.Header.ErrorMessage ?? "N/A"}");
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        Console.WriteLine($"Log Entry: {response.LogEntry}");
                        break;
                    default:
                        Console.WriteLine("Unknown response type.");
                        break;
                }
            }

        }
    }
}
