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

            if (response.FileNames.Count == 0)
            {
                Console.WriteLine("No log files found in the directory.");
                return;
            }

            Console.WriteLine("Log files:");
            foreach (var fileName in response.FileNames)
            {
                Console.WriteLine($"  {fileName}");
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
                if (int.TryParse(input, out var degree) && degree >= 0)
                {
                    return degree;
                }
                Console.WriteLine("Invalid degree of parallelism, please try again.");
            }
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input file names separated by commas (e.g., basic.log,basic-multiple.log):");
            var input = Console.ReadLine();
            if (input is null)
            {
                return new List<string>();
            }

            var fileNames = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("No file names provided.");
            }
            return fileNames;
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                return;
            }

            var request = new AnalyzeFilesRequest()
            {
                DegreeOfParallelism = degree,
            };
            request.FileNames.AddRange(fileNames);

            var response = await client.AnalyzeFilesAsync(request);
            if (response.Status.Success)
            {
                Console.WriteLine("Analysis completed.");
            }
            else
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var response = await client.AnalyzeAllAsync(new AnalyzeAllRequest()
            {
                DegreeOfParallelism = degree,
            });

            if (response.Status.Success)
            {
                Console.WriteLine("Analysis completed.");
            }
            else
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input file name:");
            var fileName = Console.ReadLine();
            if (fileName is null)
            {
                return;
            }

            var request = new GetAnalysisResultRequest()
            {
                FileName = fileName,
            };

            using var call = client.GetAnalysisResult(request);
            var responses = await call.ResponseStream.ReadAllAsync().ToListAsync();

            if (responses.Count == 0)
            {
                Console.WriteLine("No response received.");
                return;
            }

            var first = responses[0];
            if (!first.Status.Success)
            {
                Console.WriteLine($"Error: {first.Status.Code}: {first.Status.Message}");
                return;
            }

            if (first.PayloadCase != GetAnalysisResultResponse.PayloadOneofCase.Header)
            {
                Console.WriteLine("Unexpected response.");
                return;
            }

            var header = first.Header;
            switch (header.State)
            {
                case AnalysisStateEnum.NotAnalyzed:
                    Console.WriteLine($"File '{fileName}' has not been analyzed yet.");
                    break;
                case AnalysisStateEnum.Failed:
                    Console.WriteLine($"File '{fileName}' failed to analyze: {header.ErrorMessage}");
                    break;
                case AnalysisStateEnum.Succeeded:
                    var visitor = new KeyValueVisitor();
                    foreach (var response in responses.Skip(1))
                    {
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        var kv = visitor.Dump(entry);
                        Console.WriteLine(string.Join(", ", kv.Select(pair => $"{pair.Key}={pair.Value}")));
                    }
                    break;
            }
        }
    }
}
