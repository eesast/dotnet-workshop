# 03-async-grpc 实验报告

## 实现的功能介绍

本节完成了 Agent（gRPC 服务端）与 RemoteCli（gRPC 客户端）两部分。

### T3.1 gRPC Agent（`src/LogAnalyzerRpc`、`src/LogAnalyzerAgent`）

+ `GrpcLogEntryVisitor`：实现访问者模式，将 `RequestLogEntry`、`InternalLogEntry` 分别转换为 Protobuf 的 `RequestLogEntryMessage` / `InternalLogEntryMessage`，填入 `LogEntryMessage` 的 oneof 字段。
+ `GrpcTypeConverter`：补全 `LogSeverity` ↔ `LogSeverityEnum`、`LogEventType` ↔ `LogEventTypeEnum` 的双向转换，以及 `ConvertFromGrpc(LogEntryMessage)` 对 Request / Internal 两种消息的反转换（重新构造 `RequestLogEntry` / `InternalLogEntry`，其中 Timestamp 用 `ToDateTimeOffset()` 还原）。
+ `AgentSession`：实现四个处理逻辑：
  - `ChangeDirectory`：空路径返回 `INVALID_ARGUMENT`；目录不存在返回 `DIRECTORY_NOT_FOUND`；成功后返回当前目录与全部 `.log` 文件名。
  - `AnalyzeAll` / `AnalyzeFiles`：未设置目录返回 `INVALID_OPERATION`；`AnalyzeFiles` 对空文件列表返回 `INVALID_ARGUMENT`，对包含不存在文件名的输入（`ArgumentException`）返回 `FILE_NOT_FOUND`，对其他非法操作（`InvalidOperationException`）返回 `INVALID_OPERATION`，其余异常统一转为 `INTERNAL_ERROR`，保证 Agent 永不因用户非法输入而崩溃。
  - `GetAnalysisResult`：文件不存在返回单条 `FILE_NOT_FOUND` 响应；否则返回「header（`AnalysisResultHeaderMessage`，含状态 / 错误信息 / worker id）+ 每条日志一个 `LogEntryMessage`」的响应列表。
+ `AgentService`：作为 gRPC 服务入口，将每个 RPC 转交给 `AgentSession`；`GetAnalysisResult` 使用服务端流式返回，逐个 `WriteAsync`。

### T3.2 RemoteCli（`src/RemoteCli/Program.cs`）

参照上一节 `LocalCli` 改造为全异步 gRPC 调用（全部使用 `...Async` 方法）：

+ `ShowLogFiles`：调用 `GetLogFilesAsync` 列出文件。
+ `ReadDegreeOfParallelism`：读取并发度（`0` 表示自动），非法输入循环重试。
+ `ReadFileNames`：读取逗号分隔的文件名列表，非法输入循环重试。
+ `AnalyzeFiles` / `AnalyzeAll`：先读并发度（及文件名），再异步调用分析接口，捕获 RPC 异常与业务错误并提示。
+ `GetAnalysisResult`：用 `client.GetAnalysisResult(request)` 发起服务端流式调用，通过 `ResponseStream.ReadAllAsync()` 逐条读取；打印 header 后用 `KeyValueVisitor.Dump` 输出每条日志的键值对。

## 功能记录（终端输出）

以下为 `src/` 目录下先启动 `LogAnalyzerAgent`（监听 `http://localhost:7777`），再运行 `RemoteCli` 的完整功能流程记录（命令行环境无法截取位图，故以终端文本记录代替截图）：

```
Connecting to agent at http://localhost:7777...

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 5                                              <- 功能5：设置日志目录
Please input directory containing log files:
>>> dataset

Please choose: ...
>>> 1                                              <- 功能1：列出日志文件
Log files in the current directory:
- basic-fail.log
- basic-multiple.log
- basic.log

Please choose: ...
>>> 3                                              <- 功能3：分析全部文件
Please input degree of parallelism (0 for auto):
>>> 0
Analysis completed.

Please choose: ...
>>> 4                                              <- 功能4：查询分析结果
Please input a file name to query:
>>> basic.log
Analysis result for 'basic.log':
  State: Succeeded, WorkerId: 2
  Entries:
    LineNo=0, Timestamp=2026-06-05T16:00:29.0450000+00:00, PodName=userservice-0, Severity=Info, EventType=Call, RequestId=3a013a08-6853-49fc-8f06-50daeb5c1e51, TargetService=authservice, DurationMs=18
    LineNo=1, Timestamp=2026-06-05T16:00:31.0860000+00:00, PodName=userservice-1, Severity=Info, EventType=Request, RequestId=1177c344-115e-4f85-b8ec-c9164d132b79, Method=GET, Path=/api/user/john, StatusCode=404
    LineNo=2, Timestamp=2026-06-05T16:05:45.3220000+00:00, PodName=gateway-0, Severity=Error, EventType=Internal, ExceptionName=System.InvalidOperationException, ExceptionMessage=Failed to load gateway routing configuration.
```

## 鲁棒性测试记录（终端输出）

```
Please choose: ...
>>> 4
Please input a file name to query:
>>> nonexist.log                                  <- 查询不存在的文件
Error: FileNotFound: File 'nonexist.log' is not found.

Please choose: ...
>>> 2
Please input degree of parallelism (0 for auto):
>>> 0
Please input file names to analyze (separated by commas):
>>> basic.log, no-such-file.log                   <- 分析列表中含不存在的文件
Analysis failed: FileNotFound: File 'no-such-file.log' is not in the current directory or does not exist.

Please choose: ...
>>> 5
Please input directory containing log files:
>>> nonexist-dir                                  <- 切换到不存在的目录
Error: DirectoryNotFound: Directory 'nonexist-dir' does not exist., please try again:
Please input directory containing log files:
>>> dataset                                       <- 重新输入有效目录，恢复正常
```

可以看到，查询不存在的文件、分析包含不存在文件名的列表、切换到不存在的目录等非法输入，都会被 Agent 以业务错误码（`FILE_NOT_FOUND` / `DIRECTORY_NOT_FOUND`）返回并由 RemoteCli 友好提示，程序与 Agent 都不会崩溃。


## (Q3.1)

我认为开发网络应用程序与开发非网络应用程序的主要区别有以下几点：

1. **程序结构从「单机函数调用」变为「分布式服务」**：非网络程序在同一个进程内直接调用函数、共享内存；网络程序则需要把逻辑拆分为服务端与客户端，通过 RPC 在网络上交换数据。因此业务逻辑被拆成「服务端如何提供能力」和「客户端如何请求能力」两个面，开发时必须在两端同时考虑。
2. **数据不再是本地的对象，而是需要「序列化 / 反序列化」**：本地程序直接引用对象即可；网络程序传输的是字节流（本作业中是 Protobuf 消息），因此需要额外编写类型转换层（`GrpcTypeConverter`、`GrpcLogEntryVisitor`），并维护 C# 类型与 Protobuf 类型、以及两边命名法（PascalCase ↔ snake_case）的一致性。
3. **必须考虑网络的不确定性**：网络请求可能超时、被中断、服务端不可达；返回的错误除了「业务错误」（如文件不存在）还有「传输层错误」（RpcException）。因此客户端需要做异常捕获，服务端需要保证对任何输入都不会崩溃（这直接影响服务的可用性）。
4. **并发与状态管理更复杂**：Agent 作为常驻服务是「有状态」的（保存当前目录、分析结果），且可能同时被多个客户端调用，必须通过依赖注入将 `LogFileAnalyzer` / `AgentSession` / `AgentService` 注册为单例，并注意内部共享状态的线程安全；非网络程序则没有「并发请求」这个维度。
5. **调试与部署方式不同**：需要同时启动服务端与客户端两个进程来联调，难以像单机程序那样一步打断点；启动参数（监听地址、端口）、环境变量（`ASPNETCORE_URLS`）等也引入了额外复杂度。

额外的难点与复杂之处总结为：类型系统的跨语言/跨进程映射、错误处理的多层化（传输错误 + 业务错误）、有状态服务的并发安全、以及「服务端永不因非法输入崩溃」这一硬性要求。

## (Q3.2.b)

本次作业使用了 AI（Cline 编码助手）。

### 我给予 AI 的提示词

> 阅读 `docs/03-async-grpc` 的说明文档、`src/test-03-async-grpc` 测试代码以及 `Protos/log_analyzer.proto`，补齐 `GrpcTypeConverter`、`GrpcLogEntryVisitor`、`AgentSession`、`AgentService` 与 `RemoteCli/Program.cs` 中的 TODO 实现，使 `dotnet test test-03-async-grpc -c Release` 全部通过；随后启动 Agent 并运行 RemoteCli 做端到端验证，最后撰写 `docs/03-async-grpc/report.md`。

### 对 AI 的使用方式

主要是让 AI 编写一部分作业代码（T3.1 / T3.2 的 TODO 实现），同时也向 AI 询问了一些 gRPC 客户端流式调用的具体写法（例如如何用 `ResponseStream.ReadAllAsync()` 读取服务端流），以及 Protobuf oneof 字段的 C# 使用方式。

### AI 的解答是否出现过错误

在初版实现中没有出现功能性错误，测试一次通过；但 AI 在端到端联调时一度没有注意到 `RemoteCli` 与 `LocalCli` 的不同——`RemoteCli` 启动后**不会自动提示输入目录**，需要先通过菜单选项 5 切换目录，导致最初的联调输入序列里目录设置没有生效。经分析 `Program.cs` 框架后修正了联调流程。

### 从 AI 那里得知的新知识

+ gRPC 服务端流式返回在 C# 中通过在 `IServerStreamWriter<T>` 上反复 `WriteAsync` 实现，客户端则通过 `call.ResponseStream.ReadAllAsync()` 逐条读取。
+ Protobuf 的 `oneof` 字段在 C# 中表现为 `PayloadCase` 枚举（如 `GetAnalysisResultResponse.PayloadOneofCase.Header`）与对应的 `Header` / `LogEntry` 属性，只能设置其中一个。
+ `optional string` 字段会生成 `HasErrorMessage` 属性用于判断字段是否被显式设置。
+ 有状态 gRPC 服务需要把相关类注册为单例（`AddSingleton`），否则每次请求都会新建状态导致相互覆盖。

