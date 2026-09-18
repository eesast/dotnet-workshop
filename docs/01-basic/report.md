### (Q1.1)

在给出的代码框架 `Parser` 中：

+ 哪条语句或哪几条语句将日志按逗号进行分割？代码中，我们是如何指定每一行的第几个字段代表何种意义的？
```
var config = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    HasHeaderRecord = false
};
using var csv = new CsvReader(logFile, config);
csv.Context.RegisterClassMap<LogRecordMap>();
            
foreach (var logRecord in csv.GetRecords<LogRecord>())
{
    yield return LineParser.ParseLine(logRecord);
}
```
```
internal class LogRecordMap : ClassMap<LogRecord>
{
    public LogRecordMap()
    {
        Map(m => m.LineNo).Index(0);
        Map(m => m.Timestamp).Index(1);
        Map(m => m.PodName).Index(2);
        Map(m => m.Message).Index(3);
    }
}
```

+ 在对日志中 JSON 格式的 `message` 字段进行读取时，我们是在哪个方法内用哪几条语句判断这一行日志的种类（Call / Request / Internal）的？
```
eventElement.GetString() switch
{
    "call" => LineParser.CreateCall(logRecord),
    "request" => LineParser.CreateRequest(logRecord),
    "internal" => LineParser.CreateInternal(logRecord),
    _ => throw new FormatException($"Unknown event type: {eventElement.GetString()} in log message: {logRecord.Message}")
};
```
+ 在确定了日志种类后，我们是调用了哪个库方法对 JSON 进行解析的？

`JsonSerializer.Deserialize`

+ 进一步，我们的框架代码是如何防止日志中有字段缺失的？（例如所给的 Call 日志的 `message` 中缺失 `request_id` 字段）

`throw new FormatException(...)`

+ 更进一步，日志中的 JSON 的键是 `abc-def` 命名法（称为烤串命名法），而我们的解析结果却是放在 `AbcDef` 命名法（称为大驼峰命名法）的属性里，我们的框架代码中是如何告诉 JSON 解析器完成这一命名法转换的？

```
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};

JsonSerializer.Deserialize<T>(..., options);
```

### (Q1.2)

以一个 Call 事件的解析结果为例，当调用 `KeyValueVisitor` 的 `Dump` 方法后，都有哪些方法被调用？请补充完整如下的方法调用链（.NET 内置库无需写出）：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
+ `Dictionary<string, string> Visit(CallLogEntry entry)`

### (Q1.3)

未使用AI。从开始到通过全部测试花费1.5小时。本次作业相比程序设计作业代码量较少，具体逻辑编写也较为简单，但是需要理解已有代码架构具有一定难度。目前作答并不完美，有以下两个问题：
1. 对于 internal message 解析的操作并未考虑可能的不存在 ':' 时的情况，此时代码将直接抛出异常
2. 代码commit信息和此前风格未保持一致，未使用git emoji