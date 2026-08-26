using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;

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
            try
            {
                var response = await client.GetLogFilesAsync(new Empty());
                if (!PrintError(response.Status))
                {
                    return;
                }

                Console.WriteLine(response.FileNames.Count == 0 ? "No log files found." : "Log files:");
                foreach (var fileName in response.FileNames)
                {
                    Console.WriteLine($"- {fileName}");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC failed: {ex.Status.Detail}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Input degree of parallelism (0 = processor count):");
                var input = Console.ReadLine();
                if (input is null)
                {
                    return 0;
                }
                if (int.TryParse(input, out var value) && value >= 0)
                {
                    return value;
                }
                Console.WriteLine("Please input a non-negative integer.");
            }
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Input comma-separated log file names:");
            return (Console.ReadLine() ?? "")
                .Split(',')
                .Select(fileName => fileName.Trim())
                .Where(fileName => !string.IsNullOrEmpty(fileName))
                .Distinct()
                .ToList();
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("No file name was provided.");
                return;
            }

            var request = new AnalyzeFilesRequest
            {
                DegreeOfParallelism = ReadDegreeOfParallelism(),
            };
            request.FileNames.AddRange(fileNames);
            try
            {
                var response = await client.AnalyzeFilesAsync(request);
                if (PrintError(response.Status))
                {
                    Console.WriteLine("Analysis completed.");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC failed: {ex.Status.Detail}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            try
            {
                var response = await client.AnalyzeAllAsync(new AnalyzeAllRequest
                {
                    DegreeOfParallelism = ReadDegreeOfParallelism(),
                });
                if (PrintError(response.Status))
                {
                    Console.WriteLine("Analysis completed.");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC failed: {ex.Status.Detail}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Input a log file name:");
            var fileName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            try
            {
                using var call = client.GetAnalysisResult(new GetAnalysisResultRequest { FileName = fileName });
                AnalysisResultHeaderMessage? header = null;
                var visitor = new KeyValueVisitor();
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!PrintError(response.Status))
                    {
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            header = response.Header;
                            Console.WriteLine($"State: {header.State}");
                            if (header.State == AnalysisStateEnum.Failed)
                            {
                                Console.WriteLine($"Analysis failed: {header.ErrorMessage}");
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            Console.WriteLine(string.Join(", ", visitor.Dump(entry).Select(pair => $"{pair.Key}={pair.Value}")));
                            break;
                    }
                }

                if (header is not null && header.State == AnalysisStateEnum.NotAnalyzed)
                {
                    Console.WriteLine($"Log file '{fileName}' has not been analyzed.");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC failed: {ex.Status.Detail}");
            }
        }

        private static bool PrintError(OperationStatusMessage status)
        {
            if (status.Success)
            {
                return true;
            }
            Console.WriteLine($"Error: {status.Code}: {status.Message}");
            return false;
        }
    }
}
