using Google.Protobuf.WellKnownTypes;
using LogAnalyzerAgent.Applications;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace test_03_async_grpc;

[TestClass]
public sealed class Test_5_1_LogEntryQuery
{
    private static readonly DateTimeOffset BaseTime =
        DateTimeOffset.Parse("2026-07-01T08:00:00Z");

    [TestMethod(DisplayName = "T5.1 Query combines all specified conditions")]
    public void QueryCombinesConditions()
    {
        var entries = CreateEntries();
        var request = new QueryAnalysisResultRequest
        {
            FileName = "sample.log",
            EventType = LogEventTypeEnum.Call,
            Severity = LogSeverityEnum.Info,
            ServiceName = "gateway",
            RequestId = "request-a",
            StartTime = Timestamp.FromDateTimeOffset(BaseTime),
            EndTime = Timestamp.FromDateTimeOffset(BaseTime.AddMinutes(2)),
            PageNumber = 1,
            PageSize = 50,
        };

        var result = LogEntryQuery.Execute(entries, request);

        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(1, result.InfoCount);
        Assert.AreEqual(0, result.WarningCount);
        Assert.AreEqual(0, result.ErrorCount);
        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(1, result.Entries[0].LineNo);
        Assert.AreEqual("gateway", LogEntryQuery.GetServiceName("gateway-1"));
        Assert.AreEqual("gateway-blue", LogEntryQuery.GetServiceName("gateway-blue"));
    }

    [TestMethod(DisplayName = "T5.2 Query searches subtype fields and pages sorted results")]
    public void QuerySearchesAndPagesSortedResults()
    {
        var searchResult = LogEntryQuery.Execute(CreateEntries(), new QueryAnalysisResultRequest
        {
            FileName = "sample.log",
            SearchText = "timeout",
            PageNumber = 1,
            PageSize = 25,
        });

        Assert.AreEqual(1, searchResult.TotalCount);
        Assert.IsInstanceOfType<InternalLogEntry>(searchResult.Entries[0]);

        var pageResult = LogEntryQuery.Execute(CreateEntries(), new QueryAnalysisResultRequest
        {
            FileName = "sample.log",
            SortField = LogSortFieldEnum.Timestamp,
            SortDescending = true,
            PageNumber = 2,
            PageSize = 2,
        });

        Assert.AreEqual(5, pageResult.TotalCount);
        Assert.AreEqual(2, pageResult.PageNumber);
        Assert.HasCount(2, pageResult.Entries);
        Assert.AreEqual(3, pageResult.Entries[0].LineNo);
        Assert.AreEqual(2, pageResult.Entries[1].LineNo);
    }

    [TestMethod(DisplayName = "T5.2 Query validates paging and time ranges")]
    public void QueryRejectsInvalidArguments()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LogEntryQuery.Execute(CreateEntries(), new QueryAnalysisResultRequest
            {
                PageNumber = 0,
                PageSize = 50,
            }));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LogEntryQuery.Execute(CreateEntries(), new QueryAnalysisResultRequest
            {
                PageNumber = 1,
                PageSize = LogEntryQuery.MaxPageSize + 1,
            }));

        Assert.ThrowsExactly<ArgumentException>(() =>
            LogEntryQuery.Execute(CreateEntries(), new QueryAnalysisResultRequest
            {
                StartTime = Timestamp.FromDateTimeOffset(BaseTime.AddMinutes(2)),
                EndTime = Timestamp.FromDateTimeOffset(BaseTime),
                PageNumber = 1,
                PageSize = 50,
            }));
    }

    [TestMethod(DisplayName = "T5.2 Query response lists services in the analyzed file")]
    public void QueryResponseListsAvailableServices()
    {
        var analyzer = new LogFileAnalyzer("dataset");
        analyzer.AnalyzeFiles(1, ["basic-multiple.log"]);
        var session = new AgentSession(analyzer, NullLoggerFactory.Instance);

        var responses = session.QueryAnalysisResult(new QueryAnalysisResultRequest
        {
            FileName = "basic-multiple.log",
            PageNumber = 1,
            PageSize = 25,
        }, CancellationToken.None);

        Assert.IsTrue(responses[0].Status.Success);
        Assert.AreEqual(
            QueryAnalysisResultResponse.PayloadOneofCase.Header,
            responses[0].PayloadCase);
        Assert.Contains("gateway", responses[0].Header.ServiceNames);
        Assert.Contains("authservice", responses[0].Header.ServiceNames);
        Assert.IsFalse(responses[0].Header.ServiceNames.Any(name => name == "gateway-1"));
    }

    private static IReadOnlyList<LogEntry> CreateEntries() =>
    [
        new CallLogEntry(
            1, BaseTime, "gateway-1", LogSeverity.Info,
            "request-a", "authservice", 40),
        new CallLogEntry(
            2, BaseTime.AddMinutes(1), "gateway-2", LogSeverity.Warning,
            "request-b", "userservice", 250),
        new RequestLogEntry(
            3, BaseTime.AddMinutes(2), "gateway-1", LogSeverity.Info,
            "request-c", "GET", "/api/orders", 200),
        new InternalLogEntry(
            4, BaseTime.AddMinutes(3), "authservice-0", LogSeverity.Error,
            "System.TimeoutException", "Request timed out"),
        new RequestLogEntry(
            5, BaseTime.AddMinutes(4), "userservice-0", LogSeverity.Info,
            "request-d", "POST", "/api/users", 201),
    ];
}
