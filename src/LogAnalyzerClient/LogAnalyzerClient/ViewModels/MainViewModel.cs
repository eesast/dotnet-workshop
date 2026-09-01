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

        // ===== T5.1: 表格显示 + 查询排序 =====
        [ObservableProperty]
        private ObservableCollection<DisplayRow> _displayRows = new();

        [ObservableProperty]
        private string _filterText = "";

        [ObservableProperty]
        private bool _sortDescending = false;

        [ObservableProperty]
        private string _sortKey = "LineNo";

        public IReadOnlyList<string> SortKeys { get; } = new[] { "LineNo", "Timestamp", "Severity", "EventType", "PodName" };

        /// <summary>当前文件解析出的全部条目（查询排序的本地数据源）。</summary>
        private readonly List<DisplayRow> _allRows = new();

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSortKeyChanged(string value) => ApplyFilter();

        partial void OnSortDescendingChanged(bool value) => ApplyFilter();

        private void ApplyFilter()
        {
            var query = (FilterText ?? "").Trim();
            IEnumerable<DisplayRow> rows = _allRows;

            if (query.Length > 0)
            {
                // 支持 k=v 精确匹配（如 severity=error、event=call）与其他子串过滤
                var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    var predicate = kv.Length == 2
                        ? (Func<DisplayRow, bool>)(r => RowValue(r, kv[0]).Contains(kv[1], StringComparison.OrdinalIgnoreCase))
                        : r => RowText(r).Contains(part, StringComparison.OrdinalIgnoreCase);
                    rows = rows.Where(predicate);
                }
            }

            var key = SortKey ?? "LineNo";
            var desc = SortDescending;
            rows = key switch
            {
                "Timestamp" => desc ? rows.OrderByDescending(r => r.Timestamp) : rows.OrderBy(r => r.Timestamp),
                "Severity" => desc ? rows.OrderByDescending(r => SeverityRank(r.Severity)) : rows.OrderBy(r => SeverityRank(r.Severity)),
                "EventType" => desc ? rows.OrderByDescending(r => r.EventType) : rows.OrderBy(r => r.EventType),
                "PodName" => desc ? rows.OrderByDescending(r => r.PodName) : rows.OrderBy(r => r.PodName),
                _ => desc ? rows.OrderByDescending(r => r.LineNo) : rows.OrderBy(r => r.LineNo),
            };

            DisplayRows.Clear();
            foreach (var row in rows)
            {
                DisplayRows.Add(row);
            }
        }

        private static string RowValue(DisplayRow r, string key)
        {
            return key.ToLowerInvariant() switch
            {
                "lineno" or "line" or "line_no" => r.LineNo.ToString(),
                "timestamp" or "time" => r.Timestamp,
                "podname" or "pod" or "service" => r.PodName,
                "severity" or "level" => r.Severity,
                "eventtype" or "event" => r.EventType,
                _ => r.Detail,
            };
        }

        private static string RowText(DisplayRow r) =>
            $"{r.LineNo} {r.Timestamp} {r.PodName} {r.Severity} {r.EventType} {r.Detail}";

        private static int SeverityRank(string severity) => severity switch
        {
            "Error" => 2,
            "Warning" => 1,
            _ => 0,
        };

        private static DisplayRow ToDisplayRow(LogEntry entry)
        {
            string severity = entry.Severity.ToString();
            string eventType = entry.EventType.ToString();
            string detail = entry switch
            {
                CallLogEntry c => $"call → {c.TargetService} ({c.DurationMs}ms) req={c.RequestId}",
                RequestLogEntry q => $"{q.Method} {q.Path} → {q.StatusCode} req={q.RequestId}",
                InternalLogEntry i => $"{i.ExceptionName}: {i.ExceptionMessage}",
                _ => "",
            };
            return new DisplayRow(
                entry.LineNo,
                entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                entry.PodName,
                severity,
                eventType,
                detail,
                severity switch
                {
                    "Error" => "sev-error",
                    "Warning" => "sev-warning",
                    _ => "sev-info",
                });
        }
        // ===== T5.1 end =====

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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }
                if (!int.TryParse(DegreeOfParallelismText, out var degree) || degree < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"Invalid degree of parallelism: {DegreeOfParallelismText}");
                    return;
                }
                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degree,
                };
                request.FileNames.AddRange(SelectedFiles);
                var response = await _client!.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                await RefreshAsync();
            });
        }

        /*
         * TODO: T4.1
         * Add AnalyzeAllAsync ReplayCommand
         */
        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!int.TryParse(DegreeOfParallelismText, out var degree) || degree < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"Invalid degree of parallelism: {DegreeOfParallelismText}");
                    return;
                }
                var request = new AnalyzeAllRequest()
                {
                    DegreeOfParallelism = degree,
                };
                var response = await _client!.AnalyzeAllAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                await RefreshAsync();
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }
                if (!int.TryParse(DegreeOfParallelismText, out var degree) || degree < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"Invalid degree of parallelism: {DegreeOfParallelismText}");
                    return;
                }
                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degree,
                };
                request.FileNames.Add(SelectedLogFile.FileName);
                var response = await _client!.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                await RefreshAsync();
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }
                var request = new GetAnalysisResultRequest()
                {
                    FileName = SelectedLogFile.FileName,
                };
                ResultEntries.Clear();
                _allRows.Clear();
                using var call = _client!.GetAnalysisResult(request);
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error",
                            $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                    {
                        var header = response.Header;
                        switch (header.State)
                        {
                            case AnalysisStateEnum.Failed:
                                ResultEntries.Add(new LogFields(0,
                                    new List<LogFieldItem>(), header.ErrorMessage));
                                break;
                            case AnalysisStateEnum.NotAnalyzed:
                                ResultEntries.Add(new LogFields(0,
                                    new List<LogFieldItem>(),
                                    "This file has not been analyzed yet."));
                                break;
                            case AnalysisStateEnum.Succeeded:
                                // Header 只标记状态，条目随后到达
                                break;
                        }
                    }
                    else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        var visitor = new KeyValueVisitor();
                        var dict = visitor.Dump(entry);
                        var fields = dict.Select(kv => new LogFieldItem(kv.Key, kv.Value)).ToList();
                        ResultEntries.Add(new LogFields((int)entry.LineNo, fields, null));
                        _allRows.Add(ToDisplayRow(entry));
                    }
                }
                ApplyFilter();
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
