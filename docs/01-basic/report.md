## (Q1.1)

### 1

在LogParser\Parser\LogFileParser.cs中

```
using var csv = new CsvReader(logFile, config);
            csv.Context.RegisterClassMap<LogRecordMap>();
            
            foreach (var logRecord in csv.GetRecords<LogRecord>())

```

csv.GetRecords按照CSV规则解析各行。而每一列对应什么含义，也是在 LogFileParser 中通过 Index 指定的：

```
  Map(m => m.LineNo).Index(0);
  Map(m => m.Timestamp).Index(1);
  Map(m => m.PodName).Index(2);
  Map(m => m.Message).Index(3);

```

### 2

```

using (var doc = JsonDocument.Parse(logRecord.Message))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var eventElement))

```

根据 eventElement 来判断类型

### 3

使用JsonSerializer.Deserialize。

同时设置

private static JsonSerializerOptions options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
    };

调用时将options传入，完成命名法转换

## （Q1.2）

+ Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)
+ TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor)
+ TResult Visit(CallLogEntry entry) 或 TResult Visit(RequestLogEntry entry) 或 TResult Visit(InternalLogEntry entry)

## （Q1.3）

没有使用AI，时间花了大概三小时。比程设作业难。我认为我完成作业只是模仿示例完成了代码，还没有完全看懂整个架构。

