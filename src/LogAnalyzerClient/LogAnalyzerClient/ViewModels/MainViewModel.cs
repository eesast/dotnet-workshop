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
using System.Net;
using System.Threading.Tasks;
using LogParser.Models;
using System.IO;

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

        [ObservableProperty] private string _queryPodName = "";
        [ObservableProperty] private string _queryRequestId = "";
        [ObservableProperty] private string _querySeverity = "";
        [ObservableProperty] private string _queryEventType = "";
        [ObservableProperty] private string _sortKey = "Timestamp";
        [ObservableProperty] private bool _sortDescending;
        [ObservableProperty] private ObservableCollection<LogTableRow> _queryRows = new();
        public IReadOnlyList<string> SortKeys { get; } = ["Timestamp", "Severity", "RequestId"];  
        
        private QueryLogEntriesRequest CreateQueryRequest()
        {
            var selectedFile = SelectedLogFile
                ?? LogFiles.FirstOrDefault(item => SelectedFiles.Contains(item.FileName));

            if (selectedFile is null)
                throw new InvalidOperationException("Please select a log file first.");

            var request = new QueryLogEntriesRequest
            {
                FileName = selectedFile.FileName,
                SortKey = SortKey switch
                {
                    "Severity" => LogSortKey.Severity,
                    "RequestId" => LogSortKey.RequestId,
                    _ => LogSortKey.Timestamp
                },
                Descending = SortDescending,
                PodName = QueryPodName.Trim(),
                RequestId = QueryRequestId.Trim()
            };
            if (System.Enum.TryParse<LogSeverityEnum>(QuerySeverity, true, out var severity)) request.Severity = severity;
            if (System.Enum.TryParse<LogEventTypeEnum>(QueryEventType, true, out var eventType)) request.EventType = eventType;
            return request;
        }

        private static LogTableRow ToLogTableRow(LogEntry entry) => entry switch
        {
            CallLogEntry call => new(call.LineNo, call.Timestamp.ToString("O"), call.PodName,
                call.Severity.ToString(), call.EventType.ToString(), call.RequestId,
                call.TargetService, "", "", "", call.DurationMs.ToString(), "", ""),
            RequestLogEntry request => new(request.LineNo, request.Timestamp.ToString("O"), request.PodName,
                request.Severity.ToString(), request.EventType.ToString(), request.RequestId,
                "", request.Method, request.Path, request.StatusCode.ToString(), "", "", ""),
            InternalLogEntry internalEntry => new(internalEntry.LineNo, internalEntry.Timestamp.ToString("O"), internalEntry.PodName,
                internalEntry.Severity.ToString(), internalEntry.EventType.ToString(), "",
                "", "", "", "", "", internalEntry.ExceptionName, internalEntry.ExceptionMessage),
            _ => throw new InvalidOperationException("Unknown log entry type.")
        };

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
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
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
            if (SelectedFiles.Count == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Please select at least one log file.");
                return;
            }
            if(!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) || degreeOfParallelism < 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                return;
            }
            await WithClientNotNull(async() =>
            {
                var request = new AnalyzeFilesRequest{DegreeOfParallelism = degreeOfParallelism};
                request.FileNames.AddRange(SelectedFiles);
                var response = await _client!.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            if (!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) || degreeOfParallelism < 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                return;
            }
            await WithClientNotNull(async() =>
            {
                var response = await _client!.AnalyzeAllAsync(new AnalyzeAllRequest
                {
                    DegreeOfParallelism = degreeOfParallelism
                });
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            if (SelectedLogFile is null)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Please select a log file first.");
                return;
            }
            if(!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) || degreeOfParallelism < 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                return;
            }
            await WithClientNotNull(async() =>
            {
                var request = new AnalyzeFilesRequest {DegreeOfParallelism = degreeOfParallelism};
                request.FileNames.Add(SelectedLogFile.FileName);
                var response = await _client!.AnalyzeFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            if (SelectedLogFile is null)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Please select a log file firest.");
                return;
            }
            await WithClientNotNull(async() =>
            {
                QueryRows.Clear();
                using var call = _client!.GetAnalysisResult(new GetAnalysisResultRequest
                {
                    FileName = SelectedLogFile.FileName
                });
                await foreach(var response in call.ResponseStream.ReadAllAsync())
                {
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header &&
                        response.Header.State != AnalysisStateEnum.Succeeded)
                    {
                        var message = response.Header.HasErrorMessage
                            ? response.Header.ErrorMessage
                            : "The Agent did not provide an error message.";
                        await DialogHelper.ShowMessageDialogAsync("Analysis result unavailable",
                            $"State: {response.Header.State}\nFile: {response.Header.FileName}\n{message}");
                        return;
                    }
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }
                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            QueryRows.Add(ToLogTableRow(entry));
                            break;
                    }
                }
            });
        }

        [RelayCommand]
        private async Task QueryAsync()
        {
            await WithClientNotNull(async () =>
            {
                QueryRows.Clear();
                using var queryCall = _client!.QueryLogEntries(CreateQueryRequest());
                await foreach (var response in queryCall.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Query error",
                            $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }
                    var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                    QueryRows.Add(ToLogTableRow(entry));
                }
            });
        }

        [RelayCommand]
        private async Task ExportCsvAsync()
        {
            if (QueryRows.Count == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Export", "There are no query results to export.");
                return;
            }

            static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
            var path = Path.Combine(Environment.CurrentDirectory,
                $"logscope-query-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var lines = new List<string> { "LineNo,Timestamp,PodName,Severity,EventType,RequestId,TargetService,Method,Path,StatusCode,DurationMs,ExceptionName,ExceptionMessage" };
            lines.AddRange(QueryRows.Select(row => string.Join(",", new[]{
                row.LineNo.ToString(), row.Timestamp, row.PodName, row.Severity, row.EventType,
                row.RequestId, row.TargetService, row.Method, row.Path, row.StatusCode,
                row.DurationMs, row.ExceptionName, row.ExceptionMessage
        }.Select(Csv))));
            await File.WriteAllLinesAsync(path, lines);
            await DialogHelper.ShowMessageDialogAsync("Export complete",
                $"Exported {QueryRows.Count} row(s) to {path}");
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
