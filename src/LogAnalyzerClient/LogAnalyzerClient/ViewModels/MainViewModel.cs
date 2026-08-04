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

        // 用于把 gRPC 返回的日志条目转成键值对，便于在 Analysis Result 中展示。
        private readonly KeyValueVisitor _visitor = new();

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

        /// <summary>
        /// 解析并行度输入框中的文本。合法（非负整数）时返回对应值，否则返回 null。
        /// </summary>
        private int? TryGetDegreeOfParallelism()
        {
            if (int.TryParse(DegreeOfParallelismText?.Trim(), out int degree) && degree >= 0)
            {
                return degree;
            }
            return null;
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            await WithClientNotNull(async () =>
            {
                var response = await _client!.GetLogFilesAsync(new Empty());
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
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
                if (SelectedFiles.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file selected. Please select at least one log file (hold Ctrl to multi-select).");
                    return;
                }
                var degree = TryGetDegreeOfParallelism();
                if (degree is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Invalid degree of parallelism. Please input a non-negative integer.");
                    return;
                }
                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degree.Value,
                };
                request.FileNames.AddRange(SelectedFiles);
                var response = await _client!.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                await DialogHelper.ShowMessageDialogAsync("Analyze",
                    $"Successfully analyzed {SelectedFiles.Count} file(s).");
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                var degree = TryGetDegreeOfParallelism();
                if (degree is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Invalid degree of parallelism. Please input a non-negative integer.");
                    return;
                }
                var request = new AnalyzeAllRequest()
                {
                    DegreeOfParallelism = degree.Value,
                };
                var response = await _client!.AnalyzeAllAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                await DialogHelper.ShowMessageDialogAsync("Analyze", "Successfully analyzed all log files.");
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file selected. Please select a log file first.");
                    return;
                }
                var degree = TryGetDegreeOfParallelism();
                if (degree is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Invalid degree of parallelism. Please input a non-negative integer.");
                    return;
                }
                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degree.Value,
                };
                request.FileNames.Add(SelectedLogFile.FileName);
                var response = await _client!.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                await DialogHelper.ShowMessageDialogAsync("Analyze",
                    $"Successfully analyzed '{SelectedLogFile.FileName}'.");
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file selected. Please select a log file first.");
                    return;
                }
                var fileName = SelectedLogFile.FileName;
                var request = new GetAnalysisResultRequest()
                {
                    FileName = fileName,
                };

                // 每次查看结果前先清空，避免与上一次的结果混淆。
                ResultEntries.Clear();
                using var call = _client!.GetAnalysisResult(request);
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error",
                            $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            switch (response.Header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(),
                                        $"File '{fileName}' has not been analyzed yet."));
                                    return;
                                case AnalysisStateEnum.Failed:
                                    ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(),
                                        $"Analysis failed: {response.Header.ErrorMessage}"));
                                    return;
                                case AnalysisStateEnum.Succeeded:
                                    // 头部状态为成功，继续接收后续逐条日志条目。
                                    break;
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var dump = _visitor.Dump(entry);
                            var fields = dump
                                .Select(kv => new LogFieldItem(kv.Key, kv.Value))
                                .ToList();
                            ResultEntries.Add(new LogFields(entry.LineNo, fields, null));
                            break;
                    }
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
