using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;

namespace LogAnalyzerAgent.Applications;

public sealed record LogEntryQueryResult(
    IReadOnlyList<LogEntry> Entries,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int InfoCount,
    int WarningCount,
    int ErrorCount);

public static class LogEntryQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static string GetServiceName(string podName)
    {
        var separatorIndex = podName.LastIndexOf('-');
        if (separatorIndex > 0
            && int.TryParse(podName[(separatorIndex + 1)..], out _))
        {
            return podName[..separatorIndex];
        }

        return podName;
    }

    public static LogEntryQueryResult Execute(
        IEnumerable<LogEntry> source,
        QueryAnalysisResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        if (request.PageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PageNumber),
                "Page number must be greater than zero.");
        }

        var pageSize = request.PageSize == 0 ? DefaultPageSize : request.PageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PageSize),
                $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (request.StartTime is not null
            && request.EndTime is not null
            && request.StartTime.ToDateTimeOffset() > request.EndTime.ToDateTimeOffset())
        {
            throw new ArgumentException("Start time must not be later than end time.");
        }

        var query = source.Where(entry => Matches(entry, request));
        var matched = query.ToList();
        var sorted = Sort(matched, request.SortField, request.SortDescending);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matched.Count / (double)pageSize));
        var pageNumber = Math.Min(request.PageNumber, pageCount);
        var offset = checked((pageNumber - 1) * pageSize);

        return new LogEntryQueryResult(
            sorted.Skip(offset).Take(pageSize).ToList(),
            matched.Count,
            pageNumber,
            pageSize,
            matched.Count(entry => entry.Severity == LogSeverity.Info),
            matched.Count(entry => entry.Severity == LogSeverity.Warning),
            matched.Count(entry => entry.Severity == LogSeverity.Error));
    }

    private static bool Matches(LogEntry entry, QueryAnalysisResultRequest request)
    {
        if (request.HasEventType
            && entry.EventType != GrpcTypeConverter.ConvertFromGrpc(request.EventType))
        {
            return false;
        }

        if (request.StartTime is not null
            && entry.Timestamp < request.StartTime.ToDateTimeOffset())
        {
            return false;
        }

        if (request.EndTime is not null
            && entry.Timestamp > request.EndTime.ToDateTimeOffset())
        {
            return false;
        }

        if (request.HasSeverity
            && entry.Severity != GrpcTypeConverter.ConvertFromGrpc(request.Severity))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ServiceName)
            && !MatchesService(entry.PodName, request.ServiceName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.RequestId)
            && !string.Equals(
                GetRequestId(entry),
                request.RequestId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(request.SearchText)
            || GetSearchableValues(entry).Any(value => value.Contains(
                request.SearchText.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesService(string podName, string requestedService)
    {
        var serviceName = requestedService.Trim();
        return string.Equals(podName, serviceName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                GetServiceName(podName),
                serviceName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRequestId(LogEntry entry) => entry switch
    {
        CallLogEntry call => call.RequestId,
        RequestLogEntry request => request.RequestId,
        _ => string.Empty,
    };

    private static IEnumerable<string> GetSearchableValues(LogEntry entry)
    {
        yield return entry.PodName;
        yield return entry.Severity.ToString();
        yield return entry.EventType.ToString();
        yield return entry.Timestamp.ToString("O");

        switch (entry)
        {
            case CallLogEntry call:
                yield return call.RequestId;
                yield return call.TargetService;
                yield return call.DurationMs.ToString();
                break;
            case RequestLogEntry request:
                yield return request.RequestId;
                yield return request.Method;
                yield return request.Path;
                yield return request.StatusCode.ToString();
                break;
            case InternalLogEntry internalEntry:
                yield return internalEntry.ExceptionName;
                yield return internalEntry.ExceptionMessage;
                break;
        }
    }

    private static IOrderedEnumerable<LogEntry> Sort(
        IEnumerable<LogEntry> entries,
        LogSortFieldEnum sortField,
        bool descending)
    {
        Func<LogEntry, object> selector = sortField switch
        {
            LogSortFieldEnum.LineNo => entry => entry.LineNo,
            LogSortFieldEnum.Timestamp => entry => entry.Timestamp,
            LogSortFieldEnum.PodName => entry => entry.PodName,
            LogSortFieldEnum.Severity => entry => entry.Severity,
            LogSortFieldEnum.EventType => entry => entry.EventType,
            LogSortFieldEnum.RequestId => entry => GetRequestId(entry),
            _ => throw new ArgumentOutOfRangeException(nameof(sortField), sortField, null),
        };

        return descending
            ? entries.OrderByDescending(selector).ThenByDescending(entry => entry.LineNo)
            : entries.OrderBy(selector).ThenBy(entry => entry.LineNo);
    }
}
