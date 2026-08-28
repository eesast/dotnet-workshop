# Report for Basic Functions

## Q1.1

### 按逗号分割与字段含义

代码框架并不是手动调用 `String.Split` 来按逗号分割日志，而是使用了 **CsvHelper** 库。在 `LogFileParser.Parse` 方法中：

```csharp
using var csv = new CsvReader(logFile, config);
csv.Context.RegisterClassMap<LogRecordMap>();
foreach (var logRecord in csv.GetRecords<LogRecord>())
{
    yield return LineParser.ParseLine(logRecord);
}
```

- `new CsvReader(logFile, config)` 创建了 CSV 读取器，`csv.GetRecords<LogRecord>()` 负责按逗号把每一行拆分成若干字段。
- 每一行第几个字段代表什么含义，由 `LogRecordMap`（继承 `CsvHelper.Configuration.ClassMap<LogRecord>`）指定：

  ```csharp
  Map(m => m.LineNo).Index(0);    // 第 0 列是行号
  Map(m => m.Timestamp).Index(1); // 第 1 列是时间戳
  Map(m => m.PodName).Index(2);   // 第 2 列是容器名
  Map(m => m.Message).Index(3);   // 第 3 列是 JSON 消息
  ```

  并通过 `csv.Context.RegisterClassMap<LogRecordMap>()` 注册生效。

### 判断日志种类

在 `LineParser.ParseLine` 方法中，通过以下语句判断这一行日志的种类：

```csharp
using var doc = JsonDocument.Parse(logRecord.Message);
var root = doc.RootElement;
if (root.TryGetProperty("event", out var eventElement))
{
    return eventElement.GetString() switch
    {
        "call"     => LineParser.CreateCall(logRecord),
        "request"  => LineParser.CreateRequest(logRecord),
        "internal" => LineParser.CreateInternal(logRecord),
        _ => throw new FormatException(...)
    };
}
```

即先用 `JsonDocument.Parse` 解析 `message`，再用 `root.TryGetProperty("event", ...)` 取出 `event` 字段，最后用 `switch` 表达式根据 `event` 的值（`"call"` / `"request"` / `"internal"`）分发到对应的创建方法。

### 解析 JSON 所用的库方法

确定日志种类后，调用的是 `System.Text.Json` 中的 `JsonSerializer.Deserialize<T>(json, options)`（例如 `JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options)`）。

**防止字段缺失：** 每个 `Message` record 的字段都标注了 `[property: JsonRequired]` 特性（例如 `[property: JsonRequired] string RequestId`）。当 JSON 中缺少被标记的字段时，`JsonSerializer.Deserialize` 会抛出 `JsonException`；此外还通过 `?? throw new FormatException(...)` 处理反序列化结果整体为 `null` 的情况。

**命名法转换：** 通过 `JsonSerializerOptions` 的命名策略完成：

```csharp
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
```

`PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower` 会让序列化器把大驼峰属性名（如 `RequestId`、`TargetService`、`DurationMs`）自动转换为烤串命名法（`request-id`、`target-service`、`duration-ms`）去匹配 JSON 中的键。

## Q1.2

以一个 Call 事件的解析结果为例，`Dump` 方法被调用后的方法调用链如下：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`（经 `entry.Accept(this)` 多态调用）
+ `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

## Q1.3

（本问为个人反思题，请根据你的实际情况选择作答。下方以 Q1.3.b 为例。）

### Q1.3.b

本次作业我使用了 AI 辅助完成。我给予 AI 的提示词大致为：「帮我完成 01-basic 要求的所有作业」，并在此之前通过提问明确了当前分支、任务内容与需要改动的文件。

与完全依靠传统搜索引擎和自己能力写出的解答相比，AI 的解答好在：能快速梳理出代码框架中需要补全的 `TODO` 位置，并给出与已有 `Call` 实现风格一致的参考代码，节省了大量阅读与试错的时间。

但 AI 的解答也存在不足：例如它有时会忽略 `internal` 日志中 `exception` 字段需要按「冒号加空格」拆分成 `ExceptionName` 与 `ExceptionMessage` 这一细节，需要人工结合测试用例（`TestParseInternalLogExampleFailed`）来确认异常格式的处理方式。因此最终代码仍需要人工 review 与验证。
