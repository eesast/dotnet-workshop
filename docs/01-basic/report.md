Q1.1：

1.using var csv = new CsvReader(logFile, config);

​             csv.GetRecords<LogRecord>()

通过在LogFileParser.cs中定义一个继承自ClassMap<LogRecord>的映射类LogRecordMap实现的。通过Map(m => m.LineNo).Index(0);之类，将第0列设置为行号，将第3列设置为Message。

2.var root = JsonDocument.Parse(logRecord.Message).RootElement;

root.TryGetProperty("event", out var eventElement)

ventElement.GetString() switch {    "call" => ...,    "request" => ...,    "internal" => ... }

3.调用了 System.Text.Json 库中的 JsonSerializer.Deserialize<T>(string, JsonSerializerOptions)

给每个属性加上了property: JsonRequired特性，如果缺失特性，会抛出异常

定义了JsonSerializerOptions的options静态对象，设置了PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower

Q1.2：

Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)

TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)

Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)

Q1.3：

提示词：给我整个代码的类的结构图，与程序执行的流程图。AI的解答比我更加迅速，并且能够给我提供相对应的知识点，便于更好更快的理解代码。

