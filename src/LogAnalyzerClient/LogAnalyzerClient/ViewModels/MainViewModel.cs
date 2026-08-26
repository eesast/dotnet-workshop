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
            await WithClientNotNull(async () =>
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
            await WithClientNotNull(async () =>
            {
                if (SelectedFiles == null || SelectedFiles.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "No log files selected.");
                    return;
                }

                if (!int.TryParse(DegreeOfParallelismText, out int parallelism) || parallelism < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = parallelism
                };
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
            await WithClientNotNull(async () =>
            {
                if (!int.TryParse(DegreeOfParallelismText, out int parallelism) || parallelism < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeAllRequest
                {
                    DegreeOfParallelism = parallelism
                };

                var response = await _client!.AnalyzeAllAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null || string.IsNullOrEmpty(SelectedLogFile.FileName))
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "No log file selected.");
                    return;
                }

                if (!int.TryParse(DegreeOfParallelismText, out int parallelism) || parallelism < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = parallelism
                };
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
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null || string.IsNullOrEmpty(SelectedLogFile.FileName))
                {
                    await DialogHelper.ShowMessageDialogAsync("Warning", "Please select a log file to view analysis results.");
                    return;
                }

                var fileName = SelectedLogFile.FileName;
                var request = new GetAnalysisResultRequest
                {
                    FileName = fileName
                };

                ResultEntries.Clear();

                using var call = _client!.GetAnalysisResult(request);
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
                            var header = response.Header;
                            switch (header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), $"File '{fileName}' has not been analyzed yet."));
                                    break;
                                case AnalysisStateEnum.Failed:
                                    ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), $"Analysis failed for '{fileName}': {header.ErrorMessage}"));
                                    break;
                                case AnalysisStateEnum.Succeeded:
                                    break;
                            }
                            break;

                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            if (entry != null)
                            {
                                ResultEntries.Add(new LogFields(0, Array.Empty<LogFieldItem>(), entry.ToString()));
                            }
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

