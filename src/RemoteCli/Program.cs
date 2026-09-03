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
            GetLogFilesResponse response;
            try
            {
                response = await client.GetLogFilesAsync(new Empty());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get log files: {ex.Message}");
                return;
            }

            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                return;
            }

            if (response.FileNames.Count == 0)
            {
                Console.WriteLine("There are no log files in the current directory.");
                return;
            }

            Console.WriteLine("Log files in the current directory:");
            foreach (var fileName in response.FileNames)
            {
                Console.WriteLine($"- {fileName}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism (0 for auto):");
                Console.Write(">>> ");
                Console.Out.Flush();
                var input = Console.ReadLine();
                if (int.TryParse(input, out var degree) && degree >= 0)
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
                Console.WriteLine("Please input file names to analyze (separated by commas):");
                Console.Write(">>> ");
                Console.Out.Flush();
                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("Invalid input, please try again.");
                    continue;
                }

                var fileNames = input.Split(',')
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
                if (fileNames.Count == 0)
                {
                    Console.WriteLine("Invalid input, please try again.");
                    continue;
                }
                return fileNames;
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            var request = new AnalyzeFilesRequest()
            {
                DegreeOfParallelism = degree,
            };
            request.FileNames.AddRange(fileNames);

            AnalyzeFilesResponse response;
            try
            {
                response = await client.AnalyzeFilesAsync(request);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
                return;
            }

            if (!response.Status.Success)
            {
                Console.WriteLine($"Analysis failed: {response.Status.Code}: {response.Status.Message}");
                return;
            }
            Console.WriteLine("Analysis completed.");
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var request = new AnalyzeAllRequest()
            {
                DegreeOfParallelism = degree,
            };

            AnalyzeAllResponse response;
            try
            {
                response = await client.AnalyzeAllAsync(request);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
                return;
            }

            if (!response.Status.Success)
            {
                Console.WriteLine($"Analysis failed: {response.Status.Code}: {response.Status.Message}");
                return;
            }
            Console.WriteLine("Analysis completed.");
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input a file name to query:");
            Console.Write(">>> ");
            Console.Out.Flush();
            var fileName = Console.ReadLine();
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
                            Console.WriteLine($"Analysis result for '{header.FileName}':");
                            Console.WriteLine($"  State: {header.State}, WorkerId: {header.WorkerId}");
                            if (header.HasErrorMessage)
                            {
                                Console.WriteLine($"  ErrorMessage: {header.ErrorMessage}");
                            }
                            if (header.State == AnalysisStateEnum.Succeeded)
                            {
                                Console.WriteLine("  Entries:");
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var kvResult = visitor.Dump(entry);
                            Console.WriteLine($"    {string.Join(", ", kvResult.Select(pair => $"{pair.Key}={pair.Value}"))}");
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
