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
            // TODO: T3.2
            try
            {
                var response = await client.GetLogFilesAsync(new Empty());
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }
                Console.WriteLine("Log files in agent's current directory:");
                if (response.FileNames.Count == 0)
                {
                    Console.WriteLine("  (no .log files found)");
                    return;
                }
                foreach (var fileName in response.FileNames)
                {
                    Console.WriteLine($"  {fileName}");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC error: {ex.StatusCode}: {ex.Message}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            // TODO: T3.2
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism (0 = auto):");
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
            // TODO: T3.2
            while (true)
            {
                Console.WriteLine("Please input file names separated by commas:");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("No file name given, please try again.");
                    continue;
                }
                var fileNames = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                if (fileNames.Count == 0)
                {
                    Console.WriteLine("No file name given, please try again.");
                    continue;
                }
                return fileNames;
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            // TODO: T3.2
            var degree = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            try
            {
                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degree,
                };
                request.FileNames.AddRange(fileNames);
                var response = await client.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }
                Console.WriteLine("Analysis finished.");
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC error: {ex.StatusCode}: {ex.Message}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            // TODO: T3.2
            var degree = ReadDegreeOfParallelism();
            try
            {
                var request = new AnalyzeAllRequest()
                {
                    DegreeOfParallelism = degree,
                };
                var response = await client.AnalyzeAllAsync(request);
                if (!response.Status.Success)
                {
                    Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                    return;
                }
                Console.WriteLine("Analysis finished.");
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC error: {ex.StatusCode}: {ex.Message}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            // TODO: T3.2
            Console.WriteLine("Please input the file name:");
            var fileName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("No file name given.");
                return;
            }
            try
            {
                var request = new GetAnalysisResultRequest()
                {
                    FileName = fileName.Trim(),
                };
                using var call = client.GetAnalysisResult(request);
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                        return;
                    }
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                    {
                        var header = response.Header;
                        Console.WriteLine($"--- {header.FileName} ({header.FullName}) ---");
                        Console.WriteLine($"State: {header.State}, Worker: {header.WorkerId}");
                        if (!string.IsNullOrEmpty(header.ErrorMessage))
                        {
                            Console.WriteLine($"Error message: {header.ErrorMessage}");
                        }
                    }
                    else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        var visitor = new KeyValueVisitor();
                        var dict = visitor.Dump(entry);
                        Console.WriteLine(string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}")));
                    }
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC error: {ex.StatusCode}: {ex.Message}");
            }
        }
    }
}
