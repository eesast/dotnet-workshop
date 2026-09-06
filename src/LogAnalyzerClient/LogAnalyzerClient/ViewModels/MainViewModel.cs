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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private ObservableCollection<LogTableRow> _resultRows = new();

        [ObservableProperty]
        private string _resultStatusText = "";

        [ObservableProperty]
        private string _sortKey = "LineNo";

        [ObservableProperty]
        private bool _sortDescending = false;

        [ObservableProperty]
        private string _queryRequestId = "";

        [ObservableProperty]
        private string _queryServiceName = "";

        [ObservableProperty]
        private string _querySeverity = "All";

        [ObservableProperty]
        private string _queryEventType = "All";

        [ObservableProperty]
        private string _queryStartTime = "";

        [ObservableProperty]
        private string _queryEndTime = "";

        public IReadOnlyList<string> SortKeys { get; } =
        [
            "LineNo", "Timestamp", "PodName", "Severity", "EventType",
            "RequestId", "TargetService", "DurationMs", "Method", "Path",
            "StatusCode", "ExceptionName", "ExceptionMessage",
        ];

        public IReadOnlyList<string> SeverityFilterOptions { get; } = ["All", "Info", "Warning", "Error"];

        public IReadOnlyList<string> EventTypeFilterOptions { get; } = ["All", "Call", "Request", "Internal"];

        private List<Dictionary<string, string>> _resultFieldList = new();

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
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file is selected. Please select at least one file first.");
                    return;
                }
                if (!TryGetDegreeOfParallelism(out var degree))
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest
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
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!TryGetDegreeOfParallelism(out var degree))
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeAllRequest
                {
                    DegreeOfParallelism = degree,
                };
                var response = await _client!.AnalyzeAllAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
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
                        "No file is selected. Please right-click a file first.");
                    return;
                }
                if (!TryGetDegreeOfParallelism(out var degree))
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest
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
                        "No file is selected. Please right-click a file first.");
                    return;
                }

                await LoadResultAsync(SelectedLogFile.FileName, null);
            });
        }

        [RelayCommand]
        private async Task QueryAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "No file is selected. Please right-click a file first.");
                    return;
                }
                if (!TryBuildQueryCriteria(out var criteria, out var error))
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", error!);
                    return;
                }

                await LoadResultAsync(SelectedLogFile.FileName, criteria);
            });
        }

        [RelayCommand]
        private async Task ClearFilterAsync()
        {
            QueryRequestId = "";
            QueryServiceName = "";
            QuerySeverity = "All";
            QueryEventType = "All";
            QueryStartTime = "";
            QueryEndTime = "";
            await GetAnalysisResultAsync();
        }

        [RelayCommand]
        private void SortRows()
        {
            RebuildResultView();
        }

        [RelayCommand]
        private async Task ExportJsonAsync()
        {
            if (_resultFieldList.Count == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error",
                    "No log entries to export. Please view or query analysis results first.");
                return;
            }

            var path = await DialogHelper.ShowSaveFileDialogAsync("analysis-export.json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(_resultFieldList,
                    new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);
                await DialogHelper.ShowMessageDialogAsync("Export",
                    $"Exported {_resultFieldList.Count} entries to {path}");
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowMessageDialogAsync("Error",
                    $"Failed to export JSON: {ex.Message}");
            }
        }

        private async Task LoadResultAsync(string fileName, LogQueryCriteria? criteria)
        {
            ResultStatusText = $"Loading results for '{fileName}'...";
            ResultRows.Clear();
            _resultFieldList = new List<Dictionary<string, string>>();

            var fieldList = new List<Dictionary<string, string>>();
            var visitor = new KeyValueVisitor();
            string status = "";
            bool succeeded = false;

            AsyncServerStreamingCall<GetAnalysisResultResponse> call = criteria is null
                ? _client!.GetAnalysisResult(new GetAnalysisResultRequest { FileName = fileName })
                : _client!.QueryAnalysisResult(new QueryAnalysisResultRequest
                {
                    FileName = fileName,
                    Criteria = criteria,
                });

            using (call)
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        status = $"Error: {response.Status.Code}: {response.Status.Message}";
                        continue;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            var header = response.Header;
                            switch (header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    status = $"File '{fileName}' has not been analyzed yet.";
                                    break;
                                case AnalysisStateEnum.Failed:
                                    status = $"Analysis failed: {header.ErrorMessage}";
                                    break;
                                case AnalysisStateEnum.Succeeded:
                                    succeeded = true;
                                    status = $"File '{fileName}' analysis succeeded.";
                                    break;
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var fields = visitor.Dump(entry)
                                .ToDictionary(pair => pair.Key, pair => pair.Value);
                            fieldList.Add(fields);
                            break;
                    }
                }
            }

            if (succeeded)
            {
                status = $"File '{fileName}' analysis succeeded ({fieldList.Count} entries).";
            }
            else if (fieldList.Count == 0 && string.IsNullOrEmpty(status))
            {
                status = "No result.";
            }

            _resultFieldList = fieldList;
            ResultStatusText = status;
            RebuildResultView();
        }

        private void RebuildResultView()
        {
            IEnumerable<Dictionary<string, string>> ordered = _resultFieldList;
            if (!string.IsNullOrEmpty(SortKey))
            {
                var key = SortKey;
                var comparer = Comparer<string>.Create((x, y) => CompareFieldValues(key, x, y));
                ordered = SortDescending
                    ? ordered.OrderByDescending(f => f.GetValueOrDefault(key, ""), comparer)
                    : ordered.OrderBy(f => f.GetValueOrDefault(key, ""), comparer);
            }

            ResultRows.Clear();
            foreach (var fields in ordered)
            {
                ResultRows.Add(LogTableRow.FromFields(fields));
            }
        }

        private static int CompareFieldValues(string key, string x, string y)
        {
            if (key is "LineNo" or "StatusCode" or "DurationMs"
                && int.TryParse(x, out var xi) && int.TryParse(y, out var yi))
            {
                return xi.CompareTo(yi);
            }
            return string.CompareOrdinal(x, y);
        }

        private bool TryBuildQueryCriteria(out LogQueryCriteria? criteria, out string? error)
        {
            criteria = null;
            error = null;

            var result = new LogQueryCriteria();
            var requestId = QueryRequestId.Trim();
            if (requestId.Length > 0)
            {
                result.RequestId = requestId;
            }

            var serviceName = QueryServiceName.Trim();
            if (serviceName.Length > 0)
            {
                result.ServiceName = serviceName;
            }

            var severity = QuerySeverity switch
            {
                "Info" => LogSeverityEnum.Info,
                "Warning" => LogSeverityEnum.Warning,
                "Error" => LogSeverityEnum.Error,
                _ => (LogSeverityEnum?)null,
            };
            if (severity is not null)
            {
                result.Severity = severity.Value;
            }

            var eventType = QueryEventType switch
            {
                "Call" => LogEventTypeEnum.Call,
                "Request" => LogEventTypeEnum.Request,
                "Internal" => LogEventTypeEnum.Internal,
                _ => (LogEventTypeEnum?)null,
            };
            if (eventType is not null)
            {
                result.EventType = eventType.Value;
            }

            if (QueryStartTime.Trim().Length > 0)
            {
                if (!DateTimeOffset.TryParse(QueryStartTime.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var startTime))
                {
                    error = "Start time must be a valid date-time, e.g. 2026-06-05T16:00:00Z.";
                    return false;
                }
                result.StartTime = Timestamp.FromDateTimeOffset(startTime);
            }

            if (QueryEndTime.Trim().Length > 0)
            {
                if (!DateTimeOffset.TryParse(QueryEndTime.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var endTime))
                {
                    error = "End time must be a valid date-time, e.g. 2026-06-05T17:00:00Z.";
                    return false;
                }
                result.EndTime = Timestamp.FromDateTimeOffset(endTime);
            }

            criteria = result;
            return true;
        }

        private bool TryGetDegreeOfParallelism(out int degree)
        {
            if (int.TryParse(DegreeOfParallelismText.Trim(), out degree) && degree >= 0)
            {
                return true;
            }
            degree = 0;
            return false;
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
