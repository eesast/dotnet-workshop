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
            if(!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                return;
            }
            if(response.FileNames.Count == 0)
            {
                Console.WriteLine("No log files found.");
                return;
            }
            foreach (var file in response.FileNames)
            {
                Console.WriteLine(file);
            }
            return;
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true) { 
                Console.WriteLine("Please input degree of parallelism:");
                Console.Write(">>> ");
                var dopStr = Console.ReadLine();
                if (dopStr is null)
                {
                    return 0;
                }
                if(int.TryParse(dopStr, out int dop) && dop > 0)
                {
                    return dop;
                }
                Console.WriteLine("Invalid input, please try again.");
            }
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input log file names (separated by commas):");
            Console.Write(">>> ");
            var fileNamesStr = Console.ReadLine();
            if(fileNamesStr is null)
            {
                return new List<string>();
            }
            return fileNamesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var dop = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            var request = new AnalyzeFilesRequest()
            {
                DegreeOfParallelism = dop,
            };
            request.FileNames.AddRange(fileNames);
            var response = await client.AnalyzeFilesAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                return;
            }
            Console.WriteLine("Analysis Completed Successfully.");
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var dop = ReadDegreeOfParallelism();
            var request = new AnalyzeAllRequest()
            {
                DegreeOfParallelism = dop,
            };
            var response = await client.AnalyzeAllAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                return;
            }
            Console.WriteLine("Analysis Completed Successfully.");
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input log file name to get analysis result:");
            Console.Write(">>> ");
            var fileName = Console.ReadLine();
            if (fileName is null) return;
            using var call = client.GetAnalysisResult(new GetAnalysisResultRequest()
            {
                FileName = fileName,
            });
            while (await call.ResponseStream.MoveNext())
            {
                var result = call.ResponseStream.Current;
                switch (result.PayloadCase) {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        Console.WriteLine($"Result of {result.Header.FileName}: State = {result.Header.State}");
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        var entry = result.LogEntry;
                        switch (entry.EntryCase)
                        {
                            case LogEntryMessage.EntryOneofCase.CallLogEntry:
                                Console.WriteLine($"  line {entry.CallLogEntry.LineNo}: call {entry.CallLogEntry.TargetService}");
                                break;
                            case LogEntryMessage.EntryOneofCase.RequestLogEntry:
                                Console.WriteLine($"  line {entry.RequestLogEntry.LineNo}: {entry.RequestLogEntry.Method} {entry.RequestLogEntry.Path} -> {entry.RequestLogEntry.StatusCode}");
                                break;
                            case LogEntryMessage.EntryOneofCase.InternalLogEntry:
                                Console.WriteLine($"  line {entry.InternalLogEntry.LineNo}: {entry.InternalLogEntry.ExceptionName}");
                                break;
                        }
                        break;
                }
            }
        }
    }
}
