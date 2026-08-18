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
        private string _currentDirectory = "";

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
                    address = address.Trim();
                    ConnectStatus = ConnectStatusString.CONNECTING;
                    _client = AppService.ClientFactory.CreateClient(address);
                    await _client.PingAsync(new Empty());
                    CurrentAddress = address;
                    ConnectStatus = ConnectStatusString.CONNECTED;
                    DirectoryPath = "";
                    CurrentDirectory = "";
                    LogFiles.Clear();
                    ResultEntries.Clear();
                }
                catch (Exception ex)
                {
                    _client = null;
                    CurrentAddress = "";
                    DirectoryPath = "";
                    CurrentDirectory = "";
                    LogFiles.Clear();
                    ResultEntries.Clear();
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
                    DirectoryPath = CurrentDirectory;
                    return;
                }
                DirectoryPath = response.CurrentDirectory;
                CurrentDirectory = response.CurrentDirectory;
                await RefreshAsync();
            });
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            await WithClientNotNull(async () =>
            {
                var response = await _client!.GetLogFilesAsync(new Empty());
                if (!await EnsureSuccessAsync(response.Status))
                {
                    return;
                }

                LogFiles = new ObservableCollection<LogFileItem>(
                    response.FileNames.Select(fileName => new LogFileItem(fileName)));
                SelectedLogFile = null;
                SelectedFiles = Array.Empty<string>();
            });
        }

        [RelayCommand]
        private async Task AnalyzeSelectedFilesAsync()
        {
            if (SelectedFiles.Count == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Please select at least one file.");
                return;
            }

            if (!await TryGetDegreeOfParallelismAsync())
            {
                return;
            }

            await WithClientNotNull(async () =>
            {
                var response = await _client!.AnalyzeFilesAsync(new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = int.Parse(DegreeOfParallelismText),
                    FileNames = { SelectedFiles }
                });
                await EnsureSuccessAsync(response.Status);
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            if (!await TryGetDegreeOfParallelismAsync())
            {
                return;
            }

            await WithClientNotNull(async () =>
            {
                var response = await _client!.AnalyzeAllAsync(new AnalyzeAllRequest
                {
                    DegreeOfParallelism = int.Parse(DegreeOfParallelismText)
                });
                await EnsureSuccessAsync(response.Status);
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            if (!await TryGetSelectedLogFileAsync() || !await TryGetDegreeOfParallelismAsync())
            {
                return;
            }

            await WithClientNotNull(async () =>
            {
                var response = await _client!.AnalyzeFilesAsync(new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = int.Parse(DegreeOfParallelismText),
                    FileNames = { SelectedLogFile!.FileName }
                });
                await EnsureSuccessAsync(response.Status);
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            if (!await TryGetSelectedLogFileAsync())
            {
                return;
            }

            await WithClientNotNull(async () =>
            {
                var request = new GetAnalysisResultRequest
                {
                    FileName = SelectedLogFile!.FileName
                };
                AnalysisResultHeaderMessage? header = null;
                var entries = new List<LogEntryMessage>();

                using var call = _client!.GetAnalysisResult(request);
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!await EnsureSuccessAsync(response.Status))
                    {
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            header = response.Header;
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            entries.Add(response.LogEntry);
                            break;
                        default:
                            throw new ClientInternalException("Agent returned an empty analysis payload.");
                    }
                }

                ShowAnalysisResult(header, entries);
            });
        }

        private async Task<bool> EnsureSuccessAsync(OperationStatusMessage status)
        {
            if (status.Success)
            {
                return true;
            }

            await DialogHelper.ShowMessageDialogAsync("Error", $"{status.Code}: {status.Message}");
            return false;
        }

        private async Task<bool> TryGetDegreeOfParallelismAsync()
        {
            if (int.TryParse(DegreeOfParallelismText, out var value) && value >= 0)
            {
                DegreeOfParallelismText = value.ToString();
                return true;
            }

            await DialogHelper.ShowMessageDialogAsync("Error",
                "Degree of parallelism must be a non-negative integer.");
            return false;
        }

        private async Task<bool> TryGetSelectedLogFileAsync()
        {
            if (SelectedLogFile is not null)
            {
                return true;
            }

            await DialogHelper.ShowMessageDialogAsync("Error", "Please select a file first.");
            return false;
        }

        private void ShowAnalysisResult(
            AnalysisResultHeaderMessage? header,
            IReadOnlyList<LogEntryMessage> entries)
        {
            if (header is null)
            {
                throw new ClientInternalException("Agent did not return an analysis result header.");
            }

            switch (header.State)
            {
                case AnalysisStateEnum.NotAnalyzed:
                    ResultEntries = new ObservableCollection<LogFields>
                    {
                        new(0, Array.Empty<LogFieldItem>(), $"File {header.FileName} has not been analyzed yet.")
                    };
                    break;
                case AnalysisStateEnum.Failed:
                    ResultEntries = new ObservableCollection<LogFields>
                    {
                        new(0, Array.Empty<LogFieldItem>(),
                            $"Analysis failed for {header.FileName}: "
                            + (header.HasErrorMessage ? header.ErrorMessage : "Unknown error"))
                    };
                    break;
                case AnalysisStateEnum.Succeeded:
                    var visitor = new KeyValueVisitor();
                    ResultEntries = new ObservableCollection<LogFields>(entries.Select((message, index) =>
                    {
                        var fields = visitor.Dump(GrpcTypeConverter.ConvertFromGrpc(message))
                            .Select(pair => new LogFieldItem(pair.Key, pair.Value))
                            .ToList();
                        return new LogFields(index + 1, fields, null);
                    }));
                    break;
                default:
                    throw new ClientInternalException($"Unknown analysis state: {header.State}.");
            }
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
