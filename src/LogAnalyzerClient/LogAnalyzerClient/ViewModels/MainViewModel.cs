using Avalonia.Controls.Embedding.Offscreen;
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
                var response =await _client!.GetLogFilesAsync(new Empty());
                if (response.Status.Success)
                {
                    LogFiles.Clear();
                    foreach(var FileName in response.FileNames)
                    {
                        LogFiles.Add(new LogFileItem(FileName));
                    }
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", 
                $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }
        private bool TryPraseDegreeOfParallelism(out int dop)
        {
            if(!int.TryParse(DegreeOfParallelismText,out dop)||dop < 0)
            {
                _=DialogHelper.ShowMessageDialogAsync("Error", "Degree of Parallelism (DoP) must be a non-negative integer.");
                return false;
            }
            return true;
        }

        [RelayCommand]
        private async Task AnalyzeSelectedFilesAsync()
        {
            if (!TryPraseDegreeOfParallelism(out int dop))
            {
                return;
            }
            if (SelectedFiles == null || SelectedFiles.Count == 0)
            {
                await DialogHelper.ShowMessageDialogAsync("Warning", "Please select at least one file from the list.");
                return;
            }
            await WithClientNotNull(async () =>{
                var request=new AnalyzeFilesRequest
                {
                    DegreeOfParallelism=dop,
                };
                request.FileNames.AddRange(SelectedFiles);
                var response=await _client!.AnalyzeFilesAsync(request);
                if (response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Success", "Successfully submitted the analysis task for the selected files!");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", 
                $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeAllAsync()
        {
            if (!TryPraseDegreeOfParallelism(out int dop))
            {
                return;
            }
            await WithClientNotNull(async () =>
            {
                var request= new AnalyzeAllRequest
                {
                    DegreeOfParallelism=dop,
                };
                var response =await _client!.AnalyzeAllAsync(request);
                if (response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Success", "Successfully submitted the analysis task for all files!");
                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", 
                $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task AnalyzeRightClickedFileAsync()
        {
            if(!TryPraseDegreeOfParallelism(out int dop))
            {
                return;
            }
            if (SelectedLogFile == null)
            {
                await DialogHelper.ShowMessageDialogAsync("Warning", "Please select a file to analyze.");
                return;
            }
            await WithClientNotNull(async () =>
            {
                var request=new AnalyzeFilesRequest
                {
                    DegreeOfParallelism=dop,
                };
                request.FileNames.Add(SelectedLogFile.FileName);
                var response=await _client!.AnalyzeFilesAsync(request);
                if (response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Success", $"Successfully submitted the analysis task for {SelectedLogFile.FileName}!");

                }
                else
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", 
                $"{response.Status.Code}: {response.Status.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task GetAnalysisResultAsync()
        {
            if (SelectedLogFile == null)
            {
                await DialogHelper.ShowMessageDialogAsync("Warning", "Please select a file to view results.");
                return;
            }
            await WithClientNotNull(async () =>
            {
                ResultEntries.Clear();
                var request =new GetAnalysisResultRequest
                {
                    FileName=SelectedLogFile.FileName
                };
                using var call=_client!.GetAnalysisResult(request);
                try
                {
                    while(await call.ResponseStream.MoveNext(System.Threading.CancellationToken.None))
                    {
                        var response=call.ResponseStream.Current;
                        if (!response.Status.Success)
                        {
                            await DialogHelper.ShowMessageDialogAsync("Error", $"{response.Status.Code}: {response.Status.Message}");
                            break;
                        }
                        var fields =new List<LogFieldItem>();
                        string? errorMessage =null;
                        if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.Header)
                        {
                            var header = response.Header;
                            fields.Add(new LogFieldItem("Type", "Header"));
                            fields.Add(new LogFieldItem("State", header.State.ToString()));
                            
                            if (header.HasErrorMessage) 
                            {
                                errorMessage = header.ErrorMessage;
                            }
                        }
                        else if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                        {
                            var entry = response.LogEntry;
                            fields.Add(new LogFieldItem("Type", "LogEntry"));
                            if (entry.EntryCase == LogEntryMessage.EntryOneofCase.CallLogEntry)
                            {
                                fields.Add(new LogFieldItem("Severity", entry.CallLogEntry.Severity.ToString()));
                            }
                            else if (entry.EntryCase == LogEntryMessage.EntryOneofCase.RequestLogEntry)
                            {
                                fields.Add(new LogFieldItem("Severity", entry.RequestLogEntry.Severity.ToString()));
                            }
                            else if (entry.EntryCase == LogEntryMessage.EntryOneofCase.InternalLogEntry)
                            {
                                fields.Add(new LogFieldItem("Severity", entry.InternalLogEntry.Severity.ToString()));
                            }
                        }
                        ResultEntries.Add(new LogFields(ResultEntries.Count + 1, fields, errorMessage));
                    }
                }
                catch(RpcException ex)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", $"Stream interrupted: {ex.Status.Detail}");
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
