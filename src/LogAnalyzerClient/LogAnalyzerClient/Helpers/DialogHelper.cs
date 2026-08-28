using Avalonia.Controls;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerClient.Models;
using LogAnalyzerRpc.Protos;
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LogAnalyzerClient.Helpers
{
    using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

    internal interface IDialogHelper
    {
        Task<ConnectResult?> ShowConnectDialogAsync();
        Task ShowMessageDialogAsync(string title, string message);
        Task<QueryFilter?> ShowQueryDialogAsync();
        Task<TopologyEdge?> ShowTopologyDialogAsync(TopologyGraph graph);
        Task ShowTraceDialogAsync(TraceWaterfall waterfall);
        Task<ExportOptions?> ShowExportDialogAsync(string sourceFileName);
        Task ShowTokenManagerDialogAsync(LogAnalyzerAgentServiceClient client, string callerToken);
    }

    internal class NullDialogHelper : IDialogHelper
    {
        public Task<ConnectResult?> ShowConnectDialogAsync()
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task ShowMessageDialogAsync(string title, string message)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task<QueryFilter?> ShowQueryDialogAsync()
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task<TopologyEdge?> ShowTopologyDialogAsync(TopologyGraph graph)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task ShowTraceDialogAsync(TraceWaterfall waterfall)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task<ExportOptions?> ShowExportDialogAsync(string sourceFileName)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task ShowTokenManagerDialogAsync(LogAnalyzerAgentServiceClient client, string callerToken)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }
    }

    internal class DesktopDialogHelper : IDialogHelper
    {
        private readonly Window _owner;

        public DesktopDialogHelper(Window owner)
        {
            _owner = owner;
        }

        public async Task<ConnectResult?> ShowConnectDialogAsync()
        {
            // 不预填上次的地址/token，避免泄露历史输入。
            var dialog = new ConnectDialog();
            return await dialog.ShowDialog<ConnectResult?>(_owner);
        }

        public async Task ShowMessageDialogAsync(string title, string message)
        {
            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(_owner);
        }

        public async Task<QueryFilter?> ShowQueryDialogAsync()
        {
            var dialog = new QueryDialog();
            return await dialog.ShowDialog<QueryFilter?>(_owner);
        }

        public async Task<TopologyEdge?> ShowTopologyDialogAsync(TopologyGraph graph)
        {
            var dialog = new TopologyWindow(graph);
            return await dialog.ShowDialog<TopologyEdge?>(_owner);
        }

        public async Task ShowTraceDialogAsync(TraceWaterfall waterfall)
        {
            var window = new TraceWindow(waterfall);
            await window.ShowDialog(_owner);
        }

        public async Task<ExportOptions?> ShowExportDialogAsync(string sourceFileName)
        {
            var dialog = new ExportDialog(sourceFileName);
            return await dialog.ShowDialog<ExportOptions?>(_owner);
        }

        public async Task ShowTokenManagerDialogAsync(LogAnalyzerAgentServiceClient client, string callerToken)
        {
            var window = new TokenManagerWindow(client, callerToken);
            await window.ShowDialog(_owner);
        }
    }

    [SupportedOSPlatform("browser")]
    internal class BrowserDialogHelper : IDialogHelper
    {
        public async Task<ConnectResult?> ShowConnectDialogAsync()
        {
            // 浏览器端用两次 prompt 分别收集地址与 token；不预填历史值。
            string address = await Task.Run(() =>
                BrowserInterop.Prompt("Agent address:", "") ?? "");
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }
            string token = await Task.Run(() =>
                BrowserInterop.Prompt("Token (issued by the Agent):", "") ?? "");
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }
            return new ConnectResult(address.Trim(), token.Trim());
        }

        public async Task ShowMessageDialogAsync(string title, string message)
        {
            await Task.Run(() =>
            {
                BrowserInterop.Alert($"[{title}]\n\n{message}");
            });
        }

        public async Task<QueryFilter?> ShowQueryDialogAsync()
        {
            // 浏览器端没有自定义窗口能力，用一个 prompt 让用户以紧凑语法输入查询条件。
            // 语法示例：type=Call,Request severity=Warning service=gateway request=abc from=2026-06-05 to=2026-06-06
            // 留空或取消表示查询全部。
            var text = await Task.Run(() =>
            {
                return BrowserInterop.Prompt(
                    "Query conditions (blank = match all). Syntax:\n" +
                    "type=Call,Request severity=Warning,Error service=gateway request=<id-substring> from=<time> to=<time>",
                    "");
            });
            if (string.IsNullOrWhiteSpace(text))
            {
                return new QueryFilter();
            }
            return QueryFilterParser.Parse(text);
        }

        public async Task ShowTraceDialogAsync(TraceWaterfall waterfall)
        {
            // 浏览器端无法绘制瀑布图，退化成按时间顺序列出每一段调用。
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Trace of request {waterfall.RequestId} ('{waterfall.FileName}'):");
            if (waterfall.Spans.Count == 0)
            {
                sb.AppendLine("(no call spans)");
            }
            foreach (var s in waterfall.Spans)
            {
                sb.AppendLine($"- {s.Label}{(s.IsError ? "  [ERROR]" : "")}");
            }
            await Task.Run(() => BrowserInterop.Alert(sb.ToString()));
        }

        public async Task<TopologyEdge?> ShowTopologyDialogAsync(TopologyGraph graph)
        {
            // 浏览器端无法绘制自定义拓扑图，退化成「列出所有边并按编号选择」的形式。
            if (graph.Edges.Count == 0)
            {
                await Task.Run(() => BrowserInterop.Alert("No call edges in this file."));
                return null;
            }
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("Edges (enter its number to view its Call logs, blank to cancel):");
            for (int i = 0; i < graph.Edges.Count; i++)
            {
                lines.AppendLine($"{i + 1}. {graph.Edges[i].Display}");
            }
            string? input = await Task.Run(() => BrowserInterop.Prompt(lines.ToString(), ""));
            if (string.IsNullOrWhiteSpace(input) ||
                !int.TryParse(input.Trim(), out int idx) ||
                idx < 1 || idx > graph.Edges.Count)
            {
                return null;
            }
            return graph.Edges[idx - 1];
        }

        public async Task<ExportOptions?> ShowExportDialogAsync(string sourceFileName)
        {
            // 浏览器端用 prompt 收集输出路径；前缀 "!" 表示覆盖已存在文件。
            var text = await Task.Run(() =>
            {
                return BrowserInterop.Prompt(
                    $"Export '{sourceFileName}' to a Parquet file.\n" +
                    "Enter output path (prefix with '!' to overwrite). Relative paths are resolved against the Agent log dir.",
                    sourceFileName + ".parquet");
            });
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            string t = text.Trim();
            bool overwrite = t.StartsWith("!");
            if (overwrite)
            {
                t = t[1..].Trim();
            }
            return new ExportOptions(t, overwrite);
        }

        public async Task ShowTokenManagerDialogAsync(LogAnalyzerAgentServiceClient client, string callerToken)
        {
            // 浏览器端无自定义窗口，退化成基于 prompt/alert 的最小可用管理流程。
            ListTokensResponse list;
            try
            {
                list = await client.ListTokensAsync(new Empty());
            }
            catch (Grpc.Core.RpcException ex)
            {
                await Task.Run(() => BrowserInterop.Alert(
                    ex.StatusCode == Grpc.Core.StatusCode.PermissionDenied
                        ? "Admin privilege required to manage tokens."
                        : $"Failed to list tokens: {ex.Status.Detail}"));
                return;
            }

            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"Tokens ({list.Tokens.Count}):");
            foreach (var t in list.Tokens)
            {
                string self = t.Token == list.CallerToken ? "  <- you" : "";
                lines.AppendLine($"- [{t.Role}] {t.Token}  ({t.Note}){self}");
            }
            await Task.Run(() => BrowserInterop.Alert(lines.ToString()));

            // 创建新 token：输入 "normal"/"admin" 加可选备注。
            string? create = await Task.Run(() =>
                BrowserInterop.Prompt("Create a new token? Enter role + note, e.g. 'normal for-alice'. Blank to skip.", ""));
            if (!string.IsNullOrWhiteSpace(create))
            {
                var parts = create.Trim().Split(new[] { ' ' }, 2);
                var role = parts[0].Equals("admin", StringComparison.OrdinalIgnoreCase)
                    ? TokenRoleEnum.TokenAdmin : TokenRoleEnum.TokenNormal;
                string note = parts.Length > 1 ? parts[1] : "";
                try
                {
                    var created = await client.CreateTokenAsync(new CreateTokenRequest { Role = role, Note = note });
                    await Task.Run(() => BrowserInterop.Alert(
                        $"Created [{created.Token.Role}] token:\n{created.Token.Token}\nCopy and hand it to the user."));
                }
                catch (Grpc.Core.RpcException ex)
                {
                    await Task.Run(() => BrowserInterop.Alert($"Create failed: {ex.Status.Detail}"));
                }
            }

            // 删除 token：粘贴 token。
            string? del = await Task.Run(() => BrowserInterop.Prompt("Delete a token? Paste it here. Blank to skip.", ""));
            if (!string.IsNullOrWhiteSpace(del))
            {
                try
                {
                    var st = await client.DeleteTokenAsync(new DeleteTokenRequest { Token = del.Trim() });
                    await Task.Run(() => BrowserInterop.Alert(st.Success ? "Deleted." : $"Failed: {st.Message}"));
                }
                catch (Grpc.Core.RpcException ex)
                {
                    await Task.Run(() => BrowserInterop.Alert($"Delete failed: {ex.Status.Detail}"));
                }
            }
        }
    }

    [SupportedOSPlatform("browser")]
    internal static partial class BrowserInterop
    {
        [JSImport("globalThis.alert")]
        public static partial void Alert(string message);

        [JSImport("globalThis.prompt")]
        public static partial string? Prompt(string message, string defaultValue);
    }
}
