## 问答题

### (Q1.1)

在给出的代码框架 `Parser` 中：

+ 哪条语句或哪几条语句将日志按逗号进行分割？代码中，我们是如何指定每一行的第几个字段代表何种意义的？

```c
// 按逗号进行分割
using var csv = new CsvReader(logFile, config);
csv.Context.RegisterClassMap<LogRecordMap>();
foreach (var logRecord in csv.GetRecords<LogRecord>())
```

通过 LogRecordMap 指定意义；

+ 在对日志中 JSON 格式的 `message` 字段进行读取时，我们是在哪个方法内用哪几条语句判断这一行日志的种类（Call / Request / Internal）的？

```c
// 通过 LineRarse 的 ParseLine 方法

if (root.TryGetProperty("event", out var eventElement))
{
    return eventElement.GetString() switch
    {
        "call" => LineParser.CreateCall(logRecord),
        "request" => LineParser.CreateRequest(logRecord),
        "internal" => LineParser.CreateInternal(logRecord),
        _ => throw new FormatException($"Unknown event type: {eventElement.GetString()} in log message: {logRecord.Message}")
    };
}
else
{
    throw new FormatException($"Log message does not contain 'event' property: {logRecord.Message}");
}

```

+ 在确定了日志种类后，我们是调用了哪个库方法对 JSON 进行解析的？

用 System.Text.Json 的 JsonSerializer.Deserialize<T>(logRecord.Message, options)

  + 进一步，我们的框架代码是如何防止日志中有字段缺失的？（例如所给的 Call 日志的 `message` 中缺失 `request_id` 字段）

  使用 [property: JsonRequired]

  + 更进一步，日志中的 JSON 的键是 `abc-def` 命名法（称为烤串命名法），而我们的解析结果却是放在 `AbcDef` 命名法（称为大驼峰命名法）的属性里，我们的框架代码中是如何告诉 JSON 解析器完成这一命名法转换的？

```c
// 使用 JsonSerializerOptions
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
```

### (Q1.2)

以一个 Call 事件的解析结果为例，当调用 `KeyValueVisitor` 的 `Dump` 方法后，都有哪些方法被调用？请补充完整如下的方法调用链（.NET 内置库无需写出）：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`

+ `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

### (Q1.3)

#### (Q1.3.b)

如果使用了 AI，你给予 AI 的提示词是什么？你认为 AI 给出的解答、你完全凭借传统搜索引擎以及自己的能力能够写出的解答之间，AI 的解答比你好在哪？AI 又有哪些解答是存在问题的，或者至少是不如你自己的解答的？给出你的理由。

- 使用的是 copilot 补全代码，以及借助 codex 理解代码；

- 我让 codex 理解一下 01-basic 代码部分已有的工作；然后自己阅读代码的时候借助 copilot 补全注释的能力理解一些看不明白的语法以及补全补全一些重复的代码；但是 copilot 的补全能力不像 codex 之类的整个仓库一起阅读好像不太能理解代码上下文和所文件情况比如对于对于 Call, Request 之类不同 Json 字段他的补充方式就是完全照搬原本写的，所以还是得靠自己 review 完动手调（）