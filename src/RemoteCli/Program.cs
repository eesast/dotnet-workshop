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

            try
            {
                using var channel = GrpcChannel.ForAddress(address);
                var client = new LogAnalyzerAgentServiceClient(channel);
                _ = await client.PingAsync(new Empty());

                await ChooseAction(client);
            }
            catch (RpcException ex)
            {
                PrintRpcError(ex);
            }
            catch (UriFormatException ex)
            {
                Console.WriteLine($"Invalid agent address: {ex.Message}");
            }
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
                if (!int.TryParse(choiceStr, out choice))
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
                try
                {
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
                catch (RpcException ex)
                {
                    PrintRpcError(ex);
                }
            }
        }

        private static async Task ShowLogFiles(LogAnalyzerAgentServiceClient client)
        {
            var response = await client.GetLogFilesAsync(new Empty());
            if (!response.Status.Success)
            {
                PrintOperationError(response.Status);
                return;
            }

            Console.WriteLine($"[{string.Join(", ", response.FileNames)}]");
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input the degree of parallelism (0 means auto):");
                Console.Write(">>> ");
                Console.Out.Flush();

                var input = Console.ReadLine();
                if (input is null)
                {
                    return 0;
                }

                if (int.TryParse(input, out var degreeOfParallelism) && degreeOfParallelism >= 0)
                {
                    return degreeOfParallelism;
                }

                Console.WriteLine("Invalid input, please try again.");
            }
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input file names to analyze, separated by commas:");
            Console.Write(">>> ");
            Console.Out.Flush();

            var input = Console.ReadLine();
            if (input is null)
            {
                return [];
            }

            return [.. input.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("No file names input.");
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
                PrintOperationError(response.Status);
                return;
            }

            Console.WriteLine($"Analysis finished: [{string.Join(", ", fileNames)}]");
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var request = new AnalyzeAllRequest
            {
                DegreeOfParallelism = ReadDegreeOfParallelism(),
            };

            var response = await client.AnalyzeAllAsync(request);
            if (!response.Status.Success)
            {
                PrintOperationError(response.Status);
                return;
            }

            Console.WriteLine("Analysis finished.");
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input the file name:");
            Console.Write(">>> ");
            Console.Out.Flush();

            var fileName = Console.ReadLine();
            if (fileName is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            using var call = client.GetAnalysisResult(new GetAnalysisResultRequest
            {
                FileName = fileName,
            });

            var receivedResponse = false;
            var dumper = new KeyValueVisitor();
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                receivedResponse = true;

                if (!response.Status.Success)
                {
                    PrintOperationError(response.Status);
                    return;
                }

                switch (response.PayloadCase)
                {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        PrintAnalysisHeader(response.Header);
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        var keyValuePairs = dumper.Dump(entry);
                        Console.WriteLine(string.Join(", ",
                            keyValuePairs.Select(pair => $"{pair.Key}: {pair.Value}")));
                        break;
                    default:
                        Console.WriteLine("Error: The agent returned an invalid analysis result.");
                        return;
                }
            }

            if (!receivedResponse)
            {
                Console.WriteLine("Error: The agent returned no analysis result.");
            }
        }

        private static void PrintAnalysisHeader(AnalysisResultHeaderMessage header)
        {
            switch (header.State)
            {
                case AnalysisStateEnum.NotAnalyzed:
                    Console.WriteLine($"File '{header.FileName}' has not been analyzed.");
                    break;
                case AnalysisStateEnum.Succeeded:
                    break;
                case AnalysisStateEnum.Failed:
                    var errorMessage = header.HasErrorMessage
                        ? header.ErrorMessage
                        : "Unknown error.";
                    Console.WriteLine($"Analysis of file '{header.FileName}' failed: {errorMessage}");
                    break;
                default:
                    Console.WriteLine($"Error: Unknown analysis state '{header.State}'.");
                    break;
            }
        }

        private static void PrintOperationError(OperationStatusMessage status)
        {
            Console.WriteLine($"Error: {status.Code}: {status.Message}");
        }

        private static void PrintRpcError(RpcException exception)
        {
            Console.WriteLine($"RPC failed: {exception.StatusCode}: {exception.Status.Detail}");
        }
    }
}
