# 01-basic 问答报告

## Q1.1

日志的 CSV 字段不是通过手动调用字符串的 `Split` 方法进行分割的，而是由 CsvHelper 完成。`LogFileParser.Parse` 中的 `csv.GetRecords<LogRecord>()` 逐条读取 CSV 记录；`LogRecordMap` 通过 `Map(...).Index(...)` 指定字段含义，其中索引 0 到 3 依次对应 `LineNo`、`Timestamp`、`PodName` 和 `Message`。

JSON 格式的 `message` 在 `LineParser.ParseLine` 中被判断种类。代码先调用 `JsonDocument.Parse(logRecord.Message)` 得到 JSON 文档，再通过 `root.TryGetProperty("event", out var eventElement)` 读取 `event`，最后根据 `eventElement.GetString()` 的结果在 `switch` 表达式中选择 `CreateCall`、`CreateRequest` 或 `CreateInternal`。

确定日志种类后，代码调用 System.Text.Json 提供的 `JsonSerializer.Deserialize<T>(logRecord.Message, options)`，将 JSON 反序列化为对应的消息记录。各消息记录的属性使用了 `[property: JsonRequired]`，因此必需字段缺失时，反序列化会抛出 `JsonException`。`options` 的 `PropertyNamingPolicy` 被设置为 `JsonNamingPolicy.KebabCaseLower`，所以 C# 中的 `RequestId`、`StatusCode` 等大驼峰属性能够分别对应 JSON 中的 `request-id`、`status-code` 等烤串命名字段。

## Q1.2

以 Call 事件为例，调用链如下：

1. `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
2. `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
3. `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

`Dump` 持有的是 `LogEntry` 基类引用，它先调用具体日志对象的 `Accept`。`CallLogEntry.Accept` 再执行 `visitor.Visit(this)`；由于此处 `this` 的类型是 `CallLogEntry`，最终会进入接收 `CallLogEntry` 参数的 `Visit` 重载。这使访问者能够针对不同日志类型执行不同的导出逻辑。

## Q1.3.b

本次作业使用了 AI。我使用过的主要提示词包括：

> 请你读dotnet-guidance，教我完成01-basic的作业

> 我还是不会T1.3，请给我修改，并完成问答报告

AI 的主要帮助是快速定位了 T1.2 和 T1.3 对应的文件、未实现的方法和测试要求，并结合已有的 Call 实现解释了 Request、Internal 的实现方式。对我不熟悉的访问者模式，AI 把 `Dump -> Accept -> Visit` 的调用关系具体对应到项目代码，比只搜索访问者模式的通用定义更直接。它还可以统一核对字典键名、时间格式和数值转换，减少因为字段拼写不一致造成的测试失败。

AI 的不足是，第一次说明仍然包含较多概念和步骤，没有立刻解决我对 T1.3 具体写法的疑问。AI 能生成能够通过当前测试的实现，但不能证明我已经理解访问者模式，也可能忽略课程测试之外的边界情况。问答报告中的个人体验也无法由 AI 代替，因此我仍需要阅读代码、运行 Release 测试，并根据自己的理解检查和修改生成的内容。相比完全依靠搜索引擎，AI 整理项目内信息更快；相比自己独立完成，它减少了探索过程，但也更容易让我在没有理解代码的情况下直接接受答案。
