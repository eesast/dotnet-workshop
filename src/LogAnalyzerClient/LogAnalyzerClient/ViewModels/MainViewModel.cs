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
                // 1. 调用 gRPC 获取文件列表
                var response = await _client!.GetLogFilesAsync(new Empty());
        
                // 2. 检查是否成功
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                    $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                // 3. 清空并重新填充文件列表
                LogFiles.Clear();
                foreach (var fileName in response.FileNames)
                {
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
                if (SelectedFiles.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "请至少选择一个文件。");
                    return;
                }

                // 2. 解析并行度（从文本框读取）
                if (!int.TryParse(DegreeOfParallelismText, out int dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "并行度必须是一个非负整数。");
                    return;
                }

                // 3. 构建请求
                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = dop
                };
                request.FileNames.AddRange(SelectedFiles);

                // 4. 调用 gRPC
                var response = await _client!.AnalyzeFilesAsync(request);

                // 5. 检查结果
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Success", "分析任务已执行完毕。");
                }
            });
        }
        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 1. 解析并行度
                if (!int.TryParse(DegreeOfParallelismText, out int dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "并行度必须是一个非负整数。");
                    return;
                }

                // 2. 调用 gRPC
                var request = new AnalyzeAllRequest
                {
                    DegreeOfParallelism = dop
                };
                var response = await _client!.AnalyzeAllAsync(request);

                // 3. 检查结果
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Success", "全部分析任务已执行完毕。");
                }
            });
        }

        /*
         * TODO: T4.1
         * Add AnalyzeAllAsync ReplayCommand
         */

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
                await WithClientNotNull(async () =>
            {
                // 1. 检查是否有选中的文件（右键选中）
                if (SelectedLogFile == null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "请先选择一个文件。");
                    return;
                }

                // 2. 解析并行度
                if (!int.TryParse(DegreeOfParallelismText, out int dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "并行度必须是一个非负整数。");
                    return;
                }

                // 3. 构建请求（只传一个文件）
                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = dop
                };
                request.FileNames.Add(SelectedLogFile.FileName);

                // 4. 调用 gRPC
                var response = await _client!.AnalyzeFilesAsync(request);

                // 5. 检查结果
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Success", $"文件 {SelectedLogFile.FileName} 分析完成。");
                }
            });
        }

       [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                // 1. 检查是否有选中的文件
                if (SelectedLogFile == null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "请先选择一个文件。");
                    return;
                }

                // 2. 清空之前的结果
                ResultEntries.Clear();

                // 3. 调用 gRPC 流式方法
                var request = new GetAnalysisResultRequest
                {
                    FileName = SelectedLogFile.FileName
                };

                using var call = _client!.GetAnalysisResult(request);
                var responseStream = call.ResponseStream;

                bool hasHeader = false;
                int entryIndex = 0;

                await foreach (var response in responseStream.ReadAllAsync())
                {
                    // 先检查状态
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error",
                            $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }

                    // 判断是 Header 还是 LogEntry
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                    {
                        var header = response.Header;
                        hasHeader = true;

                        // 根据分析状态显示不同的摘要
                        string summary;
                        if (header.State == AnalysisStateEnum.NotAnalyzed)
                        {
                            summary = $"📋 {SelectedLogFile.FileName} - 尚未分析";
                        }
                        else if (header.State == AnalysisStateEnum.Failed)
                        {
                            summary = $"❌ {SelectedLogFile.FileName} - 分析失败";
                        }
                        else // Succeeded
                        {
                            summary = $"✅ {SelectedLogFile.FileName} - 分析成功 (等待接收日志条目...)";
                        }

                        ResultEntries.Add(new LogFields(
                            Index: -1,
                            Fields: new List<LogFieldItem>(),
                            ErrorMessage: header.State == AnalysisStateEnum.Failed ? "分析失败" : null
                        ));
                    }
                    else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        // 将 Protobuf 消息转回 C# 对象
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);

                        // 用 KeyValueVisitor 转成键值对
                        var visitor = new KeyValueVisitor();
                        var dict = visitor.Dump(entry);

                        // 转成 LogFieldItem 列表
                        var fields = dict.Select(kv => new LogFieldItem(kv.Key, kv.Value)).ToList();

                        ResultEntries.Add(new LogFields(
                            Index: entryIndex++,
                            Fields: fields,
                            ErrorMessage: null
                        ));
                    }
                }

                if (!hasHeader)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "未收到有效响应。");
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
