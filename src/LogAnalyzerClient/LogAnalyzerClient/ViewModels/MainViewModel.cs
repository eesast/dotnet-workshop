using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LogAnalyzerClient.ViewModels
{
    using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

    public partial class MainViewModel : ViewModelBase
    {
        internal IDialogHelper DialogHelper { get; set; } = new NullDialogHelper();

        private LogAnalyzerAgentServiceClient? _client = null;

        public IReadOnlyList<string> SelectedFiles { get; set; } = new List<string>();

        [ObservableProperty]
        private string _greeting = "Welcome to Avalonia!";

        [ObservableProperty]
        private string _directoryPath = "";

        [ObservableProperty]
        private string _degreeOfParallelismText = "1";

        [ObservableProperty]
        private string _currentAddress = "";
        private static class ConnectStatusString
        {
            public const string NOT_CONNECTED = "Not connected.";
            public const string CONNECTING = "Connecting...";
            public const string CONNECTED = "Connected.";
            public const string CONNECT_FAILED = "Connect failed.";
        }
        [ObservableProperty]
        private string _connectStatus = ConnectStatusString.NOT_CONNECTED;

        [ObservableProperty]
        private ObservableCollection<LogFileItem> _logFiles = new();

        [ObservableProperty]
        private LogFileItem? _selectedLogFile = null;

        [ObservableProperty]
        private ObservableCollection<LogFields> _resultEntries = new();

        [RelayCommand]
        private async Task ConnectAsync()
        {
            var address = await DialogHelper.ShowConnectDialogAsync(CurrentAddress);
            if (address is null)
            {
                // Do nothing if the user cancels the dialog
            }
            else if (string.IsNullOrEmpty(address.Trim()))
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Address cannot be empty.");
            }
            else
            {
                try
                {
                    ConnectStatus = ConnectStatusString.CONNECTING;
                    _client = AppService.ClientFactory.CreateClient(address);
                    await _client.PingAsync(new Empty());
                    CurrentAddress = address;
                    ConnectStatus = ConnectStatusString.CONNECTED;
                    LogFiles.Clear();
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatusString.CONNECT_FAILED;
                    await DialogHelper.ShowMessageDialogAsync("Error", $"Failed to connect to agent: {ex.Message}");
                    ConnectStatus = ConnectStatusString.NOT_CONNECTED;
                }
            }
        }

        private async Task WithClientNotNull(Func<Task> action)
        {
            if (_client is null)
            {
                await DialogHelper.ShowMessageDialogAsync("Error",
                    "Agent is not connected. Please connect to an agent first.");
            }
            else
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"Error occurred: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private async Task ChangeDirectoryAsync()
        {
            await WithClientNotNull(async() =>
            {
                var request = new ChangeDirectoryRequest()
                {
                    DirectoryPath = DirectoryPath,
                };
                var response = await _client!.ChangeDirectoryAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                await RefreshAsync();
            });
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 发起 gRPC 调用请求文件列表
                var response = await _client!.GetLogFilesAsync(new Empty());

                // 检查服务端是否返回错误
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                // 请求成功，清空旧列表并重新填入新获取的文件列表
                LogFiles.Clear();
                foreach (var fileName in response.FileNames)
                {
                    // 注意：这里的实例化方式取决于您在 RemoteModels.cs 中对 LogFileItem 的定义
                    // 如果它是 record LogFileItem(string FileName)，这样写即可：
                    LogFiles.Add(new LogFileItem(fileName));
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeSelectedFilesAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 1. 检查是否有选中的文件
                if (SelectedFiles == null || SelectedFiles.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "Please select at least one log file to analyze.");
                    return;
                }

                // 2. 解析并行度 (DegreeOfParallelism)
                if (!int.TryParse(DegreeOfParallelismText, out int dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Invalid Degree of Parallelism. Please enter a non-negative integer.");
                    return;
                }

                // 3. 构造 gRPC 请求
                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = dop
                };
                request.FileNames.AddRange(SelectedFiles);

                // 4. 发起异步 RPC 调用
                var response = await _client!.AnalyzeFilesAsync(request);

                // 5. 处理响应结果
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Success",
                        $"Successfully started analysis for {SelectedFiles.Count} file(s).");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 1. 解析并行度 (DegreeOfParallelism)
                if (!int.TryParse(DegreeOfParallelismText, out int dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Invalid Degree of Parallelism. Please enter a non-negative integer.");
                    return;
                }

                // 2. 构造全量分析的 gRPC 请求
                var request = new AnalyzeAllRequest
                {
                    DegreeOfParallelism = dop
                };

                // 3. 发起异步 RPC 调用
                var response = await _client!.AnalyzeAllAsync(request);

                // 4. 处理响应结果
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Success",
                        "Successfully started analysis for all files.");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 1. 检查当前右键绑定的文件是否存在
                if (SelectedLogFile == null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "No file selected. Please right-click a log file to analyze.");
                    return;
                }

                // 2. 解析并行度 (DegreeOfParallelism)
                if (!int.TryParse(DegreeOfParallelismText, out int dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Invalid Degree of Parallelism. Please enter a non-negative integer.");
                    return;
                }

                // 3. 构造 gRPC 请求（将右键选中的单个文件名加入请求）
                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = dop
                };
                request.FileNames.Add(SelectedLogFile.FileName); // 从 SelectedLogFile 取出 FileName

                // 4. 发起异步 RPC 调用
                var response = await _client!.AnalyzeFilesAsync(request);

                // 5. 处理响应结果
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Success",
                        $"Successfully started analysis for {SelectedLogFile.FileName}.");
                }
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 1. 确保有文件被右键选中
                if (SelectedLogFile == null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "No file selected. Please right-click a log file first.");
                    return;
                }

                // 2. 清空旧的显示结果
                ResultEntries.Clear();

                // 3. 构造请求
                var request = new GetAnalysisResultRequest
                {
                    FileName = SelectedLogFile.FileName
                };

                try
                {
                    // 4. 获取流式调用对象
                    using var call = _client!.GetAnalysisResult(request);

                    int entryIndex = 0;

                    // 5. 使用 await foreach 迭代读取服务端流
                    await foreach (var response in call.ResponseStream.ReadAllAsync())
                    {
                        if (!response.Status.Success)
                        {
                            await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                            return;
                        }

                        // 处理头部信息（分析状态）
                        if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                        {
                            var header = response.Header;
                            if (header.State == AnalysisStateEnum.NotAnalyzed)
                            {
                                // 修复：使用构造函数传入三个参数
                                ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), $"File {SelectedLogFile.FileName} has not been analyzed yet."));
                                return;
                            }
                            else if (header.State == AnalysisStateEnum.Failed)
                            {
                                ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), $"Analysis failed: {header.ErrorMessage}"));
                                return;
                            }
                            else if (header.State == AnalysisStateEnum.Succeeded)
                            {
                                ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), $"File: {SelectedLogFile.FileName}; Worker ID: {header.WorkerId}"));
                                ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), ""));
                            }
                        }
                        // 处理具体的每一条日志条目
                        else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                        {
                            var entry = response.LogEntry;
                            string output = "";

                            switch (entry.EntryCase)
                            {
                                case LogEntryMessage.EntryOneofCase.CallLogEntry:
                                    var c = entry.CallLogEntry;
                                    output = $"{entryIndex} | LineNo: {c.LineNo}, Timestamp: {c.Timestamp.ToDateTimeOffset():O}, PodName: {c.PodName}, Severity: {c.Severity}, EventType: {c.EventType}, RequestId: {c.RequestId}, TargetService: {c.TargetService}, DurationMs: {c.DurationMs}";
                                    break;
                                case LogEntryMessage.EntryOneofCase.RequestLogEntry:
                                    var r = entry.RequestLogEntry;
                                    output = $"{entryIndex} | LineNo: {r.LineNo}, Timestamp: {r.Timestamp.ToDateTimeOffset():O}, PodName: {r.PodName}, Severity: {r.Severity}, EventType: {r.EventType}, RequestId: {r.RequestId}, Method: {r.Method}, Path: {r.Path}, StatusCode: {r.StatusCode}";
                                    break;
                                case LogEntryMessage.EntryOneofCase.InternalLogEntry:
                                    var i = entry.InternalLogEntry;
                                    output = $"{entryIndex} | LineNo: {i.LineNo}, Timestamp: {i.Timestamp.ToDateTimeOffset():O}, PodName: {i.PodName}, Severity: {i.Severity}, EventType: {i.EventType}, ExceptionName: {i.ExceptionName}, ExceptionMessage: {i.ExceptionMessage}";
                                    break;
                            }

                            // 修复：使用构造函数传入三个参数
                            ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), output));

                            entryIndex++;
                        }
                    }
                }
                catch (RpcException ex)
                {
                    await DialogHelper.ShowMessageDialogAsync("RPC Error", ex.Status.Detail);
                }
            });
        }

        [RelayCommand]
        private async Task AboutAsync()
        {
            await DialogHelper.ShowMessageDialogAsync("About",
                """
                LogAnalyzerClient
                EESAST Software Center
                https://github.com/eesast/dotnet-workshop
                """);
        }
    }
}
