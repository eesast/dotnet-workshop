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

            if (!await InputDirectory(client))
            {
                return;
            }

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

            if (response.FileNames.Count == 0)
            {
                Console.WriteLine("No log files found in the current directory.");
                return;
            }

            Console.WriteLine("Log files:");
            foreach (var fileName in response.FileNames)
            {
                Console.WriteLine(fileName);
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism (0 for auto):");
                var input = Console.ReadLine();
                if (input is null)
                {
                    return 0;
                }
                if (int.TryParse(input, out int degree) && degree >= 0)
                {
                    return degree;
                }
                Console.WriteLine("Invalid input, please try again.");
            }
        }

        private static List<string> ReadFileNames()
        {
            while (true)
            {
                Console.WriteLine("Please input file names to analyze (separated by comma):");
                var input = Console.ReadLine();
                if (input is null)
                {
                    return [];
                }

                var fileNames = input.Split(',')
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
                if (fileNames.Count > 0)
                {
                    return fileNames;
                }
                Console.WriteLine("No valid file names, please try again.");
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            while (true)
            {
                var degreeOfParallelism = ReadDegreeOfParallelism();
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
                if (response.Status.Success)
                {
                    Console.WriteLine("Analysis finished.");
                    return;
                }

                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                if (response.Status.Code is AgentErrorCode.FileNotFound or AgentErrorCode.InvalidArgument)
                {
                    continue;
                }
                return;
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            var response = await client.AnalyzeAllAsync(new AnalyzeAllRequest
            {
                DegreeOfParallelism = degreeOfParallelism,
            });
            if (response.Status.Success)
            {
                Console.WriteLine("Analysis finished.");
            }
            else
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            while (true)
            {
                Console.WriteLine("Please input file name to get analysis result:");
                var fileName = Console.ReadLine();
                if (fileName is null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(fileName))
                {
                    Console.WriteLine("Invalid file name, please try again.");
                    continue;
                }

                var request = new GetAnalysisResultRequest
                {
                    FileName = fileName,
                };
                using var call = client.GetAnalysisResult(request);
                var visitor = new KeyValueVisitor();
                bool shouldRetry = false;
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                        shouldRetry = response.Status.Code == AgentErrorCode.FileNotFound;
                        break;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            var header = response.Header;
                            switch (header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    Console.WriteLine($"File '{header.FileName}' has not been analyzed yet.");
                                    break;
                                case AnalysisStateEnum.Failed:
                                    Console.WriteLine($"File '{header.FileName}' analysis failed: {header.ErrorMessage}");
                                    break;
                                case AnalysisStateEnum.Succeeded:
                                    Console.WriteLine($"File '{header.FileName}' analysis succeeded:");
                                    break;
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var keyValues = visitor.Dump(entry)
                                .Select(pair => $"{pair.Key}: {pair.Value}");
                            Console.WriteLine(string.Join(", ", keyValues));
                            break;
                    }
                }
                if (shouldRetry)
                {
                    continue;
                }
                return;
            }
        }
    }
}
