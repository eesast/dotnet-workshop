using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
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

        // 表格化的分析结果（T5.1.b.a）
        [ObservableProperty]
        private ObservableCollection<LogRow> _resultRows = new();

        // ==================== Token 身份认证 ====================
        [ObservableProperty]
        private string _currentToken = "";

        // ==================== 查询过滤状态（T5.1.a.c） ====================
        // ComboBox 的 SelectedIndex：0 表示「全部 / 不排序」
        [ObservableProperty]
        private int _eventTypeIndex = 0;      // 0=All,1=Call,2=Request,3=Internal

        [ObservableProperty]
        private int _severityIndex = 0;       // 0=All,1=Info,2=Warning,3=Error

        [ObservableProperty]
        private int _sortByIndex = 0;         // 0=None,1=LineNo,2=Timestamp,3=Severity,4=PodName

        [ObservableProperty]
        private bool _isDescending = false;

        [ObservableProperty]
        private string _queryServiceName = "";

        [ObservableProperty]
        private string _queryRequestId = "";

        [ObservableProperty]
        private string _queryStartTime = "";

        [ObservableProperty]
        private string _queryEndTime = "";

        // ==================== 连接状态联动 ====================
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ChangeDirectoryCommand))]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSelectedFilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeAllCommand))]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeRightClickedFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(GetAnalysisResultCommand))]
        [NotifyCanExecuteChangedFor(nameof(QueryCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetQueryCommand))]
        private bool _isConnected = false;

        // 鉴权头：统一使用 x-agent-token（与 Agent 端 AuthInterceptor 一致）。
        private static Metadata GetHeaders()
        {
            var headers = new Metadata();
            if (!string.IsNullOrWhiteSpace(LogAgentClientManager.CurrentToken))
            {
                headers.Add("x-agent-token", LogAgentClientManager.CurrentToken);
            }
            return headers;
        }

        // ==================== 连接 ====================
        [RelayCommand]
        private async Task ConnectAsync()
        {
            var connectInfo = await DialogHelper.ShowConnectDialogAsync(CurrentAddress, CurrentToken);
            if (connectInfo is null) return;

            var address = connectInfo.Address.Trim();
            if (string.IsNullOrEmpty(address))
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Address cannot be empty.");
                return;
            }

            // 应用 Token（可以为空，但空 Token 会被 Agent 拒绝）
            LogAgentClientManager.CurrentToken = connectInfo.Token.Trim();
            CurrentToken = LogAgentClientManager.CurrentToken;

            try
            {
                ConnectStatus = ConnectStatusString.CONNECTING;
                _client = AppService.ClientFactory.CreateClient(address);
                await _client.PingAsync(new Empty(), headers: GetHeaders());

                CurrentAddress = address;
                ConnectStatus = ConnectStatusString.CONNECTED;
                LogFiles.Clear();
                IsConnected = true;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
            {
                ConnectStatus = ConnectStatusString.CONNECT_FAILED;
                await DialogHelper.ShowMessageDialogAsync("Authentication Error",
                    "Token 无效或未提供！请在连接对话框中填入 Agent 控制台启动时打印的 Token。");
                ConnectStatus = ConnectStatusString.NOT_CONNECTED;
                IsConnected = false;
            }
            catch (Exception ex)
            {
                ConnectStatus = ConnectStatusString.CONNECT_FAILED;
                await DialogHelper.ShowMessageDialogAsync("Error", $"Failed to connect to agent: {ex.Message}");
                ConnectStatus = ConnectStatusString.NOT_CONNECTED;
                IsConnected = false;
            }
        }

        // ==================== Token 应用 ====================
        [RelayCommand]
        private async Task ApplyTokenAsync()
        {
            var token = CurrentToken?.Trim() ?? string.Empty;
            LogAgentClientManager.CurrentToken = token;

            if (_client is null || !IsConnected)
            {
                await DialogHelper.ShowMessageDialogAsync("Info", "Token 已保存。请先连接 Agent 后再验证。");
                return;
            }

            try
            {
                await _client.PingAsync(new Empty(), headers: GetHeaders());
                await DialogHelper.ShowMessageDialogAsync("Success", "Token 已应用并通过验证。");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
            {
                await DialogHelper.ShowMessageDialogAsync("Authentication Error", "Token 无效！");
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", $"验证 Token 失败: {ex.Message}");
            }
        }

        private async Task WithClientNotNull(Func<Task> action)
        {
            if (_client is null || !IsConnected)
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
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
                {
                    await DialogHelper.ShowMessageDialogAsync("Authentication Error",
                        "Token 无效或未提供！请先应用有效的 Token。");
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
                {
                    await DialogHelper.ShowMessageDialogAsync("Permission Denied",
                        "当前 Token 权限不足，无法执行该操作！");
                }
                catch (Exception ex)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"Error occurred: {ex.Message}");
                }
            }
        }

        // ==================== 目录与文件 ====================
        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task ChangeDirectoryAsync()
        {
            await WithClientNotNull(async () =>
            {
                var request = new ChangeDirectoryRequest() { DirectoryPath = DirectoryPath };
                var response = await _client!.ChangeDirectoryAsync(request, headers: GetHeaders());
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                DirectoryPath = response.CurrentDirectory;
                await RefreshAsync();
            });
        }

        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task RefreshAsync()
        {
            await WithClientNotNull(async () =>
            {
                var response = await _client!.GetLogFilesAsync(new Empty(), headers: GetHeaders());
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                LogFiles.Clear();
                foreach (var fileName in response.FileNames) LogFiles.Add(new LogFileItem(fileName));
            });
        }

        // ==================== 分析 ====================
        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task AnalyzeSelectedFilesAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedFiles == null || SelectedFiles.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "No log files selected.");
                    return;
                }
                if (!TryParseParallelism(out int parallelism)) return;
                var request = new AnalyzeFilesRequest { DegreeOfParallelism = parallelism };
                request.FileNames.AddRange(SelectedFiles);
                var response = await _client!.AnalyzeFilesAsync(request, headers: GetHeaders());
                if (!response.Status.Success)
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
            });
        }

        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!TryParseParallelism(out int parallelism)) return;
                var request = new AnalyzeAllRequest { DegreeOfParallelism = parallelism };
                var response = await _client!.AnalyzeAllAsync(request, headers: GetHeaders());
                if (!response.Status.Success)
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
            });
        }

        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null || string.IsNullOrEmpty(SelectedLogFile.FileName))
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "No log file selected.");
                    return;
                }
                if (!TryParseParallelism(out int parallelism)) return;
                var request = new AnalyzeFilesRequest { DegreeOfParallelism = parallelism };
                request.FileNames.Add(SelectedLogFile.FileName);
                var response = await _client!.AnalyzeFilesAsync(request, headers: GetHeaders());
                if (!response.Status.Success)
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
            });
        }

        private bool TryParseParallelism(out int parallelism)
        {
            if (!int.TryParse(DegreeOfParallelismText, out parallelism) || parallelism < 0)
            {
                DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                return false;
            }
            return true;
        }

        // ==================== 结果查看与查询 ====================
        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!EnsureFileSelected()) return;
                var request = new GetAnalysisResultRequest { FileName = SelectedLogFile!.FileName };
                ResultRows.Clear();

                using var call = _client!.GetAnalysisResult(request, headers: GetHeaders());
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            HandleHeader(response.Header);
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            AddEntry(response.LogEntry);
                            break;
                    }
                }
            });
        }

        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task QueryAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!EnsureFileSelected()) return;
                if (!TryBuildFilter(out var filter)) return;

                var request = new QueryAnalysisResultRequest
                {
                    FileName = SelectedLogFile!.FileName,
                    Filter = filter,
                    Sort = BuildSort()
                };

                ResultRows.Clear();
                using var call = _client!.QueryAnalysisResult(request, headers: GetHeaders());
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            HandleHeader(response.Header);
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            AddEntry(response.LogEntry);
                            break;
                    }
                }
            });
        }

        [RelayCommand(CanExecute = nameof(IsConnected))]
        private async Task ResetQueryAsync()
        {
            EventTypeIndex = 0;
            SeverityIndex = 0;
            SortByIndex = 0;
            IsDescending = false;
            QueryServiceName = "";
            QueryRequestId = "";
            QueryStartTime = "";
            QueryEndTime = "";
            await GetAnalysisResultAsync();
        }

        private bool EnsureFileSelected()
        {
            if (SelectedLogFile is null || string.IsNullOrEmpty(SelectedLogFile.FileName))
            {
                DialogHelper.ShowMessageDialogAsync("Warning", "Please select a log file first.");
                return false;
            }
            return true;
        }

        private async void HandleHeader(AnalysisResultHeaderMessage header)
        {
            switch (header.State)
            {
                case AnalysisStateEnum.NotAnalyzed:
                    await DialogHelper.ShowMessageDialogAsync("Info", $"File '{header.FileName}' has not been analyzed yet.");
                    break;
                case AnalysisStateEnum.Failed:
                    await DialogHelper.ShowMessageDialogAsync("Error", $"Analysis failed for '{header.FileName}': {header.ErrorMessage}");
                    break;
            }
        }

        private void AddEntry(LogEntryMessage entryMessage)
        {
            var entry = GrpcTypeConverter.ConvertFromGrpc(entryMessage);
            if (entry is not null)
            {
                ResultRows.Add(LogRow.FromEntry(entry));
            }
        }

        private bool TryBuildFilter(out LogFilter filter)
        {
            filter = new LogFilter();

            if (EventTypeIndex > 0) filter.EventType = (LogEventTypeEnum)(EventTypeIndex - 1);
            if (SeverityIndex > 0) filter.Severity = (LogSeverityEnum)(SeverityIndex - 1);
            if (!string.IsNullOrWhiteSpace(QueryServiceName)) filter.ServiceName = QueryServiceName.Trim();
            if (!string.IsNullOrWhiteSpace(QueryRequestId)) filter.RequestId = QueryRequestId.Trim();

            if (!string.IsNullOrWhiteSpace(QueryStartTime))
            {
                if (!DateTimeOffset.TryParse(QueryStartTime.Trim(), out var start))
                {
                    DialogHelper.ShowMessageDialogAsync("Error", "Start time 不是合法的日期时间（例如 2026-06-05T16:00:00Z）。");
                    return false;
                }
                filter.StartTime = Timestamp.FromDateTimeOffset(start);
            }

            if (!string.IsNullOrWhiteSpace(QueryEndTime))
            {
                if (!DateTimeOffset.TryParse(QueryEndTime.Trim(), out var end))
                {
                    DialogHelper.ShowMessageDialogAsync("Error", "End time 不是合法的日期时间（例如 2026-06-05T16:00:00Z）。");
                    return false;
                }
                filter.EndTime = Timestamp.FromDateTimeOffset(end);
            }

            return true;
        }

        private LogSortOptions BuildSort()
        {
            var sort = new LogSortOptions { IsDescending = IsDescending };
            sort.SortBy = SortByIndex switch
            {
                1 => "LineNo",
                2 => "Timestamp",
                3 => "Severity",
                4 => "PodName",
                _ => string.Empty
            };
            return sort;
        }

        // ==================== About ====================
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
