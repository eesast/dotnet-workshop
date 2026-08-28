# 05-advanced 实验报告

本章在前四章（解析、多线程、异步 gRPC、Avalonia 基础客户端）之上，把一个「能用的 CLI」打磨成一个「可用的 GUI 产品」。围绕「功能性」「美观性」「自由功能」三类要求，共实现六个功能：

| # | 任务编号 | 功能 | 类别 |
| :-: | :------ | :--- | :--- |
| 1 | T5.1.a.a | Parquet 列式读写 | 功能性 |
| 2 | T5.1.a.b | Token 鉴权 + 多用户隔离 | 功能性 |
| 3 | T5.1.a.c | 多条件查询（Query） | 功能性 |
| 4 | T5.1.a.d | 调用拓扑推断 | 功能性 |
| 5 | T5.1.b.a | 结果表格 + Severity 高亮 | 美观性 |
| 6 | T5.2 | Request ID 链路追踪瀑布图 | 自由功能 |

## 一、程序编译与运行

GUI 客户端采用「Agent（gRPC 服务端）+ 桌面客户端」双进程架构，需要分别启动。

**1. 启动 Agent（服务端）**

```bash
# 终端 1：编译并启动 Agent，监听 http://localhost:5000
dotnet run --project src/LogAnalyzerAgent
```

Agent 启动时会在控制台输出一个**管理员 token**（这是登录的凭据，务必复制保存），形如：

```
info: Bootstrap[0]
      Admin token generated. Use it to log in the client (File -> Connect...) and manage other tokens: <32 位 token>
```

token 由 `RandomNumberGenerator` 生成 24 字节随机数再 base64url 编码（约 32 字符），高熵且 URL 安全。

**2. 启动桌面客户端**

```bash
# 终端 2：编译并启动桌面客户端
dotnet run --project src/LogAnalyzerClient/LogAnalyzerClient.Desktop
```

**3. 连接 Agent**

客户端启动后界面为空，点击菜单 `File → Connect...`，在弹出的 `Connect` 对话框中：
- **Address**：填 `http://localhost:5000`（占位符已给出示例）。
- **Token**：粘贴上一步控制台打印的管理员 token。

点击 `Connect`，客户端先发一个 `Ping` 校验 token；成功后底部状态栏显示 `Connected`、`[Admin]`、Agent 地址与当前目录，左侧文件列表刷新。若 token 错误会提示 `Authentication failed: the token was rejected by the Agent.`

> 连接成功后，所有后续操作（列文件、分析、查询、导出……）都会自动在 gRPC 头里携带该 token，无需重复输入。

## 二、实现的功能

### 1.（T5.1.a.a，功能性）Parquet 列式读写

#### 1.1 功能概述

把日志分析结果持久化为 **Parquet 列式存储**文件，并支持把导出的 `.parquet` 当作普通日志文件读回、再次分析，形成「`.log → 分析 → 导出 Parquet → 再分析」的闭环。Parquet 的列式 + 游程编码对这类「三种事件类型拼成的稀疏宽表」特别友好——大量 `null` 列几乎不占空间。

#### 1.2 实现要点

- **依赖**：在 `LogParser` 项目引入 `Parquet.Net 6.0.3`（`LogParser.csproj:11`），上层通过 `LogParser` 的包装 API 间接使用，避免污染客户端依赖。
- **Schema 设计**：用 POCO `ParquetLogRow`（`LogParser/Parquet/LogParquetSchema.cs`）描述一张 13 列的「宽而稀疏」表——公共列 `LineNo/Timestamp/PodName/Severity/EventType` 全填，类型专属列（Call 的 `TargetService/DurationMs`、Request 的 `Method/Path/StatusCode`、Internal 的 `ExceptionName/ExceptionMessage`）按行类型填、其余留 `null`。`Timestamp` 用 ISO-8601 round-trip（`"O"`）格式存，枚举统一小写存字符串。
- **写**：`ParquetLogWriter.WriteAsync`（`LogParser/Parquet/ParquetLogWriter.cs`）把每条 `LogEntry` 经 `ToRow` 映射成 `ParquetLogRow`，再用高层 API `ParquetSerializer.SerializeAsync` 一次落盘，返回行数。
- **读**：`ParquetLogReader.ReadAsync`（`LogParser/Parquet/ParquetLogReader.cs`）用 `ParquetSerializer.DeserializeAsync<ParquetLogRow>` 读回，再按 `EventType` 重建出 `CallLogEntry/RequestLogEntry/InternalLogEntry` 三种具体记录；遇到未知 `event_type`/`severity` 抛 `FormatException`，会被 `WorkerMain` 捕获、标记分析失败。
- **新增 RPC**（`log_analyzer.proto`）：
  ```proto
  rpc ExportAnalysisResult(ExportAnalysisResultRequest) returns (ExportAnalysisResultResponse);
  // request: file_name / output_path / overwrite
  // response: status / written_path / entry_count
  ```
- **Agent 端**（`AgentSession.ExportAnalysisResultAsync`）：校验文件名、输出路径非空 → 文件存在且已分析成功 → 解析路径（相对路径基于 Agent 当前目录，缺 `.parquet` 后缀自动补齐）→ `overwrite=false` 且文件已存在则报错 → 创建父目录 → 调 `ParquetLogWriter.WriteAsync` → 回填 `written_path` 与 `entry_count`。
- **文件枚举一视同仁**：`LogFileAnalyzer.EnumerateLogFiles` 同时收集 `*.log` 和 `*.parquet`；分析时 `WorkerMain` 按扩展名分发（`.parquet` 走 `ParquetLogReader`，其余走文本解析器），所以导出的 `.parquet` 会直接出现在文件列表里、可被再次分析。

#### 1.3 操作方法

1. 连接 Agent 后，在 `Directory Path` 输入框填入数据目录（如 `dataset`），点 `Change Directory`，再点 `Refresh`，左侧列表出现日志文件。
2. 选中一个文件（左键单击高亮），点右侧 `Analyze → Selected`（或右键 `Analyze File`）完成分析。
3. **右键**该已分析文件 → `Export to Parquet`，弹出 `Export to Parquet` 对话框：
   - 上方 `Source` 显示源文件名；
   - `Output Path` 填绝对路径，省略 `.parquet` 后缀会自动补齐；
   - 勾选 `Overwrite if the file already exists` 可覆盖同名文件。
4. 点 `Export`，成功后弹出 `Exported {N} entries to: {written_path}`。
5. 点工具栏 `Refresh`，新生成的 `.parquet` 出现在文件列表中。
6. 选中该 `.parquet` → `Analyze → Selected` → `View Analysis Results`，结果应与原 `.log` 完全一致，验证读写闭环。
7. （Browser 端）WASM 无自定义窗口，退化为单行 `prompt`：输入路径，行首加 `!` 表示覆盖。

### 2.（T5.1.a.b，功能性）Token 鉴权 + 多用户隔离

#### 2.1 功能概述

为 Agent 加上 **Bearer Token 应用层鉴权**：每个 RPC 都必须携带合法 token，否则返回 `Unauthenticated`；管理类 RPC 还要求 `Admin` 角色。同时实现**多用户隔离**——每个 token 拥有独立的 `LogFileAnalyzer`，目录与分析结果互不可见。Admin 可在 GUI 里增删 token、改权限，且系统拒绝删除/降级最后一个 admin 以防锁死。

#### 2.2 实现要点

- **token 生成**：`TokenStore.GenerateToken`（`Auth/TokenStore.cs`）用 `RandomNumberGenerator.Fill` 取 24 字节密码学随机数，base64url 编码。`TokenInfo` 含不可变的 `Token` 与可变的 `Role/Note`，全部读写经同一把锁。
- **启动引导**：`Program.cs` 启动时调 `tokenStore.CreateAdminToken()`，并把该 admin token **完整**打印一次（其余日志里都用 `Mask()` 只留前 6 位）。
- **服务端鉴权**：`AgentService.Authorize`（`Services/AgentService.cs`）从 `authorization: Bearer <token>` 头取 token，查 `TokenStore.TryGet`，失败抛 `RpcException(Unauthenticated)`；`RequireAdmin` 在此基础上校验 `Admin` 角色，否则 `PermissionDenied`。17 个 RPC 全部先 `Authorize`，其中 4 个管理 RPC 额外 `RequireAdmin`。
- **多用户隔离**：`SessionManager`（`Auth/SessionManager.cs`）是一个 `ConcurrentDictionary<string, LogFileAnalyzer>`，以 token 为键、懒加载每个用户独有的 `LogFileAnalyzer`。`AgentSession` 里每个业务方法开头都是 `var analyzer = _sessions.GetOrCreate(caller.Token);`，天然隔离目录与结果。
- **客户端注入**：`TokenInterceptor`（`Services/TokenInterceptor.cs`）实现 gRPC `Interceptor`，覆盖 unary/server-stream/client-stream/duplex 五种入口，在 `WithToken` 里给每次调用注入 `authorization` 头（并幂等地剔除旧头防重复）。Desktop 与 Browser 的 `ClientFactory` 都用 `channel.Intercept(new TokenInterceptor(token))` 包裹通道。
- **防锁死**：`TokenStore.TryDelete`/`TrySetRole` 在只剩一个 admin 时拒绝删除/降级。
- **相关 proto**：
  ```proto
  rpc CreateToken(CreateTokenRequest) returns (CreateTokenResponse);   // 需 Admin
  rpc DeleteToken(DeleteTokenRequest) returns (OperationStatusMessage); // 需 Admin
  rpc ListTokens(Empty) returns (ListTokensResponse);                  // 需 Admin
  rpc SetTokenRole(SetTokenRoleRequest) returns (OperationStatusMessage); // 需 Admin
  ```
  `ListTokensResponse.caller_token` 让客户端高亮「自己」那一行。

#### 2.3 操作方法

1. 用 admin token 连接（见「一」）。连接成功后 `DetectAdminAsync` 自动探测权限，菜单出现 `File → Manage Tokens...`（非 admin 该项隐藏）。
2. 点 `File → Manage Tokens...` 打开 `Token Management` 窗口，列出全部 token（自己的行带浅蓝底、`YOU` 徽标）。
3. **创建 token**：`Role` 选 `Normal`/`Admin`，`Note` 填备注（如 `for-alice`），点 `Create`。新 token **仅显示一次**在状态栏，需立即复制发给用户。
4. **改权限**：点对应该行的 `Promote`/`Demote` 按钮切换 Normal ↔ Admin。
5. **删除**：点该行 `Delete`（最后一个 admin 删不掉，会报错）。
6. **验证隔离**：另开一个客户端实例，用不同 token 连接，`Change Directory` 到不同目录、分析不同文件；两个窗口的文件列表与结果互不影响。
7. **验证鉴权**：用 `File → Connect...` 填一个随便编的 token，连接会被拒，提示认证失败。

### 3.（T5.1.a.c，功能性）多条件查询（Query）

#### 3.1 功能概述

对已分析的结果按 **事件类型 / 严重等级 / 服务 / Request ID / 时间范围** 五个维度做组合过滤，结果以表格呈现并支持重排。全部条件「留空即不过滤该维度」，组合关系为 AND。

#### 3.2 实现要点

- **服务端流式 RPC**（复用 `GetAnalysisResultResponse` 载体）：
  ```proto
  rpc QueryAnalysisResult(QueryAnalysisResultRequest) returns (stream GetAnalysisResultResponse);
  // request: file_name / repeated event_types / repeated severities /
  //          request_id_pattern / service_pattern / optional start_time / optional end_time
  ```
  pattern 用子串、大小写不敏感；时间两端均**闭区间**、按 UTC 解释。Internal 日志没有 request-id，设了 request-id 过滤时自动排除。
- **客户端数据模型**：`QueryFilter`（`Models/QueryFilter.cs`）用 `HashSet<枚举>` 表达类型/等级集合、两个 `string` pattern、两个 `DateTimeOffset?` 时间界。`ToRequest` 序列化为 gRPC 请求。
- **桌面对话框** `QueryDialog`：用 CheckBox 组表达「类型」「等级」多选（全不勾=任意），TextBox 表达服务/Request ID/起止时间。校验时间可解析且 `start <= end`，否则行内红字报错、窗口不关。
- **Browser 退化**：`QueryFilterParser`（`Helpers/QueryFilterParser.cs`）把一行文本解析成 `QueryFilter`，语法如 `type=Call,Request severity=Warning,Error service=gateway from=2026-06-05 to=2026-06-05T17:00:00Z`；未知键静默忽略、保证鲁棒。
- **结果与排序解耦**：流式结果先进 `_loadedEntries`（原始顺序），再套当前排序键生成 `ResultEntries`。`SortKeys` 支持 12 个键（LineNo/Timestamp/Severity/EventType/PodName/RequestId/TargetService/Method/Path/StatusCode/DurationMs/ExceptionName），缺失字段按类型默认值兜底（如非 Call 行的 `Method` 为 `""`）。查询后 `IsResultFiltered=true`，`Show All` 按钮出现，点它调 `GetAnalysisResult` 还原全集。

#### 3.3 操作方法

1. 选中一个已分析成功的文件（左键高亮，使其成为当前结果来源）。
2. 点结果面板右上角 `Query...` 按钮，打开 `Query Log Entries` 对话框。
3. 按需勾选/填写（全部留空 = 等价于 Show All）：
   - **Event Type**：勾 `Call`/`Request`/`Internal` 中的一个或多个；
   - **Severity**：勾 `Info`/`Warning`/`Error`；
   - **Service**：填服务名（如 `gateway`，会匹配 `gateway-0`、`gateway-1`）；
   - **Request ID contains**：填 request-id 子串；
   - **Start time / End time**：填日期（`2026-06-05`）或完整 ISO 8601（`2026-06-05T17:00:00Z`），闭区间、按 UTC。
4. 点 `Query`。表格刷新为命中行，状态栏显示 `Filtered: showing N entr(y/ies).`。
5. 用 `Sort by` 下拉 + `Descending` 复选框对查询结果重排（实时生效）。
6. 点 `Show All` 退出过滤、恢复全集。

### 4.（T5.1.a.d，功能性）调用拓扑推断

#### 4.1 功能概述

从已分析文件的 **Call 类型日志**里推断出服务间的有向调用图（节点=服务，边=调用关系，边权=调用次数），在独立窗口里画成节点-箭头图；点某条边即把该调用关系对应的全部 Call 日志加载进结果面板。这对应可观测性「拓扑」支柱。

#### 4.2 实现要点

- **两个 RPC**（`log_analyzer.proto`）：
  ```proto
  rpc GetCallTopology(GetCallTopologyRequest) returns (GetCallTopologyResponse); // 一元，返回整图
  rpc GetEdgeCallLogs(GetEdgeCallLogsRequest) returns (stream GetAnalysisResultResponse); // 流式，返回该边 Call 日志
  ```
- **图推断**（`AgentSession.GetCallTopology`）：遍历 `result.Entries`，只取 `CallLogEntry`；用 `ExtractService`（正则 `-\d+$` 去掉 pod 副本后缀，如 `gateway-0 → gateway`）把 `PodName` 归一为源服务、`TargetService` 为目标服务；`SortedSet<string>` 收集节点（确定性顺序保证客户端布局可复现），`Dictionary<(src,tgt),int>` 累计边权。`Top` 头部信息含服务数、边数。
- **点边取日志**：`GetEdgeCallLogs` 先推一个 header（让客户端读文件状态），再把源、目标都匹配的 Call 日志逐条 stream 回去。
- **Avalonia 绘图**（`TopologyWindow`）：固定 720×460 `Canvas` 套 `Viewbox` 等比缩放。节点按圆周等分布置（胶囊形 `Border`，蓝色填充）；普通边为灰色 `Line` + 三角箭头，并把两端各回缩 36px 让箭头落在节点边框上；自环画成节点上方的小椭圆。关键技巧：在细线之上叠一条 `StrokeThickness=16`、alpha=1 的近透明 `Line` 作**点击热区**，让细边也能轻松点中。
- **交互回流**：点边 → `Window.Close(edge)` → `ShowTopologyAsync` 拿到该边 → 发 `GetEdgeCallLogs` → `ConsumeResultStreamAsync` 把 Call 日志灌进结果表格，状态栏显示 `Edge gateway -> userservice (12 calls).`。

#### 4.3 操作方法

1. 选中并分析一个含 Call 日志的文件（如 `basic.log`）。
2. **右键**该文件 → `Show Call Topology`，弹出 `Call Topology` 窗口，顶部显示 `Call topology of '<file>' - N service(s), M edge(s)`。
3. 鼠标悬停节点看服务名 Tooltip；点任意**箭头**（或自环节点）选中该调用关系，窗口自动关闭。
4. 结果表格被替换为该边的全部 Call 日志，状态栏显示边信息；可继续 `Query...` 或 `Sort by` 二次筛选。
5. 若文件没有 Call 日志，会提示无法推断拓扑；点 `Close` 或不选边直接关窗则什么都不做。

### 5.（T5.1.b.a，美观性）结果表格 + Severity 高亮

#### 5.1 功能概述

把分析结果从「一行字符串的 ListBox」升级为 **DataGrid 表格**：每条日志一行，按 `#`、`Time`、`Severity`、`Service`、`Type`、`Detail` 分列；不同事件类型的字段摘要统一收进 Detail 列。Severity 列用圆角胶囊按等级着色——Info 蓝、Warning 橙、Error 红，一眼定位错误。

#### 5.2 实现要点

- **强类型行模型** `LogEntryRowVm`（`Models/LogEntryRowVm.cs`）：由 `LogEntry` 直接构造。`Time` 格式化为 `HH:mm:ss.fff`；`Service` 用同样的 `-\d+$` 正则去掉 pod 后缀；`Detail` 由 `BuildDetail` 按类型生成摘要：
  - Call → `-> {target} ({dur} ms)`
  - Request → `{method} {path} -> {code}`
  - Internal → `{ExceptionName}: {ExceptionMessage}`
- **着色**：`SeverityToBrushConverter`（`Converters/SeverityToBrushConverter.cs`，`IValueConverter`）把 `LogSeverity` 枚举映射成预分配的 `SolidColorBrush`（`#2B6CB0`/`#DD6B20`/`#C53030`，static 只分配一次）。XAML 里 Severity 列用 `DataGridTemplateColumn`，胶囊 `Border` 的 `Background="{Binding Severity, Converter={StaticResource SeverityToBrush}}"`，比给 `Classes` 绑定动态值更可靠。
- **状态反馈**：未分析 / 分析失败 / 命中数为 0 等状态，用表格上方加粗的状态栏文字显示（`Showing all N entries.` / `Filtered: showing N entries.` / `No entries match the query.` 等）。
- **排序与展示解耦**：`_loadedEntries: List<LogEntry>` 保留原始顺序，展示用的 `ResultEntries` 由 `ApplySort` 重排生成；切换 `Sort by` / `Descending` 实时重排。

#### 5.3 操作方法

1. 分析某文件后右键 `View Analysis Results`（或点过滤后的 `Show All`），结果以六列表格呈现，Severity 列自动着色。
2. 建议用 `basic.log` 验证 Info/Warning/Error 三色胶囊；用混合日志验证三种 Detail 摘要格式。
3. 用 `Sort by` 下拉选排序键、勾 `Descending` 调整顺序；列宽自适应，Detail 列占剩余宽度。

### 6.（T5.2，自由功能）Request ID 链路追踪瀑布图

#### 6.1 功能概述

云服务可观测性三大支柱是「指标 / 拓扑 / 追踪」。第 4 节展示了「拓扑」，本功能展示**追踪**——即分布式追踪（Distributed Tracing）的瀑布图：刻画**某一次**请求依次经过了哪些服务、每跳多久、在哪一段出错。在结果表格里选中一条 Call/Request 日志，客户端按其 `request-id` 向 Agent 拉取整条调用链，在弹窗里画成竖向瀑布：每个横条是一次服务调用，从左到右按时间排列，条长代表耗时，出错段标红。

#### 6.2 实现要点

- **新增 RPC**（`log_analyzer.proto`），**响应复用 `stream GetAnalysisResultResponse`**（与 `GetAnalysisResult`/`GetEdgeCallLogs` 同载体），省去新建 message，客户端复用 `GrpcTypeConverter` 与 header 状态判断：
  ```proto
  rpc GetTrace(GetTraceRequest) returns (stream GetAnalysisResultResponse);
  // request: file_name / request_id
  ```
- **Agent 端**（`AgentSession.GetTrace`）：按 `request-id` 精确匹配（Call/Request 取其 `RequestId`，Internal 无 id 自动排除），**按时间升序**返回，便于客户端直接落笔画瀑布。
- **客户端** `ConsumeTraceStreamAsync`：只取其中的 Call 日志组装成 `TraceSpan`（源服务、目标服务、起始时刻、耗时、是否出错——`Severity==Error` 即红色），不污染主结果表格。
- **瀑布图**（`TraceWindow`，Canvas + Viewbox，与拓扑窗口同套路）：先求整条链路的 `[minStart, maxEnd]` 时间跨度并归一化到画布宽度，每个 span 的左偏移由其起始时刻算出、宽度由耗时按比例算出（设 `MinBarWidth` 防极短条看不见），失败段用红色填充/边框、正常段蓝色；画布高度随 span 数动态增高。两端标注 `+0 ms` 与 `+{total} ms total`。
- **数据坑（关键）**：原 `gen.py` 给每条日志独立生成 `uuid`，导致每个 `request-id` 只出现一次，画不出多跳链路。改造 `gen.py` 新增 `make_trace`：模拟一次请求沿调用拓扑随机游走多跳（最多 3 跳）、共享同一 `request-id`、时间递增，约 1/4 的链路在末跳失败（用于演示红色错误段）。生成的 `dataset/trace.log` 经统计确认存在出现 ≥2 次的 `request-id`（最多 5 次，即 2 跳链路）。

#### 6.3 操作方法

1. `Change Directory` 到 `dataset`，分析 `trace.log`（已含多跳链路）。
2. `View Analysis Results` 查看结果表格。
3. 在表格里**左键**选中一条 Call 行（使其成为 `SelectedResultEntry`），再**右键** → `View Trace for this Request`。
4. 弹出 `Request Trace Waterfall` 窗口，顶部显示 `Trace of request <id> - N span(s) - '<file>'`，下方为瀑布图：每条蓝色横条一跳、末跳失败时为红色，鼠标可读 `源 -> 目标 (耗时 ms)` 的标签。
5. 选中 Internal 类型日志再右键追踪，会提示「无 Request ID」；选中的行没有先左键点过，会提示先左键选中再右键。
