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
using System.Text;
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
        private ObservableCollection<LogEntryRow> _resultEntries = new();

        [ObservableProperty]
        private string _resultMessage = "";

        [ObservableProperty]
        private string _queryEventType = "All";

        [ObservableProperty]
        private string _querySeverity = "All";

        [ObservableProperty]
        private string _queryService = "";

        [ObservableProperty]
        private string _queryRequestId = "";

        [ObservableProperty]
        private string _queryStartTime = "";

        [ObservableProperty]
        private string _queryEndTime = "";

        [ObservableProperty]
        private string _sortBy = "None";

        [ObservableProperty]
        private bool _sortAscending = true;

        public IReadOnlyList<string> EventTypeOptions { get; } = new[] { "All", "Call", "Request", "Internal" };
        public IReadOnlyList<string> SeverityOptions { get; } = new[] { "All", "Info", "Warning", "Error" };
        public IReadOnlyList<string> SortByOptions { get; } = new[] { "None", "LineNo", "Timestamp", "PodName", "Severity", "EventType" };

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
                var degree = await GetDegreeOfParallelismAsync();
                if (degree is null)
                {
                    return;
                }

                var fileNames = SelectedFiles.ToList();
                if (fileNames.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }

                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degree.Value,
                };
                request.FileNames.AddRange(fileNames);

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
                var degree = await GetDegreeOfParallelismAsync();
                if (degree is null)
                {
                    return;
                }

                var response = await _client!.AnalyzeAllAsync(new AnalyzeAllRequest()
                {
                    DegreeOfParallelism = degree.Value,
                });
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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }

                var degree = await GetDegreeOfParallelismAsync();
                if (degree is null)
                {
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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }

                var request = new GetAnalysisResultRequest()
                {
                    FileName = SelectedLogFile.FileName,
                };

                using var call = _client!.GetAnalysisResult(request);
                var responses = await call.ResponseStream.ReadAllAsync().ToListAsync();

                ResultEntries.Clear();
                ResultMessage = "";

                if (responses.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No response received.");
                    return;
                }

                var first = responses[0];
                if (!first.Status.Success)
                {
                    ResultMessage = first.Status.Message;
                    return;
                }

                if (first.PayloadCase != GetAnalysisResultResponse.PayloadOneofCase.Header)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Unexpected response.");
                    return;
                }

                var header = first.Header;
                switch (header.State)
                {
                    case AnalysisStateEnum.NotAnalyzed:
                        ResultMessage = $"File '{SelectedLogFile.FileName}' has not been analyzed yet.";
                        break;
                    case AnalysisStateEnum.Failed:
                        ResultMessage = $"Analysis failed: {header.ErrorMessage}";
                        break;
                    case AnalysisStateEnum.Succeeded:
                        foreach (var response in responses.Skip(1))
                        {
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            ResultEntries.Add(LogEntryRow.From(entry));
                        }
                        ResultMessage = $"Analysis succeeded: {ResultEntries.Count} entries.";
                        break;
                }
            });
        }

        [RelayCommand]
        private async Task QueryAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }

                var condition = new QueryCondition();
                if (QueryEventType != "All")
                {
                    condition.EventType = System.Enum.Parse<LogEventTypeEnum>(QueryEventType);
                }
                if (QuerySeverity != "All")
                {
                    condition.Severity = System.Enum.Parse<LogSeverityEnum>(QuerySeverity);
                }
                if (!string.IsNullOrWhiteSpace(QueryService))
                {
                    condition.Service = QueryService.Trim();
                }
                if (!string.IsNullOrWhiteSpace(QueryRequestId))
                {
                    condition.RequestId = QueryRequestId.Trim();
                }
                if (!string.IsNullOrWhiteSpace(QueryStartTime))
                {
                    condition.StartTime = QueryStartTime.Trim();
                }
                if (!string.IsNullOrWhiteSpace(QueryEndTime))
                {
                    condition.EndTime = QueryEndTime.Trim();
                }

                var request = new QueryLogEntriesRequest()
                {
                    FileName = SelectedLogFile.FileName,
                    Condition = condition,
                    SortBy = SortBy == "None" ? "" : SortBy,
                    SortAscending = SortAscending,
                };

                var response = await _client!.QueryLogEntriesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                ResultEntries.Clear();
                foreach (var entryMessage in response.Entries)
                {
                    ResultEntries.Add(LogEntryRow.From(GrpcTypeConverter.ConvertFromGrpc(entryMessage)));
                }
                ResultMessage = $"Query result: {ResultEntries.Count} entries.";
            });
        }

        [RelayCommand]
        private void ClearQuery()
        {
            QueryEventType = "All";
            QuerySeverity = "All";
            QueryService = "";
            QueryRequestId = "";
            QueryStartTime = "";
            QueryEndTime = "";
            SortBy = "None";
            SortAscending = true;
        }

        [RelayCommand]
        private async Task GetStatisticsAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }

                var response = await _client!.GetStatisticsAsync(new GetStatisticsRequest()
                {
                    FileName = SelectedLogFile.FileName,
                });
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("Severity:");
                foreach (var item in response.SeverityCounts)
                {
                    builder.AppendLine($"  {item.Key}: {item.Count}");
                }
                builder.AppendLine("Event type:");
                foreach (var item in response.EventTypeCounts)
                {
                    builder.AppendLine($"  {item.Key}: {item.Count}");
                }
                builder.AppendLine("Service:");
                foreach (var item in response.ServiceCounts)
                {
                    builder.AppendLine($"  {item.Key}: {item.Count}");
                }

                await DialogHelper.ShowMessageDialogAsync($"Statistics - {SelectedLogFile.FileName}", builder.ToString());
            });
        }

        private async Task<int?> GetDegreeOfParallelismAsync()
        {
            if (int.TryParse(DegreeOfParallelismText, out var degree) && degree >= 0)
            {
                return degree;
            }

            await DialogHelper.ShowMessageDialogAsync("Error",
                "Invalid degree of parallelism. Please input a non-negative integer.");
            return null;
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
