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

        private readonly KeyValueVisitor _visitor = new();

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
                    await DialogHelper.ShowMessageDialogAsync("Error", "No files are selected.");
                    return;
                }

                if (!TryReadDegreeOfParallelism(out var degree))
                {
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
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!TryReadDegreeOfParallelism(out var degree))
                {
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
            });
        }
        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file is selected.");
                    return;
                }

                if (!TryReadDegreeOfParallelism(out var degree))
                {
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
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file is selected.");
                    return;
                }

                var fileName = SelectedLogFile.FileName;
                var request = new GetAnalysisResultRequest()
                {
                    FileName = fileName,
                };

                ResultEntries.Clear();

                using var call = _client!.GetAnalysisResult(request);
                var header = null as AnalysisResultHeaderMessage;
                var index = 0;
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        ResultEntries.Add(new LogFields(
                            index,
                            Array.Empty<LogFieldItem>(),
                            $"{fileName} · {response.Status.Code}: {response.Status.Message}"));
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            header = response.Header;
                            var fields = new List<LogFieldItem>
                            {
                                new LogFieldItem("FileName", header.FileName),
                                new LogFieldItem("State", header.State.ToString()),
                                new LogFieldItem("WorkerId", header.WorkerId.ToString()),
                            };
                            if (header.HasErrorMessage)
                            {
                                fields.Add(new LogFieldItem("ErrorMessage", header.ErrorMessage));
                            }
                            ResultEntries.Add(new LogFields(index, fields, null));
                            index++;
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            var dumped = _visitor.Dump(entry);
                            var entryFields = dumped.Select(kv => new LogFieldItem(kv.Key, kv.Value)).ToList();
                            ResultEntries.Add(new LogFields(index, entryFields, null));
                            index++;
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

        private bool TryReadDegreeOfParallelism(out int degree)
        {
            if (!int.TryParse(DegreeOfParallelismText, out degree) || degree < 0)
            {
                _ = DialogHelper.ShowMessageDialogAsync("Error", "Invalid degree of parallelism. Must be a non-negative integer.");
                return false;
            }
            return true;
        }
    }
}
