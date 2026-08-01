using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
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
            try
            {
                _ = await client.PingAsync(new Empty());
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Failed to connect to agent at {address}: {ex.Status.StatusCode} - {ex.Status.Detail}");
                return;
            }
            Console.WriteLine("Connected.");

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
                ChangeDirectoryResponse response;
                try
                {
                    response = await client.ChangeDirectoryAsync(request);
                }
                catch (RpcException ex)
                {
                    Console.WriteLine($"Error: {ex.Status.StatusCode}: {ex.Status.Detail}, please try again:");
                    continue;
                }
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}, please try again:");
                    continue;
                }

                Console.WriteLine($"Current directory: {response.CurrentDirectory}");
                if (response.FileNames.Count == 0)
                {
                    Console.WriteLine("No log files found in this directory.");
                }
                else
                {
                    Console.WriteLine($"Log files ({response.FileNames.Count}):");
                    foreach (var file in response.FileNames)
                    {
                        Console.WriteLine($"  {file}");
                    }
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
            catch (RpcException ex)
            {
                Console.WriteLine($"Error: {ex.Status.StatusCode}: {ex.Status.Detail}");
                return;
            }

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

            Console.WriteLine($"Log files ({response.FileNames.Count}):");
            foreach (var file in response.FileNames)
            {
                Console.WriteLine($"  {file}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism (0 = ProcessorCount):");
                var line = Console.ReadLine();
                if (line is null)
                {
                    return 0;
                }
                if (int.TryParse(line.Trim(), out int degree) && degree >= 0)
                {
                    return degree;
                }
                Console.WriteLine("Invalid input, please input a non-negative integer, try again:");
            }
        }

        private static List<string> ReadFileNames()
        {
            while (true)
            {
                Console.WriteLine("Please input file names separated by ',' to analyze:");
                var line = Console.ReadLine();
                if (line is null)
                {
                    return new List<string>();
                }
                var fileNames = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (fileNames.Count == 0)
                {
                    Console.WriteLine("No file names provided, please try again:");
                    continue;
                }
                return fileNames;
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                return;
            }
            var degree = ReadDegreeOfParallelism();

            var request = new AnalyzeFilesRequest()
            {
                DegreeOfParallelism = degree,
            };
            request.FileNames.AddRange(fileNames);

            Console.WriteLine($"Analyzing {fileNames.Count} file(s) with parallelism {degree}...");
            try
            {
                var response = await client.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }
                Console.WriteLine("Analysis completed.");
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Error: {ex.Status.StatusCode}: {ex.Status.Detail}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var degree = ReadDegreeOfParallelism();
            var request = new AnalyzeAllRequest()
            {
                DegreeOfParallelism = degree,
            };

            Console.WriteLine($"Analyzing all log files with parallelism {degree}...");
            try
            {
                var response = await client.AnalyzeAllAsync(request);
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }
                Console.WriteLine("Analysis completed.");
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Error: {ex.Status.StatusCode}: {ex.Status.Detail}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input the file name to get analysis result:");
            var fileName = Console.ReadLine();
            if (fileName is null)
            {
                return;
            }
            fileName = fileName.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine("No file name provided.");
                return;
            }

            var request = new GetAnalysisResultRequest()
            {
                FileName = fileName,
            };

            var visitor = new KeyValueVisitor();
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
                            switch (response.Header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    Console.WriteLine($"File '{fileName}' has not been analyzed yet. Please analyze it first.");
                                    return;
                                case AnalysisStateEnum.Failed:
                                    Console.WriteLine($"Failed to analyze '{fileName}': {response.Header.ErrorMessage}");
                                    return;
                                case AnalysisStateEnum.Succeeded:
                                    Console.WriteLine($"Analysis result for '{fileName}' (parsed by worker {response.Header.WorkerId}):");
                                    break;
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var dump = visitor.Dump(entry);
                            Console.WriteLine($"  [{entry.EventType}] {string.Join(", ", dump.Select(kv => $"{kv.Key}={kv.Value}"))}");
                            break;
                    }
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Error: {ex.Status.StatusCode}: {ex.Status.Detail}");
            }
        }
    }
}
