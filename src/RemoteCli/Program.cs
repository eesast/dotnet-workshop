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
                Console.WriteLine($"Error: {response.Status.Message}");
                return;
            }
            var files=response.FileNames;
            if (files.Count == 0)
            {
                Console.WriteLine("No log files found in the current directory.");
                return;
            }
            int index = 1;
            foreach (var file in files)
            {
                Console.WriteLine($"{index}.{file}");
                index++;
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            int degree = 0;
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism:");
                var degreeStr = Console.ReadLine();
                if (degreeStr is null) return 0; 
                if (!int.TryParse(degreeStr, out degree))
                {
                    Console.WriteLine("Invalid number, please try again.");
                    continue;
                }
                break;
            }
            return degree;
        }

        private static List<string> ReadFileNames()
        {
            while (true)
            {
                Console.WriteLine("Please input log files to analyze (separated by comma):");
                var str = Console.ReadLine();
                if (str is null) return new List<string>();
                var selectedFiles = str.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(f => f.Trim())
                                    .ToList();
                if (selectedFiles.Count == 0)
                {
                    Console.WriteLine("No files specified, please try again.");
                    continue;
                }
                return selectedFiles;
            }
                }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var files = ReadFileNames();
            if (files.Count == 0) return;
            int degree = ReadDegreeOfParallelism();
            if (degree == 0) return;
            var request = new AnalyzeFilesRequest { DegreeOfParallelism = degree };
            request.FileNames.AddRange(files);
            var response = await client.AnalyzeFilesAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Message}");
            }
            else
            {
                Console.WriteLine("Analysis started successfully.");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            int degree = ReadDegreeOfParallelism();
            if(degree==0){return;}
            var request = new AnalyzeAllRequest { DegreeOfParallelism = degree };
            var response = await client.AnalyzeAllAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Message}");
            }
            else
            {
                Console.WriteLine("Analysis started successfully.");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input log file name:");
            var fileName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine("Invalid file name.");
                return;
            }
            var request = new GetAnalysisResultRequest { FileName = fileName };
            using var call = client.GetAnalysisResult(request);
            var visitor = new KeyValueVisitor();
            try
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        Console.WriteLine($"Error: {response.Status.Message}");
                        return;
                    }
                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            switch (response.Header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    Console.WriteLine("Not analyzed.");
                                    break;
                                case AnalysisStateEnum.Failed:
                                    Console.WriteLine($"Failed: {response.Header.ErrorMessage}");
                                    break;
                                case AnalysisStateEnum.Succeeded:
                                    break;
                            }
                            break;

                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var logDict = visitor.Dump(entry);
                            foreach (var kvp in logDict)
                            {
                                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                            }
                            Console.WriteLine("------------------------");
                            break;
                    }
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC Error: {ex.Status.Detail}");
            }
                }
            }
}
