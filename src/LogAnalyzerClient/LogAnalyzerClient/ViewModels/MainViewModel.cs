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
using System.Diagnostics;
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
                var response = await _client!.GetLogFilesAsync(new Empty());
                if (!response.Status.Success) {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
                LogFiles.Clear();
                foreach (var filename in response.FileNames) {
                    LogFiles.Add(new LogFileItem(filename));
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeSelectedFilesAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!int.TryParse(DegreeOfParallelismText, out var dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "The DegreeOfParallelism is invalid!");
                    return;
                }
                if (SelectedFiles.Count == 0) {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No files selected.");
                    return;
                }
                var response = await _client!.AnalyzeFilesAsync(new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = dop,
                    FileNames = { SelectedFiles, },
                });
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!int.TryParse(DegreeOfParallelismText, out var dop) || dop < 0) {
                    await DialogHelper.ShowMessageDialogAsync("Error", "The DegreeOfParallelism is invalid!");
                    return;
                }
                var response = await _client!.AnalyzeAllAsync(new AnalyzeAllRequest { DegreeOfParallelism = dop, });
                if (!response.Status.Success) {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }
            });
        }


        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                var file = SelectedLogFile?.FileName;
                if (file is null) {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }
                if (!int.TryParse(DegreeOfParallelismText, out var dop) || dop < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "The DegreeOfParallelism is invalid!");
                    return;
                }
                var response = await _client!.AnalyzeFilesAsync(new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = dop,
                    FileNames = { file, },
                });
                if (!response.Status.Success) {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                var file = SelectedLogFile?.FileName;
                if (file is null) {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file selected.");
                    return;
                }
                ResultEntries.Clear();
                using var call = _client!.GetAnalysisResult(new GetAnalysisResultRequest { FileName = file, });
                while (await call.ResponseStream.MoveNext())
                {
                    var entry = call.ResponseStream.Current;
                    switch (entry.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            switch (entry.Header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    ResultEntries.Add(new LogFields(0, new List<LogFieldItem>(), null));
                                    break;
                                case AnalysisStateEnum.Failed:
                                    var error = entry.Header.HasErrorMessage ? entry.Header.ErrorMessage : "unknown error";
                                    ResultEntries.Add(new LogFields(0, new List<LogFieldItem>(), error));
                                    break;
                                case AnalysisStateEnum.Succeeded:
                                    break;
                                default: break;
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var fields = new List<LogFieldItem>();
                            int lineNo;
                            switch (entry.LogEntry.EntryCase)
                            {
                                case LogEntryMessage.EntryOneofCase.CallLogEntry:
                                    lineNo = entry.LogEntry.CallLogEntry.LineNo;
                                    fields.Add(new LogFieldItem("severity", entry.LogEntry.CallLogEntry.Severity.ToString()));
                                    fields.Add(new LogFieldItem("pod", entry.LogEntry.CallLogEntry.PodName));
                                    fields.Add(new LogFieldItem("request_id", entry.LogEntry.CallLogEntry.RequestId));
                                    fields.Add(new LogFieldItem("target_service", entry.LogEntry.CallLogEntry.TargetService));
                                    fields.Add(new LogFieldItem("duration_ms", entry.LogEntry.CallLogEntry.DurationMs.ToString()));
                                    break;
                                case LogEntryMessage.EntryOneofCase.RequestLogEntry:
                                    lineNo = entry.LogEntry.RequestLogEntry.LineNo;
                                    fields.Add(new LogFieldItem("severity", entry.LogEntry.RequestLogEntry.Severity.ToString()));
                                    fields.Add(new LogFieldItem("pod", entry.LogEntry.RequestLogEntry.PodName));
                                    fields.Add(new LogFieldItem("request_id", entry.LogEntry.RequestLogEntry.RequestId));
                                    fields.Add(new LogFieldItem("method", entry.LogEntry.RequestLogEntry.Method));
                                    fields.Add(new LogFieldItem("path", entry.LogEntry.RequestLogEntry.Path));
                                    fields.Add(new LogFieldItem("status_code", entry.LogEntry.RequestLogEntry.StatusCode.ToString()));
                                    break;
                                case LogEntryMessage.EntryOneofCase.InternalLogEntry:
                                    lineNo = entry.LogEntry.InternalLogEntry.LineNo;
                                    fields.Add(new LogFieldItem("severity", entry.LogEntry.InternalLogEntry.Severity.ToString()));
                                    fields.Add(new LogFieldItem("pod", entry.LogEntry.InternalLogEntry.PodName));
                                    fields.Add(new LogFieldItem("exception", entry.LogEntry.InternalLogEntry.ExceptionName));
                                    fields.Add(new LogFieldItem("message", entry.LogEntry.InternalLogEntry.ExceptionMessage));
                                    break;
                                default:
                                    return;
                            }
                            ResultEntries.Add(new LogFields(lineNo, fields, null));
                            break;
                        default:
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
