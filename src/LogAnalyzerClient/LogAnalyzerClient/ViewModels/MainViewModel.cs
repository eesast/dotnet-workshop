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
                    var normalizedAddress = address.Trim();
                    var client = AppService.ClientFactory.CreateClient(normalizedAddress);
                    await client.PingAsync(new Empty());
                    _client = client;
                    CurrentAddress = normalizedAddress;
                    ConnectStatus = ConnectStatusString.CONNECTED;
                    LogFiles.Clear();
                    SelectedFiles = Array.Empty<string>();
                    SelectedLogFile = null;
                    ResultEntries.Clear();
                }
                catch (Exception ex)
                {
                    ConnectStatus = ConnectStatusString.CONNECT_FAILED;
                    await DialogHelper.ShowMessageDialogAsync("Error", $"Failed to connect to agent: {ex.Message}");
                    ConnectStatus = _client is null
                        ? ConnectStatusString.NOT_CONNECTED
                        : ConnectStatusString.CONNECTED;
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

        private async Task<int?> GetDegreeOfParallelismAsync()
        {
            if (!int.TryParse(DegreeOfParallelismText?.Trim(), out var degreeOfParallelism)
                || degreeOfParallelism < 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error",
                    "Degree of parallelism must be a non-negative integer.");
                return null;
            }

            return degreeOfParallelism;
        }

        private async Task ShowFailedOperationAsync(OperationStatusMessage status)
        {
            await DialogHelper.ShowMessageDialogAsync("Error",
                $"{status.Code}: {status.Message}");
        }

        private async Task AnalyzeFilesAsync(IEnumerable<string> fileNames)
        {
            var degreeOfParallelism = await GetDegreeOfParallelismAsync();
            if (degreeOfParallelism is null)
            {
                return;
            }

            var names = fileNames
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Select(fileName => fileName.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (names.Count == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Error",
                    "Please select at least one log file.");
                return;
            }

            var request = new AnalyzeFilesRequest
            {
                DegreeOfParallelism = degreeOfParallelism.Value,
            };
            request.FileNames.AddRange(names);

            var response = await _client!.AnalyzeFilesAsync(request);
            if (!response.Status.Success)
            {
                await ShowFailedOperationAsync(response.Status);
                return;
            }

            await DialogHelper.ShowMessageDialogAsync("Analysis completed",
                $"Analyzed files: {string.Join(", ", names)}");
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
                    await ShowFailedOperationAsync(response.Status);
                    return;
                }

                DirectoryPath = response.CurrentDirectory;
                ResultEntries.Clear();
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
                    await ShowFailedOperationAsync(response.Status);
                    return;
                }

                LogFiles = new ObservableCollection<LogFileItem>(
                    response.FileNames.Select(fileName => new LogFileItem(fileName)));
                SelectedFiles = Array.Empty<string>();
                SelectedLogFile = null;
            });
        }

        [RelayCommand]
        private async Task AnalyzeSelectedFilesAsync()
        {
            await WithClientNotNull(() => AnalyzeFilesAsync(SelectedFiles));
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                var degreeOfParallelism = await GetDegreeOfParallelismAsync();
                if (degreeOfParallelism is null)
                {
                    return;
                }

                var response = await _client!.AnalyzeAllAsync(new AnalyzeAllRequest
                {
                    DegreeOfParallelism = degreeOfParallelism.Value,
                });
                if (!response.Status.Success)
                {
                    await ShowFailedOperationAsync(response.Status);
                    return;
                }

                await DialogHelper.ShowMessageDialogAsync("Analysis completed",
                    "All log files have been analyzed.");
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
                        "Please select a log file.");
                    return;
                }

                await AnalyzeFilesAsync(new[] { SelectedLogFile.FileName });
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
                        "Please select a log file.");
                    return;
                }

                ResultEntries.Clear();
                using var call = _client!.GetAnalysisResult(new GetAnalysisResultRequest
                {
                    FileName = SelectedLogFile.FileName,
                });
                var entries = new List<LogFields>();
                var visitor = new KeyValueVisitor();
                var index = 0;

                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        ResultEntries.Clear();
                        await ShowFailedOperationAsync(response.Status);
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            var header = response.Header;
                            switch (header.State)
                            {
                                case AnalysisStateEnum.Succeeded:
                                    entries.Add(new LogFields(-1,
                                    [
                                        new LogFieldItem("File", header.FileName),
                                        new LogFieldItem("Worker ID", header.WorkerId.ToString()),
                                    ], null));
                                    break;
                                case AnalysisStateEnum.Failed:
                                    entries.Add(new LogFields(-1, Array.Empty<LogFieldItem>(),
                                        $"Analysis failed: {header.ErrorMessage}"));
                                    break;
                                case AnalysisStateEnum.NotAnalyzed:
                                    entries.Add(new LogFields(-1, Array.Empty<LogFieldItem>(),
                                        $"File {header.FileName} has not been analyzed yet."));
                                    break;
                                default:
                                    throw new ClientInternalException(
                                        $"Unknown analysis state: {header.State}");
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var fields = visitor
                                .Dump(GrpcTypeConverter.ConvertFromGrpc(response.LogEntry))
                                .Select(field => new LogFieldItem(field.Key, field.Value))
                                .ToList();
                            entries.Add(new LogFields(index++, fields, null));
                            break;
                        default:
                            throw new ClientInternalException("The Agent returned an empty result payload.");
                    }
                }

                ResultEntries = new ObservableCollection<LogFields>(entries);
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
