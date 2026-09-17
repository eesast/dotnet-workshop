using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;

namespace test_03_async_grpc;

[TestClass]
public sealed class Test_5_1_TopologyLayout
{
    [TestMethod(DisplayName = "T5.1.a.d Arrange an acyclic topology in layers")]
    [Timeout(1000, CooperativeCancellation = true)]
    public void TestArrangeAcyclicTopology()
    {
        var edges = new[]
        {
            new TopologyEdgeItem("gateway", "userservice", 3),
            new TopologyEdgeItem("userservice", "authservice", 2),
            new TopologyEdgeItem("userservice", "emailservice", 1),
        };

        var nodes = TopologyLayout.Arrange(
            ["gateway", "userservice", "authservice", "emailservice"],
            edges);

        Assert.AreEqual(TopologyLayout.Margin, Find(nodes, "gateway").X);
        Assert.AreEqual(
            TopologyLayout.Margin + TopologyLayout.HorizontalSpacing,
            Find(nodes, "userservice").X);
        Assert.AreEqual(
            TopologyLayout.Margin + TopologyLayout.HorizontalSpacing * 2,
            Find(nodes, "authservice").X);
        Assert.AreEqual(Find(nodes, "authservice").X, Find(nodes, "emailservice").X);
        Assert.AreNotEqual(Find(nodes, "authservice").Y, Find(nodes, "emailservice").Y);
    }

    [TestMethod(DisplayName = "T5.1.a.d Arrange cyclic and incomplete topology data")]
    [Timeout(1000, CooperativeCancellation = true)]
    public void TestArrangeCyclicTopology()
    {
        var nodes = TopologyLayout.Arrange(
            [],
            [
                new TopologyEdgeItem("service-b", "service-a", 1),
                new TopologyEdgeItem("service-a", "service-b", 1),
                new TopologyEdgeItem("service-b", "service-b", 1),
            ]);

        Assert.HasCount(2, nodes);
        Assert.AreEqual(TopologyLayout.Margin, Find(nodes, "service-a").X);
        Assert.AreEqual(
            TopologyLayout.Margin + TopologyLayout.HorizontalSpacing,
            Find(nodes, "service-b").X);
    }

    private static TopologyNodeItem Find(
        IReadOnlyList<TopologyNodeItem> nodes,
        string name)
    {
        return nodes.Single(node => node.Name == name);
    }
}
