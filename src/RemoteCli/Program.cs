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
                    Console.WriteLine($"获取文件列表失败: {response.Status.Message}");
                    return;
                }

                if (response.FileNames.Count == 0)
                {
                    Console.WriteLine("当前目录下没有 .log 文件。");
                    return;
                }

                Console.WriteLine("文件列表:");
                foreach (var fileName in response.FileNames)
                {
                    Console.WriteLine($"  - {fileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取文件列表时发生错误: {ex.Message}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.Write("请输入并行度（输入 0 表示使用 CPU 核心数）: ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("输入不能为空，请重新输入。");
                    continue;
                }

                if (int.TryParse(input, out int result))
                {
                    if (result >= 0)
                    {
                        return result;
                    }
                    Console.WriteLine("并行度不能为负数，请重新输入。");
                }
                else
                {
                    Console.WriteLine("请输入一个有效的整数。");
                }
            }
        }

        private static List<string> ReadFileNames()
        {
             while (true)
            {
                Console.Write("请输入要分析的文件名（多个用逗号分隔）: ");
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("输入不能为空，请重新输入。");
                    continue;
                }

                var names = input.Split(',')
                                 .Select(n => n.Trim())
                                 .Where(n => !string.IsNullOrEmpty(n))
                                 .ToList();

                if (names.Count == 0)
                {
                    Console.WriteLine("未指定任何有效文件名，请重新输入。");
                    continue;
                }

                return names;
            }
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            try
            {
                // 读取文件名列表
                var fileNames = ReadFileNames();
                
                // 读取并行度
                var degreeOfParallelism = ReadDegreeOfParallelism();

                // 构建请求
                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = degreeOfParallelism
                };
                request.FileNames.AddRange(fileNames);

                // 发送分析请求
                Console.WriteLine($"正在分析 {fileNames.Count} 个文件，并行度 = {degreeOfParallelism}...");
                var response = await client.AnalyzeFilesAsync(request);

                if (response.Status.Success)
                {
                    Console.WriteLine("分析任务已执行完毕。");
                }
                else
                {
                    Console.WriteLine($"分析失败: {response.Status.Code}: {response.Status.Message}");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"gRPC 调用失败: {ex.Status.StatusCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"分析过程中发生错误: {ex.Message}");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            try
            {
                // 读取并行度
                var degreeOfParallelism = ReadDegreeOfParallelism();

                // 构建请求
                var request = new AnalyzeAllRequest
                {
                    DegreeOfParallelism = degreeOfParallelism
                };

                // 发送分析请求
                Console.WriteLine($"正在分析所有文件，并行度 = {degreeOfParallelism}...");
                var response = await client.AnalyzeAllAsync(request);

                if (response.Status.Success)
                {
                    Console.WriteLine("全部分析任务已执行完毕。");
                }
                else
                {
                    Console.WriteLine($"分析失败: {response.Status.Code}: {response.Status.Message}");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"gRPC 调用失败: {ex.Status.StatusCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"分析过程中发生错误: {ex.Message}");
            }
        }

        private static async Task GetAnalysisResult(LogAnalyzerAgentServiceClient client)
        {
            try
            {
                Console.Write("请输入要查看的文件名: ");
                var fileName = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Console.WriteLine("文件名不能为空。");
                    return;
                }

                var request = new GetAnalysisResultRequest
                {
                    FileName = fileName
                };

                // ⭐ 流式调用：使用客户端流式读取
                using var call = client.GetAnalysisResult(request);
                var responseStream = call.ResponseStream;

                bool hasReceivedHeader = false;
                bool isSuccess = true;

                await foreach (var response in responseStream.ReadAllAsync())
                {
                    // 先检查状态
                    if (!response.Status.Success)
                    {
                        Console.WriteLine($"获取结果失败: {response.Status.Code}: {response.Status.Message}");
                        isSuccess = false;
                        break;
                    }

                    // 判断是 Header 还是 LogEntry
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                    {
                        var header = response.Header;
                        hasReceivedHeader = true;
                        Console.WriteLine($"=== 文件: {header.FileName} ===");
                        Console.WriteLine($"状态: {header.State}");
                        Console.WriteLine($"Worker ID: {header.WorkerId}");
                        Console.WriteLine($"日志条目数: (正在接收...)");
                        Console.WriteLine();
                    }
                    else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        // 将 Protobuf 消息转回 C# 对象
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        
                        // 使用 KeyValueVisitor 输出键值对
                        var visitor = new KeyValueVisitor();
                        var dict = visitor.Dump(entry);
                        Console.WriteLine(string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}")));
                    }
                }

                if (isSuccess && !hasReceivedHeader)
                {
                    Console.WriteLine("未收到任何数据。");
                }
                else if (isSuccess)
                {
                    Console.WriteLine();
                    Console.WriteLine("=== 结果输出完毕 ===");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"gRPC 调用失败: {ex.Status.StatusCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取分析结果时发生错误: {ex.Message}");
            }
        }
    }
}
