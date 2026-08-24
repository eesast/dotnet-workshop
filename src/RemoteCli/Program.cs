using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

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
                Console.WriteLine($"Error: {response.Status.Code}: {response.Status.Message}");
                return;
            }

            if (response.FileNames.Count == 0)
            {
                Console.WriteLine("No log files found in the current directory.");
                return;
            }

            Console.WriteLine("Log files in directory:");
            foreach (var file in response.FileNames)
            {
                Console.WriteLine($"- {file}");
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            Console.WriteLine("Please input max degree of parallelism (0 for default):");
            var input = Console.ReadLine();
            if (int.TryParse(input, out int result) && result >= 0)
            {
                return result;
            }
            return 0;
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input log file names separated by comma:");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            return input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .Where(f => !string.IsNullOrEmpty(f))
                        .ToList();
        }

        private static async Task AnalyzeFiles(LogAnalyzerAgentServiceClient client)
        {
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("Input cannot be empty.");
                return;
            }

            var parallelism = ReadDegreeOfParallelism();

            Console.WriteLine("Analyzing specified files...");
            var request = new AnalyzeFilesRequest
            {
                DegreeOfParallelism = parallelism
            };
            request.FileNames.AddRange(fileNames);

            var response = await client.AnalyzeFilesAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error analyzing files: {response.Status.Code}: {response.Status.Message}");
            }
            else
            {
                Console.WriteLine("Analysis completed.");
            }
        }

        private static async Task AnalyzeAll(LogAnalyzerAgentServiceClient client)
        {
            var parallelism = ReadDegreeOfParallelism();

            Console.WriteLine("Analyzing all log files...");
            var request = new AnalyzeAllRequest
            {
                DegreeOfParallelism = parallelism
            };

            var response = await client.AnalyzeAllAsync(request);
            if (!response.Status.Success)
            {
                Console.WriteLine($"Error analyzing files: {response.Status.Code}: {response.Status.Message}");
            }
            else
            {
                Console.WriteLine("Analysis completed.");
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

            var request = new GetAnalysisResultRequest
            {
                FileName = fileName
            };

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
                        switch (header.State)
                        {
                            case AnalysisStateEnum.NotAnalyzed:
                                Console.WriteLine($"File '{fileName}' has not been analyzed yet.");
                                break;
                            case AnalysisStateEnum.Failed:
                                Console.WriteLine($"Analysis failed for '{fileName}':");
                                Console.WriteLine(header.ErrorMessage);
                                break;
                            case AnalysisStateEnum.Succeeded:
                                Console.WriteLine($"Analysis result for '{fileName}':");
                                break;
                        }
                        break;

                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        Console.WriteLine(entry);
                        break;
                }
            }
        }
    }
}
