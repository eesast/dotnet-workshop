using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
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

        // 与 _client 配套的句柄，持有底层 channel；重连/断开时 Dispose 以避免泄漏。
        private AgentClientHandle? _clientHandle = null;

        // 当前已从 Agent 拉取并解析出的日志条目（原始顺序）。排序时以此为基础重排显示。
        private readonly List<LogEntry> _loadedEntries = new();

        // 各排序键对应的「键值提取器」。不同事件类型的日志字段不同，缺失字段以该类型的默认值参与排序。
        private static readonly Dictionary<string, Func<LogEntry, IComparable>> _sortKeySelectors = new()
        {
            ["LineNo"] = e => e.LineNo,
            ["Timestamp"] = e => e.Timestamp,
            ["Severity"] = e => e.Severity,
            ["EventType"] = e => e.EventType,
            ["PodName"] = e => e.PodName,
            ["RequestId"] = e => e switch
            {
                CallLogEntry c => c.RequestId,
                RequestLogEntry r => r.RequestId,
                _ => "",
            },
            ["TargetService"] = e => e is CallLogEntry c ? c.TargetService : "",
            ["Method"] = e => e is RequestLogEntry r ? r.Method : "",
            ["Path"] = e => e is RequestLogEntry r ? r.Path : "",
            ["StatusCode"] = e => e is RequestLogEntry r ? r.StatusCode : int.MinValue,
            ["DurationMs"] = e => e is CallLogEntry c ? c.DurationMs : int.MinValue,
            ["ExceptionName"] = e => e is InternalLogEntry i ? i.ExceptionName : "",
        };

        /// <summary>
        /// 可供选择的排序键列表（与分析结果面板中的下拉框绑定）。
        /// </summary>
        public ObservableCollection<string> SortKeys { get; } = new()
        {
            "LineNo", "Timestamp", "Severity", "EventType", "PodName",
            "RequestId", "TargetService", "Method", "Path", "StatusCode",
            "DurationMs", "ExceptionName",
        };

        [ObservableProperty]
        private string _selectedSortKey = "LineNo";

        [ObservableProperty]
        private bool _isSortDescending = false;

        public IReadOnlyList<string> SelectedFiles { get; set; } = new List<string>();

        [ObservableProperty]
        private string _greeting = "Welcome to Avalonia!";

        [ObservableProperty]
        private string _directoryPath = "";

        [ObservableProperty]
        private string _degreeOfParallelismText = "1";

        [ObservableProperty]
        private string _currentAddress = "";

        /// <summary>
        /// 当前用于鉴权的 token（T5.1.a.b）。连接成功后保存，便于刷新连接时预填、
        /// 以及在管理员窗口中高亮「自己」。
        /// </summary>
        [ObservableProperty]
        private string _authToken = "";

        /// <summary>
        /// 当前 token 是否拥有管理员权限；决定「Manage Tokens」菜单是否可见。
        /// 连接成功后由 <see cref="DetectAdminAsync"/> 探测得到。
        /// </summary>
        [ObservableProperty]
        private bool _isAdmin = false;

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
        private ObservableCollection<LogEntryRowVm> _resultEntries = new();

        /// <summary>
        /// 结果表格中当前选中的行（T5.2 链路追踪右键入口读取其 RequestId）。
        /// </summary>
        [ObservableProperty]
        private LogEntryRowVm? _selectedResultEntry = null;

        /// <summary>
        /// 分析结果面板的状态提示（如「Showing all N entries.」或「Filtered: N entries.」）。
        /// </summary>
        [ObservableProperty]
        private string _resultStatusText = "";

        /// <summary>
        /// 当前结果是否为按条件过滤后的子集；为 true 时显示「Show All」按钮以便回到完整结果。
        /// </summary>
        [ObservableProperty]
        private bool _isResultFiltered = false;

        [RelayCommand]
        private async Task ConnectAsync()
        {
            var input = await DialogHelper.ShowConnectDialogAsync();
            if (input is null)
            {
                // 用户取消了对话框。
                return;
            }

            string address = input.Address.Trim();
            string token = input.Token.Trim();
            if (address.Length == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Address cannot be empty.");
                return;
            }
            if (token.Length == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error",
                    "Token cannot be empty. The Agent requires authentication (T5.1.a.b).");
                return;
            }

            // 重连场景：先记住旧连接，用新 token 验证成功后再原子替换；失败则保留旧连接。
            bool wasConnected = _client is not null;
            AgentClientHandle? previousHandle = _clientHandle;
            LogAnalyzerAgentServiceClient? previousClient = _client;
            string previousAddress = CurrentAddress;
            string previousToken = AuthToken;

            ConnectStatus = ConnectStatusString.CONNECTING;
            AgentClientHandle? newHandle = null;
            try
            {
                // 用本地变量持有新 client，验证通过前不触碰 _client。
                newHandle = AppService.ClientFactory.CreateClient(address, token);
                // Ping 同样需要 token；token 非法时 Agent 返回 Unauthenticated。
                await newHandle.Client.PingAsync(new Empty());

                // 验证成功：Dispose 旧 channel，原子地切换到新连接。
                previousHandle?.Dispose();
                _clientHandle = newHandle;
                _client = newHandle.Client;
                CurrentAddress = address;
                AuthToken = token;
                ConnectStatus = ConnectStatusString.CONNECTED;
                LogFiles.Clear();
                // 连接成功后探测当前 token 是否为管理员，控制管理员菜单的可见性。
                await DetectAdminAsync();
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
            {
                await HandleConnectFailureAsync(newHandle, wasConnected, previousHandle, previousClient,
                    previousAddress, previousToken,
                    wasConnected
                        ? "Authentication failed: the new token was rejected. The previous connection has been kept."
                        : "Authentication failed: the token was rejected by the Agent.");
            }
            catch (Exception ex)
            {
                await HandleConnectFailureAsync(newHandle, wasConnected, previousHandle, previousClient,
                    previousAddress, previousToken,
                    wasConnected
                        ? $"Failed to connect with the new settings: {ex.Message}. The previous connection has been kept."
                        : $"Failed to connect to agent: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理连接/重连失败：销毁新建但未通过验证的 client；若之前已连接则恢复旧连接，否则置为未连接。
        /// </summary>
        private async Task HandleConnectFailureAsync(
            AgentClientHandle? newHandle, bool wasConnected,
            AgentClientHandle? previousHandle, LogAnalyzerAgentServiceClient? previousClient,
            string previousAddress, string previousToken, string message)
        {
            // 新 client 没有进入 _client，单独 Dispose 即可。
            newHandle?.Dispose();
            if (wasConnected)
            {
                // 保留旧连接，恢复状态。
                _clientHandle = previousHandle;
                _client = previousClient;
                CurrentAddress = previousAddress;
                AuthToken = previousToken;
                ConnectStatus = ConnectStatusString.CONNECTED;
            }
            else
            {
                ConnectStatus = ConnectStatusString.CONNECT_FAILED;
                _clientHandle = null;
                _client = null;
                IsAdmin = false;
            }
            await DialogHelper.ShowMessageDialogAsync("Error", message);
            if (!wasConnected)
            {
                ConnectStatus = ConnectStatusString.NOT_CONNECTED;
            }
        }

        /// <summary>
        /// 通过尝试列出 token 来探测当前 token 是否为管理员。
        /// 普通权限会得到 PermissionDenied；不抛错地吞掉，仅用于切换 <see cref="IsAdmin"/>。
        /// </summary>
        private async Task DetectAdminAsync()
        {
            if (_client is null)
            {
                IsAdmin = false;
                return;
            }
            try
            {
                var resp = await _client.ListTokensAsync(new Empty());
                IsAdmin = resp.Status.Success;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                IsAdmin = false;
            }
            catch
            {
                // 探测失败不应阻断连接，保守地视为非管理员。
                IsAdmin = false;
            }
        }

        /// <summary>
        /// 打开 Token 管理窗口（仅管理员可用，菜单可见性已由 <see cref="IsAdmin"/> 绑定）。
        /// </summary>
        [RelayCommand]
        private async Task ManageTokensAsync()
        {
            await WithClientNotNull(async () =>
            {
                await DialogHelper.ShowTokenManagerDialogAsync(_client!, AuthToken);
            });
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

        // 当用户切换排序键或升降序时自动重新排序。
        partial void OnSelectedSortKeyChanged(string value) => ApplySort();
        partial void OnIsSortDescendingChanged(bool value) => ApplySort();

        /// <summary>
        /// 把一条日志条目转换为结果表格中的一行 <see cref="LogEntryRowVm"/>（T5.1.b.a 强类型列模型）。
        /// </summary>
        private static LogEntryRowVm BuildRow(LogEntry entry) => new(entry);

        /// <summary>
        /// 当前是否处于非默认排序（默认为按 LineNo 升序，即日志原始顺序）。
        /// </summary>
        private bool IsCustomSortActive =>
            IsSortDescending || SelectedSortKey != "LineNo";

        /// <summary>
        /// 按当前选择的排序键与升降序，对已加载的日志条目重新排序并刷新显示。
        /// 若当前没有已加载的条目（例如只显示了错误信息），则不动显示内容。
        /// </summary>
        private void ApplySort()
        {
            if (_loadedEntries.Count == 0)
            {
                return;
            }
            var key = SelectedSortKey ?? "LineNo";
            if (!_sortKeySelectors.TryGetValue(key, out var selector))
            {
                selector = _sortKeySelectors["LineNo"];
            }

            var sorted = _loadedEntries.ToList();
            sorted.Sort((a, b) =>
            {
                int cmp = selector(a).CompareTo(selector(b));
                return IsSortDescending ? -cmp : cmp;
            });

            ResultEntries.Clear();
            foreach (var entry in sorted)
            {
                ResultEntries.Add(BuildRow(entry));
            }
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

                // 拉取全部结果（不经过滤），并刷新状态提示。
                using var call = _client!.GetAnalysisResult(request);
                bool loaded = await ConsumeResultStreamAsync(call, fileName);
                IsResultFiltered = false;
                // loaded 为 false（未分析 / 失败）时，保留 ConsumeResultStreamAsync 已设置的状态提示。
                if (loaded)
                {
                    ResultStatusText = BuildResultStatusText(filtered: false);
                }
            });
        }

        /// <summary>
        /// 打开查询对话框，按用户给出的条件向 Agent 查询当前选中文件的日志子集。
        /// 查询结果会替换显示列表并缓存到 _loadedEntries，因此排序功能同样适用于查询结果。
        /// </summary>
        [RelayCommand]
        private async Task QueryAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file selected. Please select a log file first.");
                    return;
                }

                var filter = await DialogHelper.ShowQueryDialogAsync();
                if (filter is null)
                {
                    // 用户取消了对话框
                    return;
                }

                var fileName = SelectedLogFile.FileName;
                var request = filter.ToRequest(fileName);
                using var call = _client!.QueryAnalysisResult(request);
                bool loaded = await ConsumeResultStreamAsync(call, fileName);
                // 即便条件全空（等价于查询全部），也按「过滤」语义标记，便于用户用 Show All 回到默认视图。
                IsResultFiltered = loaded;
                if (loaded)
                {
                    ResultStatusText = BuildResultStatusText(filtered: true);
                }
            });
        }

        /// <summary>
        /// 从 Agent 拉取当前选中文件的云服务调用拓扑，并在拓扑窗口中可视化。
        /// 用户在窗口中点击某条有向边后，再从 Agent 拉取该边对应的所有 Call 日志并展示到结果面板。
        /// </summary>
        [RelayCommand]
        private async Task ShowTopologyAsync()
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

                var topologyResponse = await _client!.GetCallTopologyAsync(
                    new GetCallTopologyRequest { FileName = fileName });
                if (!topologyResponse.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{topologyResponse.Status.Code}: {topologyResponse.Status.Message}");
                    return;
                }

                var graph = new TopologyGraph { FileName = fileName };
                foreach (var node in topologyResponse.Nodes)
                {
                    graph.Nodes.Add(node.Service);
                }
                foreach (var edge in topologyResponse.Edges)
                {
                    graph.Edges.Add(new TopologyEdge(
                        edge.SourceService, edge.TargetService, edge.CallCount));
                }

                if (graph.Nodes.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Topology",
                        $"No Call logs found in '{fileName}', so no topology could be inferred.");
                    return;
                }

                var selected = await DialogHelper.ShowTopologyDialogAsync(graph);
                if (selected is null)
                {
                    // 用户关闭了拓扑窗口而未选择边
                    return;
                }

                // 拉取该边对应的 Call 日志并展示（复用结果流消费逻辑，故排序同样适用）。
                var edgeRequest = new GetEdgeCallLogsRequest
                {
                    FileName = fileName,
                    SourceService = selected.SourceService,
                    TargetService = selected.TargetService,
                };
                using var call = _client!.GetEdgeCallLogs(edgeRequest);
                bool loaded = await ConsumeResultStreamAsync(call, fileName);
                IsResultFiltered = loaded;
                if (loaded)
                {
                    int n = _loadedEntries.Count;
                    ResultStatusText =
                        $"Edge  {selected.SourceService} -> {selected.TargetService}  ({n} call{(n == 1 ? "" : "s")}).";
                }
                // loaded 为 false 时保留 ConsumeResultStreamAsync 设置的状态提示（未分析 / 失败）。
            });
        }

        /// <summary>
        /// 追踪当前选中结果行所属请求的完整调用链（T5.2）：按 Request ID 从 Agent 拉取该请求的
        /// 全部 Call/Request 日志（已按时间升序），把其中的 Call 日志组装成瀑布图 spans，并在瀑布图窗口可视化。
        /// </summary>
        [RelayCommand]
        private async Task ShowTraceAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file selected. Please select a log file first.");
                    return;
                }
                if (SelectedResultEntry is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Trace",
                        "No row selected. Please left-click a row first, then right-click to trace its request.");
                    return;
                }
                string requestId = SelectedResultEntry.RequestId;
                if (string.IsNullOrEmpty(requestId))
                {
                    await DialogHelper.ShowMessageDialogAsync("Trace",
                        "This log entry has no Request ID (Internal events don't carry one), so its call chain cannot be traced.");
                    return;
                }

                var fileName = SelectedLogFile.FileName;
                using var call = _client!.GetTrace(new GetTraceRequest
                {
                    FileName = fileName,
                    RequestId = requestId,
                });

                var waterfall = new TraceWaterfall
                {
                    FileName = fileName,
                    RequestId = requestId,
                };
                bool ok = await ConsumeTraceStreamAsync(call, waterfall);
                if (!ok)
                {
                    return;
                }
                if (waterfall.Spans.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Trace",
                        $"No Call logs found for request '{requestId}'.");
                    return;
                }
                await DialogHelper.ShowTraceDialogAsync(waterfall);
            });
        }

        /// <summary>
        /// 读取一个 GetTrace 流（header + 逐条日志），把其中的 Call 日志转成瀑布图 span，收集到 <paramref name="waterfall"/>。
        /// 与 <see cref="ConsumeResultStreamAsync"/> 不同：它不清空主结果表格，只组装 spans。
        /// 返回 false 表示流首部状态非成功（未分析 / 失败 / 出错），已弹框提示。
        /// </summary>
        private async Task<bool> ConsumeTraceStreamAsync(
            AsyncServerStreamingCall<GetAnalysisResultResponse> call,
            TraceWaterfall waterfall)
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return false;
                }

                switch (response.PayloadCase)
                {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        if (response.Header.State == AnalysisStateEnum.NotAnalyzed)
                        {
                            await DialogHelper.ShowMessageDialogAsync("Trace",
                                $"File '{waterfall.FileName}' has not been analyzed yet.");
                            return false;
                        }
                        if (response.Header.State == AnalysisStateEnum.Failed)
                        {
                            await DialogHelper.ShowMessageDialogAsync("Trace",
                                $"Analysis failed: {response.Header.ErrorMessage}");
                            return false;
                        }
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        // 瀑布图的 span 只来自 Call 日志（Request 日志没有 duration / target-service）。
                        if (entry is CallLogEntry c)
                        {
                            waterfall.Spans.Add(new TraceSpan(
                                LogEntryRowVm.ExtractService(c.PodName),
                                LogEntryRowVm.ExtractService(c.TargetService),
                                c.Timestamp,
                                c.DurationMs,
                                c.Severity == LogSeverity.Error));
                        }
                        break;
                }
            }
            return true;
        }

        /// <summary>
        /// 将当前选中文件的分析结果导出为 Parquet 文件（T5.1.a.a）。
        /// 导出路径与是否覆盖由导出对话框决定；导出的 .parquet 可被刷新后重新分析，演示 Parquet 读取闭环。
        /// </summary>
        [RelayCommand]
        private async Task ExportAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file selected. Please select a log file first.");
                    return;
                }
                string fileName = SelectedLogFile.FileName;

                var options = await DialogHelper.ShowExportDialogAsync(fileName);
                if (options is null)
                {
                    // 用户取消了对话框
                    return;
                }

                var request = new ExportAnalysisResultRequest
                {
                    FileName = fileName,
                    OutputPath = options.OutputPath,
                    Overwrite = options.Overwrite,
                };
                var response = await _client!.ExportAnalysisResultAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                await DialogHelper.ShowMessageDialogAsync("Export",
                    $"Exported {response.EntryCount} entries to:\n{response.WrittenPath}");
            });
        }

        /// <summary>
        /// 读取一个分析结果流（header + 逐条日志），把日志条目缓存到 _loadedEntries 并展示到 ResultEntries。
        /// 流的首条消息为 header，据此判断文件是否已分析 / 是否失败。
        /// 返回 true 表示成功加载了（可能为零条的）日志条目；false 表示展示了非条目提示（未分析 / 失败 / 出错）。
        /// </summary>
        private async Task<bool> ConsumeResultStreamAsync(
            AsyncServerStreamingCall<GetAnalysisResultResponse> call,
            string fileName)
        {
            // 每次查看结果前先清空，避免与上一次的结果混淆。
            _loadedEntries.Clear();
            ResultEntries.Clear();
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return false;
                }

                switch (response.PayloadCase)
                {
                    case GetAnalysisResultResponse.PayloadOneofCase.Header:
                        switch (response.Header.State)
                        {
                            case AnalysisStateEnum.NotAnalyzed:
                                // 不再把状态提示作为伪行塞进表格，改为在表格上方的状态栏展示。
                                ResultStatusText = $"File '{fileName}' has not been analyzed yet.";
                                return false;
                            case AnalysisStateEnum.Failed:
                                ResultStatusText = $"Analysis failed: {response.Header.ErrorMessage}";
                                return false;
                            case AnalysisStateEnum.Succeeded:
                                // 头部状态为成功，继续接收后续逐条日志条目。
                                break;
                        }
                        break;
                    case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        // 一边接收一边按原始顺序显示，同时缓存到 _loadedEntries 以便排序。
                        _loadedEntries.Add(entry);
                        ResultEntries.Add(BuildRow(entry));
                        break;
                }
            }

            // 接收完毕后，若用户已选择非默认排序，则按当前排序重排显示。
            if (IsCustomSortActive)
            {
                ApplySort();
            }
            return true;
        }

        /// <summary>
        /// 根据当前已加载条目数与是否过滤，构造分析结果面板的状态提示文本。
        /// </summary>
        private string BuildResultStatusText(bool filtered)
        {
            int n = _loadedEntries.Count;
            string unit = n == 1 ? "entry" : "entries";
            if (n == 0)
            {
                return filtered
                    ? "No entries match the query."
                    : "No entries.";
            }
            return filtered
                ? $"Filtered: showing {n} {unit}."
                : $"Showing all {n} {unit}.";
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
