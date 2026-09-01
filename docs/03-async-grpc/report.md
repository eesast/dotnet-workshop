# 03-async-grpc 报告

## 功能实现说明

### T3.1 Agent 服务端

**GrpcLogEntryVisitor / GrpcTypeConverter**：补全 Request/Internal 两种日志条目的双向转换。访问者模式把 `LogEntry` 转成 protobuf 的 `LogEntryMessage`（oneof 字段），`ConvertFromGrpc` 按 `EntryCase` switch 还原成对应 record。枚举映射严格按 proto 定义（INFO=0/WARNING=1/ERROR=2；CALL/REQUEST/INTERNAL）。

**AgentSession**：业务逻辑层。核心是「**永不抛异常给 gRPC 层**」——所有操作把结果包进 `OperationStatusMessage`（Success/Code/Message），异常分类映射：

| 异常 | AgentErrorCode |
|---|---|
| `ChangeDirectory` 返回 false | `DirectoryNotFound` |
| `ArgumentException` | `InvalidArgument` |
| `InvalidOperationException`（如重复分析） | `InvalidOperation` |
| 文件不在目录清单（`TryGetAnalysisResult` false） | `FileNotFound` |
| 其余异常 | `InternalError` + 日志 |

`GetAnalysisResult` 返回响应列表：首条是 `Header`（状态/worker/错误消息），`Succeeded` 时逐条跟随 `LogEntry`，其他状态只返回 Header——与测试断言的条数严格一致。

**AgentService**：gRPC 基类薄封装，全部转发到 Session；`GetAnalysisResult` 是 server-streaming，把 Session 返回的列表逐条 `await responseStream.WriteAsync()`。

### T3.2 RemoteCli 客户端

菜单 1-6 全部实现：`ReadDegreeOfParallelism`/`ReadFileNames` 输入循环校验；`GetAnalysisResult` 用 `await foreach (call.ResponseStream.ReadAllAsync())` 消费流，Header 打印状态、LogEntry 经 `GrpcTypeConverter.ConvertFromGrpc` 还原后用 `KeyValueVisitor.Dump` 输出键值对。所有 RPC 调用包 `RpcException` 捕获（如 Agent 掉线时打印 `Unavailable` 而非崩溃）。

### 端到端验证（本机实跑）

Agent 监听 `http://localhost:5000`，RemoteCli 走完完整流程：换目录到 dataset → 列出 3 个 .log → 指定分析 basic.log（并行度 4）→ 流式取回 3 条日志（Call/Request/Internal 各一条，字段完整）→ AnalyzeAll 后取回 basic-multiple.log 全部 45 条 → 查询不存在的 hahahaha.log 返回 `FileNotFound` 错误码。

运行截图与鲁棒性测试（非法文件名/未分析文件/Agent 停机）记录见 `screenshots/`（运行日志存档见本仓库 `docs/03-async-grpc/demo_log.txt`）。

## 问答题

### (Q3.1) 网络应用 vs 非网络应用

**区别**：非网络程序的状态都在一个进程内存里，函数调用是可靠、同步、即答的；网络应用被切成客户端/服务端两半，中间隔着一条**不可靠、有延迟、会断开**的信道。这带来几个额外难点：

1. **部分失败**：本地调用要么成功要么抛异常，网络调用还有第三种状态——「不知道成没成功」（超时）。所以 Agent 的每个操作都要设计成幂等或可重试的，且必须用状态码（我们的 `OperationStatusMessage`）而不是异常向上传递结果。
2. **数据表示**：两端的内存布局不同，必须经 protobuf 这类 IDL 序列化。`LogEntry` 的 record 继承结构无法直接上网络线，要靠 oneof + 访问者模式做双向映射——这部分代码量几乎与业务逻辑相当。
3. **生命周期错位**：服务端是常驻进程，客户端随时连上/断开。LocalCli 的异常最多让本次交互崩溃，Agent 的异常会让**所有**后续客户端失去服务，所以 Session 层必须 catch-all（这就是 guidance 反复强调的「服务绝不能挂」）。
4. **异步模型**：网络 IO 的延迟天然要求 async/await，否则每个等待都阻塞一个线程。流式响应（server streaming）还引入了「结果分多次到达」的编程模型，必须用 `IAsyncEnumerable` 消费。

复杂之处还包括：调试要两端同时看日志、时间戳要统一时区（`Timestamp.FromDateTimeOffset`）、并发请求下服务端的状态保护（复用 02 的 `_syncRoot` 锁约定）。

### (Q3.2.b) AI 使用情况

**提示词**：提供了 proto 文件、GrpcTypeConverter 框架和「异常→错误码」的映射要求，让 AI 补全 AgentSession/AgentService/RemoteCli。

**使用方式**：主要是「让 AI 写部分作业代码」+「讲解框架」。gRPC 的 C# server-streaming 写法（`IServerStreamWriter<T>` + `ReadAllAsync`）我原本不熟，AI 给出了正确的 API 形态。

**AI 的错误**：一次把 `GetAnalysisResult` 的 session 方法签名写成 `async Task`（框架给的是同步返回 `IReadOnlyList`），导致 AgentService 里多余 await 编译警告；对照框架签名修正。另一次忘了 Header 之后再写 `Status` 字段（protobuf oneof 字段没赋值时反序列化端 `PayloadCase` 判断会走错分支），测试断言 `PayloadOneofCase.Header` 失败后补上。

**新学到的知识**：① protobuf `oneof` 的语义——同组字段同时至多一个生效，读端用 `EntryCase`/`PayloadCase` 判别；② gRPC 四种流模式中 server-streaming 的 C# 写法；③ 「错误码包进响应体而不是抛 RPC 异常」是业务错误 vs 传输错误的分界——`RpcException` 只留给信道级故障。

**难度评价**：三节中最高，但难在「接口对接的琐碎」而非算法。
