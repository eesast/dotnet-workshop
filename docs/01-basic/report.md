# C01 Basic 作业报告

## Q1.1

### 1. CSV 的分割与字段含义

框架代码中并没有显式地调用 `String.Split(',')` 之类的语句来按逗号分割日志行。实际的分割工作由 **CsvHelper** 完成：在 `LogFileParser.Parse` 中，代码创建了 `CsvReader` 并逐行读取：

```csharp
using var csv = new CsvReader(logFile, config);
csv.Context.RegisterClassMap<LogRecordMap>();

foreach (var logRecord in csv.GetRecords<LogRecord>())
{
    yield return LineParser.ParseLine(logRecord);
}
```

“每一行的第几个字段代表什么含义”是通过 `LogRecordMap` 类指定的。该类继承自 `ClassMap<LogRecord>`，在构造函数中把 `LogRecord` 的属性按列索引（从 0 开始）映射：

```csharp
Map(m => m.LineNo).Index(0);
Map(m => m.Timestamp).Index(1);
Map(m => m.PodName).Index(2);
Map(m => m.Message).Index(3);
```

即第 0 列是行号 `LineNo`，第 1 列是时间戳 `Timestamp`，第 2 列是容器名 `PodName`，第 3 列是 JSON 格式的 `Message`。

### 2. 判断日志种类的语句

判断日志种类发生在 `LineParser.ParseLine` 方法内。先用 `JsonDocument.Parse` 解析 `message`，再读取 `event` 属性：

```csharp
var root = doc.RootElement;
if (root.TryGetProperty("event", out var eventElement))
{
    return eventElement.GetString() switch
    {
        "call" => LineParser.CreateCall(logRecord),
        "request" => LineParser.CreateRequest(logRecord),
        "internal" => LineParser.CreateInternal(logRecord),
        _ => throw new FormatException(...)
    };
}
```

也就是说，通过 `root.TryGetProperty("event", ...)` 检查是否存在 `event` 键，再通过 `eventElement.GetString() switch` 对 `"call"`、`"request"`、`"internal"` 三种取值进行分发。

### 3. JSON 的解析库方法

确定日志种类后，对 JSON 的正式解析使用的是 **System.Text.Json** 库中的 `JsonSerializer.Deserialize<T>`：

```csharp
var callMessage = JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options);
var requestMessage = JsonSerializer.Deserialize<RequestMessage>(logRecord.Message, options);
var internalMessage = JsonSerializer.Deserialize<InternalMessage>(logRecord.Message, options);
```

`T` 分别对应三个私有 record：`CallMessage`、`RequestMessage`、`InternalMessage`。之前的 `JsonDocument.Parse` 只用于读取顶层的 `event` 字段做类型判断。

### 4. 如何防止字段缺失

框架在每个 message record 的属性上加了 `[property: JsonRequired]` 特性，例如：

```csharp
private record CallMessage(
    [property: JsonRequired] string Severity,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] string TargetService,
    [property: JsonRequired] int DurationMs
);
```

`JsonRequired` 会要求反序列化时 JSON 中必须存在对应属性；如果缺失（例如 Call 日志里没有 `request-id`），`JsonSerializer.Deserialize` 会抛出 `JsonException`。此外，`Deserialize` 结果再与 `?? throw new FormatException(...)` 配合，可以兜底处理返回 `null` 的情况（例如 JSON 字面量为 `null`）。

### 5. JSON 键名与 C# 属性名的转换

转换由 `JsonSerializerOptions` 的命名策略完成：

```csharp
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
```

`JsonNamingPolicy.KebabCaseLower` 让反序列化器把 JSON 中的 kebab-case（烤串命名法）键 `request-id`、`status-code` 等，自动映射到 C# 侧大驼峰命名的属性 `RequestId`、`StatusCode`，因此 record 的属性只需按 C# 惯例写成大驼峰即可。

## Q1.2

以一条 Call 日志为例，调用 `KeyValueVisitor` 的 `Dump` 方法后，方法调用链如下：

1. `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
2. `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
3. `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

调用过程说明：`Dump` 拿到的是 `LogEntry` 基类引用，它调用 `entry.Accept(this)`；由于运行时对象的真实类型是 `CallLogEntry`，会执行 `CallLogEntry` 重写的 `Accept`，其内部 `visitor.Visit(this)` 中的 `this` 被静态类型化为 `CallLogEntry`，从而命中 `KeyValueVisitor.Visit(CallLogEntry)` 这个重载。这就是访问者模式通过多态完成“双重分发”的过程。

## Q1.3.b

本次作业使用了AI辅助完成，回答 (Q1.3.b)。

### 我给出的提示词

我给出的提示词大致如下：

- 先阅读整个仓库并着重阅读本仓库的 `README` 与 `docs/00-prepare/guidance.md`，让AI了解任务安排与仓库结构；
- 完成任务前先给出需要读那些代码，已有代码的大致介绍以及阅读重点，指引我更快理解代码并协助规划后续任务；
- 约定不直接完成整个项目，在一些必要环节让AI只给出操作步骤，由我自己手动完成；
- 由于我不太熟悉md格式文档的写法，所以我写完md文档后让AI检查并帮忙调整格式。

### AI 的解答好在哪里

- 能快速定位框架中所有 `TODO: T1.2` / `TODO: T1.3` 的位置，并以已经完整的代码为模板，给出风格一致的实现；
- 在动手前指出需要重点理解的文件和段落，减少阅读量，节省时间；

### AI 的解答存在的问题或不如我的地方

- 第一次解答时提示词写的不够完善，AI没能在我想暂停的时候暂停，直接完成了大部分任务，导致我对代码结构和功能实现方式不是很了解，我需要花一定时间调整提示词以达到想要的效果；
