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
        private string _currentAddress = "http://localhost:5000";
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

        private async Task<int?> ReadDegreeOfParallelismAsync()
        {
            if (!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) || degreeOfParallelism < 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error", "Degree of parallelism must be a non-negative integer.");
                return null;
            }

            return degreeOfParallelism;
        }

        private async Task<bool> EnsureSuccessAsync(OperationStatusMessage status)
        {
            if (status.Success)
            {
                return true;
            }

            await DialogHelper.ShowMessageDialogAsync(
                "Error",
                $"{status.Code}: {status.Message}");
            return false;
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
                if (!await EnsureSuccessAsync(response.Status))
                {
                    return;
                }

                DirectoryPath = response.CurrentDirectory;
                SelectedLogFile = null;
                SelectedFiles = Array.Empty<string>();
                ResultEntries.Clear();
                LogFiles.Clear();
                foreach (var fileName in response.FileNames)
                {
                    LogFiles.Add(new LogFileItem(fileName));
                }
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

                LogFiles.Clear();
                SelectedLogFile = null;
                SelectedFiles = Array.Empty<string>();
                ResultEntries.Clear();
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
                    await DialogHelper.ShowMessageDialogAsync("Error", "Please select at least one log file.");
                    return;
                }

                var degreeOfParallelism = await ReadDegreeOfParallelismAsync();
                if (degreeOfParallelism is null)
                {
                    return;
                }

                var response = await _client!.AnalyzeFilesAsync(new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = degreeOfParallelism.Value,
                    FileNames = { SelectedFiles },
                });
                await EnsureSuccessAsync(response.Status);
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                var degreeOfParallelism = await ReadDegreeOfParallelismAsync();
                if (degreeOfParallelism is null)
                {
                    return;
                }

                var response = await _client!.AnalyzeAllAsync(new AnalyzeAllRequest
                {
                    DegreeOfParallelism = degreeOfParallelism.Value,
                });
                await EnsureSuccessAsync(response.Status);
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No log file selected.");
                    return;
                }

                var degreeOfParallelism = await ReadDegreeOfParallelismAsync();
                if (degreeOfParallelism is null)
                {
                    return;
                }

                var response = await _client!.AnalyzeFilesAsync(new AnalyzeFilesRequest
                {
                    DegreeOfParallelism = degreeOfParallelism.Value,
                    FileNames = { SelectedLogFile.FileName },
                });
                await EnsureSuccessAsync(response.Status);
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                var request = new GetAnalysisResultRequest()
                {
                    FileName = SelectedLogFile?.FileName ?? string.Empty,
                };
                if (string.IsNullOrWhiteSpace(request.FileName))
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "Please select a log file.");
                    return;
                }

                using var call = _client!.GetAnalysisResult(request, cancellationToken: default);
                ResultEntries.Clear();
                int idx = 1;
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        ResultEntries.Add(new LogFields(idx++, new List<LogFieldItem>
                        {
                            new("Type", "Error"),
                            new("Code", response.Status.Code.ToString()),
                            new("Message", response.Status.Message),
                        }, response.Status.Message));
                        continue;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            ResultEntries.Add(new LogFields(idx++, new List<LogFieldItem>
                            {
                                new("Type", "Header"),
                                new("FileName", response.Header.FileName),
                                new("FullName", response.Header.FullName),
                                new("State", response.Header.State.ToString()),
                                new("ErrorMessage", response.Header.ErrorMessage ?? string.Empty),
                                new("WorkerId", response.Header.WorkerId.ToString()),
                            }, response.Header.ErrorMessage));
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var fields = new KeyValueVisitor().Dump(entry)
                                .Select(pair => new LogFieldItem(pair.Key, pair.Value))
                                .Prepend(new LogFieldItem("Type", "LogEntry"))
                                .ToList();
                            ResultEntries.Add(new LogFields(idx++, fields, null));
                            break;
                        default:
                            ResultEntries.Add(new LogFields(
                                idx++,
                                new List<LogFieldItem>
                                {
                                    new("Type", "Error"),
                                    new("Message", "The agent returned an empty response."),
                                },
                                "The agent returned an empty response."));
                            break;
                    }
                }
            }
            );
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
