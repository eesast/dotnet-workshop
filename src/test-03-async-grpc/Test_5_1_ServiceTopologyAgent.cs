using LogAnalyzer;
using LogAnalyzerAgent.Applications;
using LogAnalyzerRpc.Protos;
using Microsoft.Extensions.Logging.Abstractions;

namespace test_03_async_grpc;

[TestClass]
public sealed class Test_5_1_ServiceTopologyAgent
{
    [TestMethod(DisplayName = "T5.1.a.d Query topology and edge logs from agent")]
    [Timeout(2000, CooperativeCancellation = true)]
    public async Task TestGetServiceTopologyAndEdgeLogs()
    {
        var analyzer = new LogFileAnalyzer("dataset");
        analyzer.AnalyzeFiles(1, ["basic.log"]);
        var session = new AgentSession(analyzer, NullLoggerFactory.Instance);

        var topologyResponse = await session.GetServiceTopology(
            new GetServiceTopologyRequest { FileName = "basic.log" },
            CancellationToken.None);

        Assert.IsTrue(topologyResponse.Status.Success);
        Assert.AreEqual(AgentErrorCode.NoAgentError, topologyResponse.Status.Code);
        CollectionAssert.AreEquivalent(
            new[] { "userservice", "authservice" },
            topologyResponse.Nodes.Select(node => node.Name).ToArray());
        Assert.HasCount(1, topologyResponse.Edges);

        var edge = topologyResponse.Edges[0];
        Assert.AreEqual("userservice", edge.SourceService);
        Assert.AreEqual("authservice", edge.TargetService);
        Assert.AreEqual(1, edge.CallCount);

        var logsResponse = await session.GetTopologyEdgeLogs(
            new GetTopologyEdgeLogsRequest
            {
                FileName = "basic.log",
                SourceService = edge.SourceService,
                TargetService = edge.TargetService,
            },
            CancellationToken.None);

        Assert.IsTrue(logsResponse.Status.Success);
        Assert.AreEqual(AgentErrorCode.NoAgentError, logsResponse.Status.Code);
        Assert.HasCount(1, logsResponse.Entries);
        Assert.AreEqual(0, logsResponse.Entries[0].LineNo);
        Assert.AreEqual("userservice-0", logsResponse.Entries[0].PodName);
        Assert.AreEqual("authservice", logsResponse.Entries[0].TargetService);
    }

    [TestMethod(DisplayName = "T5.1.a.d Reject invalid topology queries")]
    [Timeout(2000, CooperativeCancellation = true)]
    public async Task TestTopologyQueryErrors()
    {
        var analyzer = new LogFileAnalyzer("dataset");
        analyzer.AnalyzeFiles(1, ["basic.log"]);
        var session = new AgentSession(analyzer, NullLoggerFactory.Instance);

        var notAnalyzedResponse = await session.GetServiceTopology(
            new GetServiceTopologyRequest { FileName = "basic-multiple.log" },
            CancellationToken.None);
        Assert.IsFalse(notAnalyzedResponse.Status.Success);
        Assert.AreEqual(AgentErrorCode.InvalidOperation, notAnalyzedResponse.Status.Code);

        var missingFileResponse = await session.GetServiceTopology(
            new GetServiceTopologyRequest { FileName = "missing.log" },
            CancellationToken.None);
        Assert.IsFalse(missingFileResponse.Status.Success);
        Assert.AreEqual(AgentErrorCode.FileNotFound, missingFileResponse.Status.Code);

        var missingEdgeResponse = await session.GetTopologyEdgeLogs(
            new GetTopologyEdgeLogsRequest
            {
                FileName = "basic.log",
                SourceService = "authservice",
                TargetService = "userservice",
            },
            CancellationToken.None);
        Assert.IsFalse(missingEdgeResponse.Status.Success);
        Assert.AreEqual(AgentErrorCode.InvalidArgument, missingEdgeResponse.Status.Code);
        Assert.HasCount(0, missingEdgeResponse.Entries);
    }
}
