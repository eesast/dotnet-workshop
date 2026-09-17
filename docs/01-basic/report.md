## Q 1.1
### 1.1.1
- 以下代码负责按逗号分割：
```c sharp
var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csv = new CsvReader(logFile, config);
            csv.Context.RegisterClassMap<LogRecordMap>();
```
- 经过CSV分割后，每一行的文本通过以下代码对应到LineNo、Timestamp、PodName、Message四个字段
```c sharp
public LogRecordMap()
        {
            Map(m => m.LineNo).Index(0);
            Map(m => m.Timestamp).Index(1);
            Map(m => m.PodName).Index(2);
            Map(m => m.Message).Index(3);
        }
```
，存为一个LogRecord对象，之后单独`JsonDocument.Parse(logRecord.Message)`提取`event`类型，交由`LineParser.CreateCall(logRecord)`等方法，按照`LogEntries.cs`中的定义映射各自含义
### 1.1.2
- 在`LineParser::ParseLine(LogRecord logRecord)`中：
```c sharp
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
### 1.1.3
- 使用json库：
```c sharp
using System.Text.Json.Serialization;
...
JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options);
...
```

- 使用`[property: JsonRequired]`强制指定，不存在时报错
- 通过options传入源文本的编码方式Kebab
``` c sharp
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
...
var callMessage = JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options)
...

```
驼峰命名法在LogEntry的子类中分别指定。

## Q1.2
- `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
- `TResult LogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
- `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
- `Dictionary<string, string> Visit(CallLogEntry entry)`

## Q1.3
- 有使用
### Q1.3.b
- 主要使用了Copilot的自动补全，相比我自己写更省时间，减少了排错成本，经测试核查无误
- 也让ai解释了一些语句的含义、函数的用法