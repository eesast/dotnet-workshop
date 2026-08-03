# 01-basic 作业报告

- 姓名：李易臻
- 班级：秀钟31
- 学号：2023013461

## Q1.1

1. `LogFileParser.Parse` 使用 CsvHelper 的 `CsvReader.GetRecords<LogRecord>()` 读取 CSV。字段与位置的对应关系由 `LogRecordMap` 指定：`LineNo`、`Timestamp`、`PodName`、`Message` 分别映射到索引 0、1、2、3。因此代码没有直接用字符串 `Split(',')`，而是由 CsvHelper 按 CSV 规则完成分割，这也能正确处理被双引号包围且内部含逗号的 JSON 字段。
2. `LineParser.ParseLine` 先用 `JsonDocument.Parse(logRecord.Message)` 读取 JSON，再通过 `root.TryGetProperty("event", out var eventElement)` 取得事件字段，最后根据 `eventElement.GetString()` 的值选择 `CreateCall`、`CreateRequest` 或 `CreateInternal`。
3. 确定日志类型后，三个创建方法都调用 `JsonSerializer.Deserialize<T>(logRecord.Message, options)` 反序列化。消息 record 的必需字段使用 `[property: JsonRequired]` 标记，所以缺失字段时会抛出 `JsonException`。`JsonSerializerOptions.PropertyNamingPolicy` 设置为 `JsonNamingPolicy.KebabCaseLower`，从而把 C# 的 `RequestId`、`StatusCode` 等属性与 JSON 中的 `request-id`、`status-code` 等键对应起来。

## Q1.2

以 Call 日志为例，调用链为：

1. `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
2. `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
3. `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

`Dump` 只持有抽象类型 `LogEntry`，运行时通过具体对象的 `Accept` 方法完成动态分派；`Accept` 再调用与具体日志类型匹配的 `Visit` 重载。

## Q1.3.b

本次作业使用了 AI。我给出的主要提示包括：检查 `01-basic` 的作业要求，说明需要修改的文件和实现思路，协助完成 Request、Internal 日志解析及 Visitor，并解释代码每一行的含义。所有操作限定在 D 盘的本地作业目录中完成。

AI 的主要帮助是整理课程资料与仓库文档、定位未实现的方法、对照现有 Call 日志实现推导 Request 和 Internal 的数据结构，并安排测试与打包流程。这比我单独搜索更快的地方在于，它能把分散在 `guidance.md`、源代码和测试中的要求对应起来，例如由测试中的示例确认 Internal 日志需要按“冒号加空格”拆分异常名称和异常信息。

AI 的结果不能直接视为正确答案。它可能误解题意、忽略 CSV 或 JSON 的边界情况，也可能给出能够编译但不符合测试的数据字段。因此我保留官方测试作为判断标准，先确认未实现代码导致测试失败，再逐项实现并重新运行 Debug 和 Release 测试。报告中的调用链、字段映射和命名策略也都回到实际源代码逐项核对，而不是只采用 AI 的概括。
