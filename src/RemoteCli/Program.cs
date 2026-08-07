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
                var directory = Console.ReadLine();
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
                    case 2:
                    case 3:
                    case 4:
                        await actions[choice](client);
                        break;
                    case 5:
                        var success = await InputDirectory(client);
                        if (!success)
                        {
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
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                return;
            }

            Console.WriteLine($"[{string.Join(", ", response.FileNames)}]");
        }

        private static int ReadDegreeOfParallelism()
        {
            Console.WriteLine("Please input degree of parallelism (0 uses the logical processor count):");
            var input = Console.ReadLine();
            if (input is null)
            {
                return -1;
            }

            if (!int.TryParse(input.Trim(), out var degreeOfParallelism)
                || degreeOfParallelism < 0)
            {
                Console.WriteLine("Degree of parallelism must be a non-negative integer.");
                return -1;
            }

            return degreeOfParallelism;
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input log file names (comma separated):");
            var input = Console.ReadLine();
            if (input is null)
            {
                return new List<string>();
            }

            var fileNames = input
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("At least one log file name is required.");
                return new List<string>();
            }

            return fileNames;
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            if (degreeOfParallelism < 0)
            {
                return;
            }

            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                return;
            }

            var request = new AnalyzeFilesRequest
            {
                DegreeOfParallelism = degreeOfParallelism,
            };
            request.FileNames.AddRange(fileNames);
            var response = await client.AnalyzeFilesAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Unable to analyze files: {response.Status.Code}: {response.Status.Message}");
                return;
            }

            Console.WriteLine($"Analysis completed: [{string.Join(", ", fileNames)}]");
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            if (degreeOfParallelism < 0)
            {
                return;
            }

            var response = await client.AnalyzeAllAsync(new AnalyzeAllRequest
            {
                DegreeOfParallelism = degreeOfParallelism,
            });
            if (!response.Status.Success)
            {
                Console.WriteLine($"Unable to analyze files: {response.Status.Code}: {response.Status.Message}");
                return;
            }

            var filesResponse = await client.GetLogFilesAsync(new Empty());
            IEnumerable<string> fileNames = filesResponse.Status.Success
                ? filesResponse.FileNames
                : Array.Empty<string>();
            Console.WriteLine($"Analysis completed: [{string.Join(", ", fileNames)}]");
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input log file name:");
            var fileName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("Log file name cannot be empty.");
                return;
            }

            using var call = client.GetAnalysisResult(new GetAnalysisResultRequest
            {
                FileName = fileName.Trim(),
            });
            var visitor = new KeyValueVisitor();
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }

                switch (response.PayloadCase)
                {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        var header = response.Header;
                        switch (header.State)
                        {
                            case AnalysisStateEnum.NotAnalyzed:
                                Console.WriteLine($"File {header.FileName} has not been analyzed yet.");
                                break;
                            case AnalysisStateEnum.Failed:
                                Console.WriteLine($"Analysis failed for {header.FileName}: {header.ErrorMessage}");
                                break;
                            case AnalysisStateEnum.Succeeded:
                                Console.WriteLine($"Analysis result for {header.FileName}:");
                                break;
                            default:
                                Console.WriteLine($"File {header.FileName} has an unknown analysis state.");
                                break;
                        }
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        var keyValues = visitor.Dump(GrpcTypeConverter.ConvertFromGrpc(response.LogEntry));
                        Console.WriteLine(string.Join(", ",
                            keyValues.Select(keyValue => $"{keyValue.Key}: {keyValue.Value}")));
                        break;
                }
            }
        }
    }
}
