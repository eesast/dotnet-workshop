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
                Console.WriteLine($"Failed to connect to agent: {ex.Status.Detail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start RemoteCli: {ex.Message}");
            }
        }

        private static bool HasOperationError(OperationStatusMessage? status)
        {
            if (status is null)
            {
                Console.WriteLine("Error: Agent returned a response without an operation status.");
                return true;
            }

            if (status.Success)
            {
                return false;
            }

            Console.WriteLine($"Error: {status.Code}: {status.Message}");
            return true;
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
                directory = directory.Trim();
                if (directory.Length == 0)
                {
                    Console.WriteLine("Directory cannot be empty, please try again:");
                    continue;
                }
                var request = new ChangeDirectoryRequest()
                {
                    DirectoryPath = directory,
                };
                var response = await client.ChangeDirectoryAsync(request);
                if (HasOperationError(response.Status))
                {
                    Console.WriteLine("Please try again:");
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

                var choiceStr = Console.ReadLine();
                if (choiceStr is null)
                {
                    return;
                }
                if (!int.TryParse(choiceStr, out var choice))
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
                catch (EndOfStreamException)
                {
                    return;
                }
                catch (RpcException ex)
                {
                    Console.WriteLine($"RPC failed: {ex.StatusCode}: {ex.Status.Detail}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Operation failed: {ex.Message}");
                }
            }
        }

        private static async Task ShowLogFiles(LogAnalyzerAgentServiceClient client)
        {
            var response = await client.GetLogFilesAsync(new Empty());
            if (HasOperationError(response.Status))
            {
                return;
            }

            Console.WriteLine($"[{string.Join(", ", response.FileNames)}]");
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism:");
                var input = Console.ReadLine();
                if (input is null)
                {
                    throw new EndOfStreamException("Input ended.");
                }

                if (int.TryParse(input.Trim(), out var degreeOfParallelism)
                    && degreeOfParallelism >= 0)
                {
                    return degreeOfParallelism;
                }

                Console.WriteLine("Invalid degree of parallelism, please try again:");
            }
        }

        private static List<string> ReadFileNames()
        {
            while (true)
            {
                Console.WriteLine("Please input log file names (comma separated):");
                var input = Console.ReadLine();
                if (input is null)
                {
                    throw new EndOfStreamException("Input ended.");
                }

                var fileNames = input
                    .Split(',')
                    .Select(fileName => fileName.Trim())
                    .Where(fileName => fileName.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (fileNames.Count > 0)
                {
                    return fileNames;
                }

                Console.WriteLine("No log file names provided, please try again:");
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();

            var response = await client.AnalyzeFilesAsync(new AnalyzeFilesRequest
            {
                DegreeOfParallelism = degreeOfParallelism,
                FileNames = { fileNames }
            });
            if (HasOperationError(response.Status))
            {
                return;
            }

            Console.WriteLine($"Analysis completed: [{string.Join(", ", fileNames)}]");
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            var filesResponse = await client.GetLogFilesAsync(new Empty());
            if (HasOperationError(filesResponse.Status))
            {
                return;
            }

            var response = await client.AnalyzeAllAsync(new AnalyzeAllRequest
            {
                DegreeOfParallelism = degreeOfParallelism,
            });
            if (HasOperationError(response.Status))
            {
                return;
            }

            Console.WriteLine(
                $"Analysis completed: [{string.Join(", ", filesResponse.FileNames)}]");
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input log file name:");
            var input = Console.ReadLine();
            if (input is null)
            {
                return;
            }

            var fileName = input.Trim();
            if (fileName.Length == 0)
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            var request = new GetAnalysisResultRequest { FileName = fileName };
            AnalysisResultHeaderMessage? header = null;
            var entries = new List<LogEntryMessage>();
            using var call = client.GetAnalysisResult(request);
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                if (HasOperationError(response.Status))
                {
                    return;
                }

                switch (response.PayloadCase)
                {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        header = response.Header;
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        entries.Add(response.LogEntry);
                        break;
                    default:
                        Console.WriteLine("Error: Agent returned an empty analysis payload.");
                        return;
                }
            }

            if (header is null)
            {
                Console.WriteLine("Error: Agent did not return an analysis result header.");
                return;
            }

            switch (header.State)
            {
                case AnalysisStateEnum.NotAnalyzed:
                    Console.WriteLine($"File {fileName} has not been analyzed yet.");
                    break;

                case AnalysisStateEnum.Failed:
                    Console.WriteLine(
                        $"Analysis failed for {fileName}: "
                        + (header.HasErrorMessage ? header.ErrorMessage : "Unknown error"));
                    break;

                case AnalysisStateEnum.Succeeded:
                    Console.WriteLine($"Analysis result for {fileName}:");
                    var visitor = new KeyValueVisitor();
                    foreach (var entryMessage in entries)
                    {
                        var entry = GrpcTypeConverter.ConvertFromGrpc(entryMessage);
                        var values = visitor.Dump(entry);
                        Console.WriteLine(string.Join(", ",
                            values.Select(pair => $"{pair.Key}: {pair.Value}")));
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown analysis state for {fileName}: {header.State}");
                    break;
            }
        }
    }
}
