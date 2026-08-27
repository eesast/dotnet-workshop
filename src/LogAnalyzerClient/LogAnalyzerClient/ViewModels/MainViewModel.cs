using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
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
        private readonly List<LogEntry> _allResultEntries = new();

        public IReadOnlyList<string> SelectedFiles { get; set; } = new List<string>();

        public IReadOnlyList<LogTypeFilter> LogTypeOptions { get; } =
            System.Enum.GetValues<LogTypeFilter>();

        public IReadOnlyList<CallCountSort> CallCountSortOptions { get; } =
            System.Enum.GetValues<CallCountSort>();

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

        [ObservableProperty]
        private string _analysisStatus = "Select an analyzed log file to view its entries.";

        [ObservableProperty]
        private LogTypeFilter _selectedLogType = LogTypeFilter.All;

        [ObservableProperty]
        private CallCountSort _selectedCallCountSort = CallCountSort.None;

        public bool IsCallCountSortEnabled => SelectedLogType == LogTypeFilter.Call;

        [ObservableProperty]
        private ObservableCollection<TopologyNodeItem> _topologyNodes = new();

        [ObservableProperty]
        private ObservableCollection<TopologyEdgeItem> _topologyEdges = new();

        [ObservableProperty]
        private ObservableCollection<LogFields> _topologyEdgeLogEntries = new();

        [ObservableProperty]
        private TopologyEdgeItem? _selectedTopologyEdge = null;

        [ObservableProperty]
        private string _topologyStatus = "Select a log file and open its service topology.";

        [ObservableProperty]
        private int _selectedResultTabIndex = 0;

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

                LogFiles = new ObservableCollection<LogFileItem>(
                    response.FileNames.Select(fileName => new LogFileItem(fileName)));
            });
        }

        [RelayCommand]
        private async Task AnalyzeSelectedFilesAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) ||
                    degreeOfParallelism < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                if (SelectedFiles.Count == 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Please select at least one log file.");
                    return;
                }

                var request = new AnalyzeFilesRequest
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
                if (!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) ||
                    degreeOfParallelism < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeAllRequest
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
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Please select a log file.");
                    return;
                }

                if (!int.TryParse(DegreeOfParallelismText, out var degreeOfParallelism) ||
                    degreeOfParallelism < 0)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Degree of parallelism must be a non-negative integer.");
                    return;
                }

                var request = new AnalyzeFilesRequest
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
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Please select a log file.");
                    return;
                }

                SelectedResultTabIndex = 0;
                _allResultEntries.Clear();
                ResultEntries.Clear();
                AnalysisStatus = "Loading analysis result...";
                using var call = _client!.GetAnalysisResult(new GetAnalysisResultRequest
                {
                    FileName = SelectedLogFile.FileName,
                });

                var receivedResponse = false;
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    receivedResponse = true;
                    if (!response.Status.Success)
                    {
                        await DialogHelper.ShowMessageDialogAsync("Error",
                            $"{response.Status.Code}: {response.Status.Message}");
                        return;
                    }

                    switch (response.PayloadCase)
                    {
                        case GetAnalysisResultResponse.PayloadOneofCase.Header:
                            switch (response.Header.State)
                            {
                                case AnalysisStateEnum.NotAnalyzed:
                                    AnalysisStatus =
                                        $"File {response.Header.FileName} has not been analyzed yet.";
                                    break;
                                case AnalysisStateEnum.Succeeded:
                                    AnalysisStatus =
                                        $"File: {response.Header.FileName}; Worker ID: {response.Header.WorkerId}";
                                    break;
                                case AnalysisStateEnum.Failed:
                                    var errorMessage = response.Header.HasErrorMessage
                                        ? response.Header.ErrorMessage
                                        : "Unknown error.";
                                    AnalysisStatus = $"Analysis failed: {errorMessage}";
                                    break;
                                default:
                                    throw new ClientInternalException(
                                        $"Unknown analysis state: {response.Header.State}.");
                            }
                            break;
                        case GetAnalysisResultResponse.PayloadOneofCase.LogEntry:
                            var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                            _allResultEntries.Add(entry);
                            break;
                        default:
                            throw new ClientInternalException(
                                "The agent returned an invalid analysis result.");
                    }
                }

                if (!receivedResponse)
                {
                    throw new ClientInternalException(
                        "The agent returned no analysis result.");
                }

                ApplyResultView();
            });
        }

        partial void OnSelectedLogTypeChanged(LogTypeFilter value)
        {
            OnPropertyChanged(nameof(IsCallCountSortEnabled));

            if (!IsCallCountSortEnabled && SelectedCallCountSort != CallCountSort.None)
            {
                SelectedCallCountSort = CallCountSort.None;
                return;
            }

            ApplyResultView();
        }

        partial void OnSelectedCallCountSortChanged(CallCountSort value)
        {
            ApplyResultView();
        }

        private void ApplyResultView()
        {
            var callCounts = _allResultEntries
                .OfType<CallLogEntry>()
                .GroupBy(entry => entry.TargetService, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            IEnumerable<LogEntry> entries = SelectedLogType switch
            {
                LogTypeFilter.Call => _allResultEntries.OfType<CallLogEntry>(),
                LogTypeFilter.Request => _allResultEntries.OfType<RequestLogEntry>(),
                LogTypeFilter.Internal => _allResultEntries.OfType<InternalLogEntry>(),
                _ => _allResultEntries,
            };

            if (SelectedLogType == LogTypeFilter.Call)
            {
                entries = SelectedCallCountSort switch
                {
                    CallCountSort.Ascending => entries
                        .OrderBy(entry => callCounts[((CallLogEntry)entry).TargetService])
                        .ThenBy(entry => entry.LineNo),
                    CallCountSort.Descending => entries
                        .OrderByDescending(entry => callCounts[((CallLogEntry)entry).TargetService])
                        .ThenBy(entry => entry.LineNo),
                    _ => entries.OrderBy(entry => entry.LineNo),
                };
            }

            var dumper = new KeyValueVisitor();
            var visibleEntries = entries
                .Select((entry, index) =>
                {
                    var fields = dumper.Dump(entry)
                        .Select(pair => new LogFieldItem(pair.Key, pair.Value))
                        .ToList();

                    if (entry is CallLogEntry callEntry)
                    {
                        fields.Add(new LogFieldItem(
                            "CallCount",
                            callCounts[callEntry.TargetService].ToString()));
                    }

                    return new LogFields(index, fields, null);
                });

            ResultEntries = new ObservableCollection<LogFields>(visibleEntries);
        }

        [RelayCommand]
        private async Task ShowServiceTopologyAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        "Please select a log file.");
                    return;
                }

                var response = await _client!.GetServiceTopologyAsync(
                    new GetServiceTopologyRequest
                    {
                        FileName = SelectedLogFile.FileName,
                    });
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                var edges = response.Edges
                    .Select(edge => new TopologyEdgeItem(
                        edge.SourceService,
                        edge.TargetService,
                        edge.CallCount))
                    .ToArray();
                var nodes = TopologyLayout.Arrange(
                    response.Nodes.Select(node => node.Name),
                    edges);

                TopologyEdges = new ObservableCollection<TopologyEdgeItem>(edges);
                TopologyNodes = new ObservableCollection<TopologyNodeItem>(nodes);
                TopologyEdgeLogEntries.Clear();
                SelectedTopologyEdge = null;
                TopologyStatus = edges.Length == 0
                    ? $"No service calls were found in {SelectedLogFile.FileName}."
                    : $"Topology for {SelectedLogFile.FileName}. Click an edge count to load its {edges.Sum(edge => edge.CallCount)} call logs.";
                SelectedResultTabIndex = 1;
            });
        }

        [RelayCommand]
        private async Task GetTopologyEdgeLogsAsync(TopologyEdgeItem? edge)
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null || edge is null)
                {
                    return;
                }

                var response = await _client!.GetTopologyEdgeLogsAsync(
                    new GetTopologyEdgeLogsRequest
                    {
                        FileName = SelectedLogFile.FileName,
                        SourceService = edge.SourceService,
                        TargetService = edge.TargetService,
                    });
                if (!response.Status.Success)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                SelectedTopologyEdge = edge;
                TopologyEdgeLogEntries.Clear();
                var dumper = new KeyValueVisitor();
                for (var index = 0; index < response.Entries.Count; index++)
                {
                    var entry = GrpcTypeConverter.ConvertFromGrpc(new LogEntryMessage
                    {
                        CallLogEntry = response.Entries[index],
                    });
                    var fields = dumper.Dump(entry)
                        .Select(pair => new LogFieldItem(pair.Key, pair.Value))
                        .ToList();
                    TopologyEdgeLogEntries.Add(new LogFields(index, fields, null));
                }
                TopologyStatus = $"{edge.Summary}; loaded {response.Entries.Count} matching logs.";
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
