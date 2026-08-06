using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to agent: {ex.Message}");
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

            // 按照参考截图输出格式 [file1, file2]
            Console.WriteLine($"[{string.Join(", ", response.FileNames)}]");
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism:");
                var input = Console.ReadLine();

                // 确保输入是数字并且不小于 0（通常 0 代表自动配置，正数代表具体线程数）
                if (int.TryParse(input, out int dop) && dop >= 0)
                {
                    return dop;
                }
                Console.WriteLine("Invalid input, please try again.");
            }
        }

        private static List<string> ReadFileNames()
        {
            while (true)
            {
                Console.WriteLine("Please input log file names (comma separated):");
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Invalid input, please try again.");
                    continue;
                }

                // 拆分字符串，去除两端空格，并忽略空元素
                var files = input.Split(',')
                                 .Select(s => s.Trim())
                                 .Where(s => !string.IsNullOrEmpty(s))
                                 .ToList();

                if (files.Count == 0)
                {
                    Console.WriteLine("No valid file names found, please try again.");
                    continue;
                }

                return files;
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            int dop = ReadDegreeOfParallelism();
            List<string> files = ReadFileNames();

            var request = new AnalyzeFilesRequest
            {
                DegreeOfParallelism = dop
            };
            request.FileNames.AddRange(files);

            var response = await client.AnalyzeFilesAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
            else
            {
                Console.WriteLine($"Analysis completed: [{string.Join(", ", files)}]");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            int dop = ReadDegreeOfParallelism();
            var request = new AnalyzeAllRequest
            {
                DegreeOfParallelism = dop
            };

            var response = await client.AnalyzeAllAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
            }
            else
            {
                Console.WriteLine("Analysis completed.");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            Console.WriteLine("Please input log file name:");
            var fileName = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("Invalid input.");
                return;
            }

            var request = new GetAnalysisResultRequest
            {
                FileName = fileName
            };

            try
            {
                // 获取流式调用对象
                using var call = client.GetAnalysisResult(request);

                // 使用 await foreach 迭代读取服务端流
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                        return;
                    }

                    // 处理头部信息
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                    {
                        var header = response.Header;
                        if (header.State == AnalysisStateEnum.NotAnalyzed)
                        {
                            Console.WriteLine($"File {fileName} has not been analyzed yet.");
                            return; // 还没分析，直接退出
                        }
                        if (header.State == AnalysisStateEnum.Failed)
                        {
                            Console.WriteLine($"File {fileName} analysis failed: {header.ErrorMessage}");
                            return;
                        }
                    }
                    // 处理日志条目
                    else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        var entry = response.LogEntry;
                        string output = "";

                        // 根据具体的 oneof 类型输出日志
                        switch (entry.EntryCase)
                        {
                            case LogEntryMessage.EntryOneofCase.CallLogEntry:
                                var c = entry.CallLogEntry;
                                output = $"LineNo: {c.LineNo}, Timestamp: {c.Timestamp.ToDateTimeOffset():O}, PodName: {c.PodName}, Severity: {c.Severity}, EventType: {c.EventType}, RequestId: {c.RequestId}, TargetService: {c.TargetService}, DurationMs: {c.DurationMs}";
                                break;
                            case LogEntryMessage.EntryOneofCase.RequestLogEntry:
                                var r = entry.RequestLogEntry;
                                output = $"LineNo: {r.LineNo}, Timestamp: {r.Timestamp.ToDateTimeOffset():O}, PodName: {r.PodName}, Severity: {r.Severity}, EventType: {r.EventType}, RequestId: {r.RequestId}, Method: {r.Method}, Path: {r.Path}, StatusCode: {r.StatusCode}";
                                break;
                            case LogEntryMessage.EntryOneofCase.InternalLogEntry:
                                var i = entry.InternalLogEntry;
                                output = $"LineNo: {i.LineNo}, Timestamp: {i.Timestamp.ToDateTimeOffset():O}, PodName: {i.PodName}, Severity: {i.Severity}, EventType: {i.EventType}, ExceptionName: {i.ExceptionName}, ExceptionMessage: {i.ExceptionMessage}";
                                break;
                        }

                        Console.WriteLine(output);
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