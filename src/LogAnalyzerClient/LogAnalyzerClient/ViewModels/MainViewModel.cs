using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc.Protos;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LogAnalyzerClient.ViewModels;

using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

public partial class MainViewModel : ViewModelBase
{
    internal IDialogHelper DialogHelper { get; set; } = new NullDialogHelper();

    private LogAnalyzerAgentServiceClient? _client;
    private string? _resultFileName;

    public IReadOnlyList<string> SelectedFiles { get; set; } = [];

    public IReadOnlyList<FilterOption> EventTypeFilters { get; } =
    [
        new("All event types", null),
        new("Call", nameof(LogEventTypeEnum.Call)),
        new("Request", nameof(LogEventTypeEnum.Request)),
        new("Internal", nameof(LogEventTypeEnum.Internal)),
    ];

    public IReadOnlyList<FilterOption> SeverityFilters { get; } =
    [
        new("All severities", null),
        new("Info", nameof(LogSeverityEnum.Info)),
        new("Warning", nameof(LogSeverityEnum.Warning)),
        new("Error", nameof(LogSeverityEnum.Error)),
    ];

    public IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100, 200];
    public ObservableCollection<FilterOption> ServiceFilters { get; } =
    [
        new("All services", null),
    ];

    [ObservableProperty] private string _directoryPath = "";
    [ObservableProperty] private string _currentDirectory = "";
    [ObservableProperty] private string _degreeOfParallelismText = "1";
    [ObservableProperty] private string _currentAddress = "";

    private static class ConnectStatusString
    {
        public const string NotConnected = "Not connected";
        public const string Connecting = "Connecting";
        public const string Connected = "Connected";
        public const string ConnectFailed = "Connection failed";
    }

    [ObservableProperty] private string _connectStatus = ConnectStatusString.NotConnected;
    [ObservableProperty] private ObservableCollection<LogFileItem> _logFiles = [];
    [ObservableProperty] private LogFileItem? _selectedLogFile;
    [ObservableProperty] private ObservableCollection<LogTableRow> _resultEntries = [];
    [ObservableProperty] private FilterOption _selectedEventTypeFilter;
    [ObservableProperty] private FilterOption _selectedSeverityFilter;
    [ObservableProperty] private FilterOption _selectedServiceFilter;
    [ObservableProperty] private DateTimeOffset? _startDate;
    [ObservableProperty] private TimeSpan? _startTime;
    [ObservableProperty] private DateTimeOffset? _endDate;
    [ObservableProperty] private TimeSpan? _endTime;
    [ObservableProperty] private string _requestIdFilter = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _selectedPageSize = 50;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private bool _canGoToPreviousPage;
    [ObservableProperty] private bool _canGoToNextPage;
    [ObservableProperty] private string _resultTitle = "Analysis Result";
    [ObservableProperty] private string _resultSummary = "Select an analyzed file to view its entries.";
    [ObservableProperty] private string _resultMessage = "No result loaded.";
    [ObservableProperty] private bool _isResultMessageVisible = true;
    [ObservableProperty] private string _sortDescription = "Line, ascending";

    private LogSortFieldEnum _sortField = LogSortFieldEnum.LineNo;
    private bool _sortDescending;

    public MainViewModel()
    {
        _selectedEventTypeFilter = EventTypeFilters[0];
        _selectedSeverityFilter = SeverityFilters[0];
        _selectedServiceFilter = ServiceFilters[0];
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var address = await DialogHelper.ShowConnectDialogAsync(CurrentAddress);
        if (address is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            await DialogHelper.ShowMessageDialogAsync("Error", "Address cannot be empty.");
            return;
        }

        try
        {
            address = address.Trim();
            ConnectStatus = ConnectStatusString.Connecting;
            _client = AppService.ClientFactory.CreateClient(address);
            await _client.PingAsync(new Empty());
            CurrentAddress = address;
            ConnectStatus = ConnectStatusString.Connected;
            DirectoryPath = "";
            CurrentDirectory = "";
            LogFiles.Clear();
            ClearResult("No result loaded.");
        }
        catch (Exception ex)
        {
            _client = null;
            CurrentAddress = "";
            DirectoryPath = "";
            CurrentDirectory = "";
            LogFiles.Clear();
            ClearResult("No result loaded.");
            ConnectStatus = ConnectStatusString.ConnectFailed;
            await DialogHelper.ShowMessageDialogAsync(
                "Error",
                $"Failed to connect to agent: {ex.Message}");
            ConnectStatus = ConnectStatusString.NotConnected;
        }
    }

    private async Task WithClientNotNull(Func<Task> action)
    {
        if (_client is null)
        {
            await DialogHelper.ShowMessageDialogAsync(
                "Error",
                "Agent is not connected. Please connect to an agent first.");
            return;
        }

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowMessageDialogAsync("Error", $"Error occurred: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ChangeDirectoryAsync()
    {
        await WithClientNotNull(async () =>
        {
            var response = await _client!.ChangeDirectoryAsync(new ChangeDirectoryRequest
            {
                DirectoryPath = DirectoryPath,
            });
            if (!response.Status.Success)
            {
                await DialogHelper.ShowMessageDialogAsync(
                    "Error",
                    $"{response.Status.Code}: {response.Status.Message}");
                DirectoryPath = CurrentDirectory;
                return;
            }

            DirectoryPath = response.CurrentDirectory;
            CurrentDirectory = response.CurrentDirectory;
            ClearResult("No result loaded.");
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
            SelectedFiles = [];
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
            if (await EnsureSuccessAsync(response.Status) && SelectedLogFile is not null)
            {
                await GetAnalysisResultAsync();
            }
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
            if (await EnsureSuccessAsync(response.Status) && SelectedLogFile is not null)
            {
                await GetAnalysisResultAsync();
            }
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
            if (await EnsureSuccessAsync(response.Status))
            {
                await GetAnalysisResultAsync();
            }
        });
    }

    [RelayCommand]
    private async Task GetAnalysisResultAsync()
    {
        if (!await TryGetSelectedLogFileAsync())
        {
            return;
        }

        await SelectLogFileAsync(SelectedLogFile!);
    }

    public async Task SelectLogFileAsync(LogFileItem file)
    {
        if (!string.Equals(_resultFileName, file.FileName, StringComparison.Ordinal))
        {
            ResetFilterValues(clearServiceOptions: true);
        }

        SelectedLogFile = file;
        _resultFileName = file.FileName;
        CurrentPage = 1;
        await QueryAnalysisResultAsync();
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        ResetFilterValues(clearServiceOptions: false);
        CurrentPage = 1;

        if (_resultFileName is not null)
        {
            await QueryAnalysisResultAsync();
        }
    }

    private void ResetFilterValues(bool clearServiceOptions)
    {
        SelectedEventTypeFilter = EventTypeFilters[0];
        SelectedSeverityFilter = SeverityFilters[0];
        if (clearServiceOptions)
        {
            UpdateServiceFilters([]);
        }
        else
        {
            SelectedServiceFilter = ServiceFilters[0];
        }
        StartDate = null;
        StartTime = null;
        EndDate = null;
        EndTime = null;
        RequestIdFilter = "";
        SearchText = "";
    }

    [RelayCommand]
    private async Task ChangeSortAsync(string? fieldName)
    {
        if (!System.Enum.TryParse<LogSortFieldEnum>(fieldName, out var requestedSort))
        {
            return;
        }

        if (_sortField == requestedSort)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortField = requestedSort;
            _sortDescending = false;
        }

        SortDescription = $"{GetSortLabel(_sortField)}, {(_sortDescending ? "descending" : "ascending")}";
        CurrentPage = 1;
        if (_resultFileName is not null)
        {
            await QueryAnalysisResultAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!CanGoToPreviousPage || _resultFileName is null)
        {
            return;
        }
        CurrentPage--;
        await QueryAnalysisResultAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoToNextPage || _resultFileName is null)
        {
            return;
        }
        CurrentPage++;
        await QueryAnalysisResultAsync();
    }

    private async Task QueryAnalysisResultAsync()
    {
        if (_resultFileName is null)
        {
            return;
        }

        var request = await BuildQueryRequestAsync();
        if (request is null)
        {
            return;
        }

        await WithClientNotNull(async () =>
        {
            QueryAnalysisResultHeaderMessage? header = null;
            var entries = new List<LogEntryMessage>();
            using var call = _client!.QueryAnalysisResult(request);
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                if (!await EnsureSuccessAsync(response.Status))
                {
                    return;
                }

                switch (response.PayloadCase)
                {
                    case QueryAnalysisResultResponse.PayloadOneofCase.Header:
                        header = response.Header;
                        break;
                    case QueryAnalysisResultResponse.PayloadOneofCase.LogEntry:
                        entries.Add(response.LogEntry);
                        break;
                    default:
                        throw new ClientInternalException("Agent returned an empty query payload.");
                }
            }

            if (string.Equals(request.FileName, _resultFileName, StringComparison.Ordinal))
            {
                ShowQueryResult(header, entries);
            }
        });
    }

    private async Task<QueryAnalysisResultRequest?> BuildQueryRequestAsync()
    {
        var request = new QueryAnalysisResultRequest
        {
            FileName = _resultFileName,
            ServiceName = SelectedServiceFilter.Value ?? "",
            RequestId = RequestIdFilter.Trim(),
            SearchText = SearchText.Trim(),
            SortField = _sortField,
            SortDescending = _sortDescending,
            PageNumber = CurrentPage,
            PageSize = SelectedPageSize,
        };

        if (SelectedEventTypeFilter.Value is not null)
        {
            request.EventType = System.Enum.Parse<LogEventTypeEnum>(SelectedEventTypeFilter.Value);
        }
        if (SelectedSeverityFilter.Value is not null)
        {
            request.Severity = System.Enum.Parse<LogSeverityEnum>(SelectedSeverityFilter.Value);
        }

        if (StartDate is null && StartTime is not null)
        {
            await DialogHelper.ShowMessageDialogAsync(
                "Invalid start time",
                "Select a start date before selecting a start time.");
            return null;
        }
        if (EndDate is null && EndTime is not null)
        {
            await DialogHelper.ShowMessageDialogAsync(
                "Invalid end time",
                "Select an end date before selecting an end time.");
            return null;
        }

        DateTimeOffset? startTime = StartDate is null
            ? null
            : CombineDateAndTime(StartDate.Value, StartTime, endOfDay: false);
        DateTimeOffset? endTime = EndDate is null
            ? null
            : CombineDateAndTime(EndDate.Value, EndTime, endOfDay: true);
        if (startTime is not null && endTime is not null && startTime > endTime)
        {
            await DialogHelper.ShowMessageDialogAsync(
                "Invalid time range",
                "Start time must not be later than end time.");
            return null;
        }

        if (startTime is not null)
        {
            request.StartTime = Timestamp.FromDateTimeOffset(startTime.Value);
        }
        if (endTime is not null)
        {
            request.EndTime = Timestamp.FromDateTimeOffset(endTime.Value);
        }
        return request;
    }

    private static DateTimeOffset CombineDateAndTime(
        DateTimeOffset date,
        TimeSpan? time,
        bool endOfDay)
    {
        var defaultTime = endOfDay
            ? TimeSpan.FromDays(1) - TimeSpan.FromTicks(1)
            : TimeSpan.Zero;
        var dateTime = DateTime.SpecifyKind(
            date.Date + (time ?? defaultTime),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(dateTime, date.Offset);
    }

    private void ShowQueryResult(
        QueryAnalysisResultHeaderMessage? header,
        IReadOnlyList<LogEntryMessage> entries)
    {
        if (header?.Analysis is null)
        {
            throw new ClientInternalException("Agent did not return a query result header.");
        }

        ResultTitle = $"Analysis Result - {header.Analysis.FileName}";
        UpdateServiceFilters(header.ServiceNames);
        switch (header.Analysis.State)
        {
            case AnalysisStateEnum.NotAnalyzed:
                ClearResult($"{header.Analysis.FileName} has not been analyzed yet.", keepTitle: true);
                break;
            case AnalysisStateEnum.Failed:
                ClearResult(
                    header.Analysis.HasErrorMessage
                        ? header.Analysis.ErrorMessage
                        : $"Analysis failed for {header.Analysis.FileName}.",
                    keepTitle: true);
                break;
            case AnalysisStateEnum.Succeeded:
                ResultEntries = new ObservableCollection<LogTableRow>(
                    entries.Select(LogTableRow.FromGrpc));
                CurrentPage = header.PageNumber;
                TotalPages = Math.Max(1, (int)Math.Ceiling(header.TotalCount / (double)header.PageSize));
                CanGoToPreviousPage = CurrentPage > 1;
                CanGoToNextPage = CurrentPage < TotalPages;
                ResultSummary = $"{header.TotalCount} matched | {header.InfoCount} info | "
                    + $"{header.WarningCount} warning | {header.ErrorCount} error | "
                    + $"sorted by {SortDescription}";
                ResultMessage = header.TotalCount == 0
                    ? "No log entries match the current filters."
                    : "";
                IsResultMessageVisible = header.TotalCount == 0;
                break;
            default:
                throw new ClientInternalException(
                    $"Unknown analysis state: {header.Analysis.State}.");
        }
    }

    private void ClearResult(string message, bool keepTitle = false)
    {
        _resultFileName = keepTitle ? _resultFileName : null;
        ResultEntries.Clear();
        if (!keepTitle)
        {
            ResultTitle = "Analysis Result";
            UpdateServiceFilters([]);
        }
        ResultSummary = "";
        ResultMessage = message;
        IsResultMessageVisible = true;
        CurrentPage = 1;
        TotalPages = 1;
        CanGoToPreviousPage = false;
        CanGoToNextPage = false;
    }

    private void UpdateServiceFilters(IEnumerable<string> serviceNames)
    {
        var selectedValue = SelectedServiceFilter?.Value;
        ServiceFilters.Clear();
        ServiceFilters.Add(new FilterOption("All services", null));
        foreach (var serviceName in serviceNames)
        {
            ServiceFilters.Add(new FilterOption(serviceName, serviceName));
        }

        SelectedServiceFilter = ServiceFilters.FirstOrDefault(
            option => string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
            ?? ServiceFilters[0];
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
        await DialogHelper.ShowMessageDialogAsync(
            "Error",
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

    private static string GetSortLabel(LogSortFieldEnum field) => field switch
    {
        LogSortFieldEnum.LineNo => "Line",
        LogSortFieldEnum.Timestamp => "Timestamp",
        LogSortFieldEnum.PodName => "Service",
        LogSortFieldEnum.Severity => "Severity",
        LogSortFieldEnum.EventType => "Type",
        LogSortFieldEnum.RequestId => "Request ID",
        _ => field.ToString(),
    };

    [RelayCommand]
    private async Task AboutAsync()
    {
        await DialogHelper.ShowMessageDialogAsync(
            "About",
            "LogAnalyzerClient\nEESAST Software Center\nhttps://github.com/eesast/dotnet-workshop");
    }
}
