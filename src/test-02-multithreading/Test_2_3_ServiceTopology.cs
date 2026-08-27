using LogAnalyzer;
using LogParser.Models;

namespace test_02_multithreading;

[TestClass]
public sealed class Test_2_3_ServiceTopology
{
    [TestMethod(DisplayName = "T5.1.a.d Build service topology")]
    [Timeout(1000, CooperativeCancellation = true)]
    public void TestBuildServiceTopology()
    {
        var userToAuth0 = CreateCall(1, "userservice-0", "authservice");
        var userToAuth1 = CreateCall(2, "userservice-1", "authservice");
        var gatewayToUser = CreateCall(3, "gateway-0", "userservice");
        LogEntry ignoredRequest = new RequestLogEntry(
            LineNo: 4,
            Timestamp: DateTimeOffset.UnixEpoch,
            PodName: "emailservice-0",
            Severity: LogSeverity.Info,
            RequestId: "request-4",
            Method: "GET",
            Path: "/health",
            StatusCode: 200);

        var topology = ServiceTopologyBuilder.Build(
            [userToAuth0, ignoredRequest, gatewayToUser, userToAuth1]);

        CollectionAssert.AreEqual(
            new[] { "authservice", "gateway", "userservice" },
            topology.Nodes.Select(node => node.Name).ToArray());
        Assert.HasCount(2, topology.Edges);

        var gatewayEdge = topology.Edges.Single(edge =>
            edge.SourceService == "gateway" && edge.TargetService == "userservice");
        Assert.AreEqual(1, gatewayEdge.CallCount);
        Assert.AreSame(gatewayToUser, gatewayEdge.Calls[0]);

        var userEdge = topology.Edges.Single(edge =>
            edge.SourceService == "userservice" && edge.TargetService == "authservice");
        Assert.AreEqual(2, userEdge.CallCount);
        CollectionAssert.AreEqual(
            new[] { userToAuth0, userToAuth1 },
            userEdge.Calls.ToArray());
    }

    [TestMethod(DisplayName = "T5.1.a.d Parse source service from pod name")]
    [Timeout(1000, CooperativeCancellation = true)]
    public void TestGetSourceService()
    {
        Assert.AreEqual("userservice", ServiceTopologyBuilder.GetSourceService("userservice-0"));
        Assert.AreEqual("gateway", ServiceTopologyBuilder.GetSourceService("gateway-12"));
        Assert.AreEqual("custom-service", ServiceTopologyBuilder.GetSourceService("custom-service"));
        Assert.AreEqual("api-v2", ServiceTopologyBuilder.GetSourceService("api-v2"));
    }

    private static CallLogEntry CreateCall(int lineNo, string podName, string targetService)
    {
        return new CallLogEntry(
            LineNo: lineNo,
            Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(lineNo),
            PodName: podName,
            Severity: LogSeverity.Info,
            RequestId: $"request-{lineNo}",
            TargetService: targetService,
            DurationMs: lineNo * 10);
    }
}
