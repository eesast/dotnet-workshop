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
        private string _greeting = "Welcome to Avalonia!";

        [ObservableProperty]
        private string _directoryPath = "";

        [ObservableProperty]
        private string _degreeOfParallelismText = "1";

        [ObservableProperty]
        private string _currentAddress = "";

        [ObservableProperty]
        private string _token = "";
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

        [RelayCommand]
        private async Task ConnectAsync()
        {
            var connectInfo = await DialogHelper.ShowConnectDialogAsync(CurrentAddress, Token);
            if (connectInfo is null)
            {
                // Do nothing if the user cancels the dialog
            }
            else if (string.IsNullOrEmpty(connectInfo.Address) || string.IsNullOrWhiteSpace(connectInfo.Address.Trim()))
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Address cannot be empty.");
            }
            else if (string.IsNullOrEmpty(connectInfo.Token) || string.IsNullOrWhiteSpace(connectInfo.Token.Trim()))
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Token cannot be empty.");
            }
            else
            {
                try
                {
                    ConnectStatus = ConnectStatusString.CONNECTING;
                    _client = AppService.ClientFactory.CreateClient(connectInfo.Address, connectInfo.Token);
                    await _client.PingAsync(new Empty());
                    CurrentAddress = connectInfo.Address;
                    Token = connectInfo.Token;
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
                var request = new Empty();
                var response = await _client!.GetLogFilesAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                }
                else
                {
                    LogFiles.Clear();
                    foreach (var filename in response.FileNames)
                    {
                        LogFiles.Add(new LogFileItem(filename));
                    }
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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No files selected for analysis.");
                    return;
                }

                if (!int.TryParse(DegreeOfParallelismText, out int degreeOfParallelism) || degreeOfParallelism <= 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a positive integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degreeOfParallelism,
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
                if (!int.TryParse(DegreeOfParallelismText, out int degreeOfParallelism) || degreeOfParallelism <= 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a positive integer.");
                    return;
                }

                var request = new AnalyzeAllRequest()
                {
                    DegreeOfParallelism = degreeOfParallelism,
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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No selected file for analysis.");
                    return;
                }

                if (!int.TryParse(DegreeOfParallelismText, out int degreeOfParallelism) || degreeOfParallelism <= 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a positive integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = degreeOfParallelism,
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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No selected file");
                    return;
                }
                ResultEntries.Clear();

                var fileName = SelectedLogFile.FileName;
                var request = new GetAnalysisResultRequest()
                {
                    FileName = fileName
                };

                using var call = _client!.GetAnalysisResult(request);
                var analysisSucceeded = false;
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error",
                            $"{response.Status.Code}: {response.Status.Message}");
                        break;
                    }
                    else
                    {
                        if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                        {
                            var state = GrpcTypeConverter.ConvertFromGrpc(response.Header.State);
                            if (state == LogAnalyzer.AnalysisState.NotAnalyzed)
                            {
                                await DialogHelper.ShowMessageDialogAsync(
                                    "Analysis Result",
                                    $"{fileName} has not been analyzed yet.");
                                break;
                            }
                            else if (state == LogAnalyzer.AnalysisState.Failed)
                            {
                                await DialogHelper.ShowMessageDialogAsync(
                                    "Analysis Failed",
                                    $"{fileName} analysis failed.\n"
                                    + $"Error message: {response.Header.ErrorMessage}\n"
                                    + $"Worker ID: {response.Header.WorkerId}");
                                break;
                            }
                            else if (state == LogAnalyzer.AnalysisState.Succeeded)
                            {
                                analysisSucceeded = true;
                            }
                        }
                        else if (analysisSucceeded
                            && response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                        {
                            var logEntry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            ResultEntries.Add(LogEntryRow.FromLogEntry(logEntry));
                        }
                    }
                }
            });
        }

        [RelayCommand]
        private async Task SaveAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No selected file to save.");
                    return;
                }

                string directoryPath = await DialogHelper.ShowSaveAnalysisResultDialogAsync(SelectedLogFile.FileName);
                var request = new SaveAnalysisResultRequest
                {
                    FileName = SelectedLogFile.FileName,
                    DirectoryPath = directoryPath
                };

                var response = await _client!.SaveAnalysisResultAsync(request);
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                await DialogHelper.ShowMessageDialogAsync("Saved",
                    $"Analysis result saved successfully.\n\n{response.OutputFilePath}");
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
