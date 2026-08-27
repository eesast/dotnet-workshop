using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
using LogParser.Parser;

Console.WriteLine("=== Advanced Chapter 5: query & topology verification ===\n");

var entries = new LogFileParser().Parse(new StreamReader("dataset/basic.log")).ToList();
Console.WriteLine($"Parsed {entries.Count} log entries from dataset/basic.log");

// ---------- 1. 过滤：按事件类型 ----------
var callFilter = new LogFilter { EventType = LogEventTypeEnum.Call };
var calls = LogAnalysisQuery.FilterAndSort(entries, callFilter, null);
Console.WriteLine($"\n[filter event=Call]        -> {calls.Count} entries");

var errFilter = new LogFilter { Severity = LogSeverityEnum.Error };
var errors = LogAnalysisQuery.FilterAndSort(entries, errFilter, null);
Console.WriteLine($"[filter severity=Error]   -> {errors.Count} entries");

var gwFilter = new LogFilter { ServiceName = "gateway" };
var gateway = LogAnalysisQuery.FilterAndSort(entries, gwFilter, null);
Console.WriteLine($"[filter service=gateway]   -> {gateway.Count} entries");

// ---------- 2. 排序 ----------
var sortBySeverity = new LogSortOptions { SortBy = "Severity", IsDescending = true };
var sorted = LogAnalysisQuery.FilterAndSort(entries, null, sortBySeverity);
Console.WriteLine($"\n[sort severity desc] first 5 severities:");
foreach (var e in sorted.Take(5))
{
    Console.WriteLine($"   line={e.LineNo,-4} severity={e.Severity}");
}

// ---------- 3. 服务名提取 ----------
Console.WriteLine($"\n[GetServiceName] gateway-0 -> {LogAnalysisQuery.GetServiceName("gateway-0")}");
Console.WriteLine($"[GetServiceName] userservice-12 -> {LogAnalysisQuery.GetServiceName("userservice-12")}");

// ---------- 4. 拓扑推断 ----------
var topology = TopologyBuilder.Build(entries);
Console.WriteLine($"\n[topology] {topology.Nodes.Count} nodes:");
Console.WriteLine("   " + string.Join(", ", topology.Nodes));
Console.WriteLine($"[topology] {topology.Edges.Count} edges:");
foreach (var edge in topology.Edges)
{
    Console.WriteLine($"   {edge.SourceService} -> {edge.TargetService}  ({edge.CallCount} calls)");
}

// ---------- 5. 拓扑边 -> 日志（request_ids 过滤） ----------
var firstEdge = topology.Edges.FirstOrDefault();
if (firstEdge is not null)
{
    var edgeFilter = new LogFilter();
    edgeFilter.RequestIds.AddRange(firstEdge.RequestIds);
    var edgeLogs = LogAnalysisQuery.FilterAndSort(entries, edgeFilter, null);
    Console.WriteLine($"\n[edge {firstEdge.SourceService}->{firstEdge.TargetService}] " +
                      $"request_ids={firstEdge.RequestIds.Count}, matched logs={edgeLogs.Count}");
}

// ---------- 6. gRPC 端到端流程测试（需要先启动 Agent 并传入 admin token） ----------
if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
{
    Console.WriteLine("\n=== gRPC end-to-end flow test ===");
    var token = args[0];
    using var channel = GrpcChannel.ForAddress("http://localhost:5000");
    var client = new LogAnalyzerAgentService.LogAnalyzerAgentServiceClient(channel);
    var headers = new Metadata { { "x-agent-token", token } };

    // 无 token -> 应被拒绝
    try
    {
        await client.PingAsync(new Empty());
        Console.WriteLine("[FAIL] ping without token should be rejected");
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
    {
        Console.WriteLine("[PASS] ping without token -> Unauthenticated");
    }

    // 带 token -> Ping
    await client.PingAsync(new Empty(), headers);
    Console.WriteLine("[PASS] ping with token -> OK");

    // ChangeDirectory
    var dir = @"D:\THUsummerlearning\THU\src\dataset";
    var cdResp = await client.ChangeDirectoryAsync(new ChangeDirectoryRequest { DirectoryPath = dir }, headers);
    Console.WriteLine(cdResp.Status.Success
        ? $"[PASS] ChangeDirectory -> {cdResp.CurrentDirectory} ({cdResp.FileNames.Count} files)"
        : $"[FAIL] ChangeDirectory: {cdResp.Status.Message}");

    // GetLogFiles
    var filesResp = await client.GetLogFilesAsync(new Empty(), headers);
    Console.WriteLine($"[PASS] GetLogFiles -> {filesResp.FileNames.Count} files: {string.Join(", ", filesResp.FileNames)}");

    // AnalyzeAll
    var analyzeResp = await client.AnalyzeAllAsync(new AnalyzeAllRequest { DegreeOfParallelism = 4 }, headers);
    Console.WriteLine(analyzeResp.Status.Success ? "[PASS] AnalyzeAll -> OK" : $"[FAIL] AnalyzeAll: {analyzeResp.Status.Message}");

    // GetAnalysisResult
    int entryCount = 0;
    using (var call = client.GetAnalysisResult(new GetAnalysisResultRequest { FileName = "basic.log" }, headers))
    {
        await foreach (var r in call.ResponseStream.ReadAllAsync())
        {
            if (r.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry) entryCount++;
        }
    }
    Console.WriteLine($"[PASS] GetAnalysisResult basic.log -> {entryCount} entries");

    // Query（按服务名过滤）
    int qCount = 0;
    using (var qCall = client.QueryAnalysisResult(new QueryAnalysisResultRequest
    {
        FileName = "basic.log",
        Filter = new LogFilter { ServiceName = "gateway" }
    }, headers))
    {
        await foreach (var r in qCall.ResponseStream.ReadAllAsync())
        {
            if (r.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry) qCount++;
        }
    }
    Console.WriteLine($"[PASS] Query service=gateway -> {qCount} entries");

    // Topology
    var topoResp = await client.GetTopologyAsync(new GetTopologyRequest { FileName = "basic.log" }, headers);
    Console.WriteLine(topoResp.Status.Success
        ? $"[PASS] Topology -> {topoResp.Nodes.Count} nodes, {topoResp.Edges.Count} edges"
        : $"[FAIL] Topology: {topoResp.Status.Message}");

    // ListTokens（管理员接口）
    var list = await client.ListTokensAsync(new Empty(), headers);
    Console.WriteLine($"[PASS] ListTokens -> {list.Tokens.Count} token(s): " +
                      string.Join(", ", list.Tokens.Select(t => $"{t.Role}")));
}
else
{
    Console.WriteLine("\n=== gRPC end-to-end flow test skipped (pass admin token as arg) ===");
}

Console.WriteLine("\n=== Done ===");
