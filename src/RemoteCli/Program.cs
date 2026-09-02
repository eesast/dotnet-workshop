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
            try
            {
                var response = await client.GetLogFilesAsync(new Empty());
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }
                Console.WriteLine("Log files:");
                foreach (var file in response.FileNames)
                {
                    Console.WriteLine($" - {file}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get log files: {ex.Message}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            Console.WriteLine("Please input degree of parallelism (0 for processor count):");
            while (true)
            {
                var input = Console.ReadLine();
                if (int.TryParse(input, out int degree))
                {
                    return degree;
                }
                Console.WriteLine("Invalid input, please try again:");
            }
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input file names separated by comma (e.g. log1.log,log2.log):");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }
            return input.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("No file names provided.");
                return;
            }
            var request = new AnalyzeFilesRequest()
            {
                DegreeOfParallelism = degree,
            };
            request.FileNames.AddRange(fileNames);
            try
            {
                var response = await client.AnalyzeFilesAsync(request);
                if (response.Status.Success)
                {
                    Console.WriteLine("Analysis finished.");
                }
                else
                {
                    Console.WriteLine($"Analysis failed: {response.Status.Code}: {response.Status.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var request = new AnalyzeAllRequest()
            {
                DegreeOfParallelism = degree,
            };
            try
            {
                var response = await client.AnalyzeAllAsync(request);
                if (response.Status.Success)
                {
                    Console.WriteLine("All files analysis finished.");
                }
                else
                {
                    Console.WriteLine($"Analysis failed: {response.Status.Code}: {response.Status.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input file name:");
            var fileName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }
            var request = new GetAnalysisResultRequest()
            {
                FileName = fileName,
            };
            try
            {
                using var call = client.GetAnalysisResult(request);
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
                            Console.WriteLine($"File: {header.FileName}");
                            Console.WriteLine($"  State: {header.State}");
                            if (header.State == AnalysisStateEnum.NotAnalyzed)
                            {
                                Console.WriteLine("  File has not been analyzed.");
                            }
                            else if (header.State == AnalysisStateEnum.Failed)
                            {
                                Console.WriteLine($"  Analysis failed: {header.ErrorMessage}");
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var visitor = new KeyValueVisitor();
                            var dict = visitor.Dump(entry);
                            foreach (var kv in dict)
                            {
                                Console.WriteLine($"  {kv.Key}: {kv.Value}");
                            }
                            Console.WriteLine();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get analysis result: {ex.Message}");
            }
        }
    }
}
