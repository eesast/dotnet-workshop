using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerClient.Models;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LogAnalyzerClient.ViewModels
{
    public partial class MainViewModel
    {
        public const string FilterAny = "(Any)";

        public IReadOnlyList<string> EventTypeFilterOptions { get; } =
            new[] { FilterAny, "Call", "Request", "Internal" };

        public IReadOnlyList<string> SeverityFilterOptions { get; } =
            new[] { FilterAny, "Info", "Warning", "Error" };

        public IReadOnlyList<string> SortKeyOptions { get; } = LogRowSort.Keys;

        [ObservableProperty]
        private ObservableCollection<LogEntryRow> _resultRows = new();

        [ObservableProperty]
        private string _resultInfo = "No analysis result yet. Select a file and click \"View Analysis Results\".";

        [ObservableProperty]
        private string _statsText = "";

        // ---- Sort settings ----
        [ObservableProperty]
        private string _selectedSortKey = LogRowSort.Keys[0];

        [ObservableProperty]
        private bool _sortDescending = false;

        // ---- Query (filter) settings ----
        [ObservableProperty]
        private string _selectedEventTypeFilter = FilterAny;

        [ObservableProperty]
        private string _selectedSeverityFilter = FilterAny;

        [ObservableProperty]
        private string _serviceNameFilter = "";

        [ObservableProperty]
        private string _requestIdFilter = "";

        [ObservableProperty]
        private string _startTimeFilter = "";

        [ObservableProperty]
        private string _endTimeFilter = "";

        [RelayCommand]
        private async Task QueryLogsAsync()
        {
            await WithClientNotNull(async () =>
            {
                if (SelectedLogFile is null)
                {
                    await DialogHelper.ShowMessageDialogAsync("Error", "No file is selected.");
                    return;
                }

                if (!TryBuildQueryRequest(out var request, out var errorMessage))
                {
                    await DialogHelper.ShowMessageDialogAsync("Invalid query", errorMessage);
                    return;
                }

                var response = await _client!.QueryLogEntriesAsync(request);
                if (!response.Status.Success)
                {
                    ShowQueryError($"{response.Status.Code}: {response.Status.Message}");
                    await DialogHelper.ShowMessageDialogAsync("Query failed",
                        $"{response.Status.Code}: {response.Status.Message}");
                    return;
                }

                var rows = response.LogEntries
                    .Select(entryMessage => LogEntryRow.FromEntry(GrpcTypeConverter.ConvertFromGrpc(entryMessage)))
                    .ToList();
                ShowRows(rows, $"{SelectedLogFile.FileName} · query result · matched {rows.Count} of {response.TotalCount} entries");
            });
        }

        [RelayCommand]
        private void ApplySort()
        {
            if (ResultRows.Count == 0)
            {
                return;
            }
            // ShowRows re-applies the current sort settings and refreshes the statistics.
            ShowRows(ResultRows.ToList(), ResultInfo);
        }

        [RelayCommand]
        private void ResetFilters()
        {
            SelectedEventTypeFilter = FilterAny;
            SelectedSeverityFilter = FilterAny;
            ServiceNameFilter = string.Empty;
            RequestIdFilter = string.Empty;
            StartTimeFilter = string.Empty;
            EndTimeFilter = string.Empty;
        }

        private void ClearResultRows()
        {
            ResultRows.Clear();
            StatsText = string.Empty;
        }

        private void ShowQueryError(string message)
        {
            ResultRows.Clear();
            ResultInfo = message;
            StatsText = string.Empty;
        }

        private void ShowRows(IReadOnlyCollection<LogEntryRow> rows, string info)
        {
            var sorted = rows.OrderBy(row => row, LogRowSort.CreateComparer(SelectedSortKey, SortDescending)).ToList();
            ResultRows.Clear();
            foreach (var row in sorted)
            {
                ResultRows.Add(row);
            }
            ResultInfo = info;
            UpdateStats();
        }

        private void UpdateStats()
        {
            var infoCount = 0;
            var warningCount = 0;
            var errorCount = 0;
            var callCount = 0;
            var requestCount = 0;
            var internalCount = 0;
            foreach (var row in ResultRows)
            {
                switch (row.Severity)
                {
                    case "Info": infoCount++; break;
                    case "Warning": warningCount++; break;
                    case "Error": errorCount++; break;
                }
                switch (row.EventType)
                {
                    case "Call": callCount++; break;
                    case "Request": requestCount++; break;
                    case "Internal": internalCount++; break;
                }
            }
            StatsText = $"Total {ResultRows.Count}    Severity: Info {infoCount} / Warning {warningCount} / Error {errorCount}    Event type: Call {callCount} / Request {requestCount} / Internal {internalCount}";
        }

        private bool TryBuildQueryRequest(out QueryLogEntriesRequest request, out string errorMessage)
        {
            request = new QueryLogEntriesRequest
            {
                FileName = SelectedLogFile!.FileName,
            };
            errorMessage = string.Empty;

            if (SelectedEventTypeFilter != FilterAny)
            {
                if (!TryParseEventType(SelectedEventTypeFilter, out var eventType))
                {
                    errorMessage = $"Invalid event type: {SelectedEventTypeFilter}";
                    return false;
                }
                request.EventType = eventType;
            }

            if (SelectedSeverityFilter != FilterAny)
            {
                if (!TryParseSeverity(SelectedSeverityFilter, out var severity))
                {
                    errorMessage = $"Invalid severity: {SelectedSeverityFilter}";
                    return false;
                }
                request.Severity = severity;
            }

            var serviceName = ServiceNameFilter?.Trim();
            if (!string.IsNullOrEmpty(serviceName))
            {
                request.ServiceName = serviceName;
            }

            var requestId = RequestIdFilter?.Trim();
            if (!string.IsNullOrEmpty(requestId))
            {
                request.RequestId = requestId;
            }

            if (!string.IsNullOrWhiteSpace(StartTimeFilter))
            {
                if (!DateTimeOffset.TryParse(StartTimeFilter, out var startTime))
                {
                    errorMessage = $"Invalid start time: \"{StartTimeFilter}\". Use an ISO 8601 value such as 2026-06-05T16:00:00Z.";
                    return false;
                }
                request.StartTime = startTime.ToUniversalTime().ToTimestamp();
            }

            if (!string.IsNullOrWhiteSpace(EndTimeFilter))
            {
                if (!DateTimeOffset.TryParse(EndTimeFilter, out var endTime))
                {
                    errorMessage = $"Invalid end time: \"{EndTimeFilter}\". Use an ISO 8601 value such as 2026-06-05T16:03:00Z.";
                    return false;
                }
                request.EndTime = endTime.ToUniversalTime().ToTimestamp();
            }

            return true;
        }

        private static bool TryParseEventType(string text, out LogEventTypeEnum eventType)
        {
            switch (text.Trim())
            {
                case "Call":
                    eventType = LogEventTypeEnum.Call;
                    return true;
                case "Request":
                    eventType = LogEventTypeEnum.Request;
                    return true;
                case "Internal":
                    eventType = LogEventTypeEnum.Internal;
                    return true;
                default:
                    eventType = LogEventTypeEnum.Call;
                    return false;
            }
        }

        private static bool TryParseSeverity(string text, out LogSeverityEnum severity)
        {
            switch (text.Trim())
            {
                case "Info":
                    severity = LogSeverityEnum.Info;
                    return true;
                case "Warning":
                    severity = LogSeverityEnum.Warning;
                    return true;
                case "Error":
                    severity = LogSeverityEnum.Error;
                    return true;
                default:
                    severity = LogSeverityEnum.Info;
                    return false;
            }
        }
    }
}
