#  (Q1.1)

## 1. 按逗号分割日志的语句，以及如何指定每个字段的意义

本框架借助第三方 CSV 解析库 **CsvHelper** 来完成按逗号分列的工作：

```csharp
using var csv = new CsvReader(logFile, config);
csv.Context.RegisterClassMap<LogRecordMap>();
foreach (var logRecord in csv.GetRecords<LogRecord>()) { ... }
```

其中真正「按逗号把一行切成多个字段」的工作由 `CsvReader` / `csv.GetRecords<LogRecord>()` 在库内部完成（它还能正确处理 `message` 字段两端的双引号以及 JSON 内部出现的逗号）。

「每一行的第几个字段代表何种意义」是通过一个继承自 `ClassMap<LogRecord>` 的映射类 `LogRecordMap` 来指定的，使用 `Map(...).Index(n)` 把 CSV 的第 `n` 列绑定到 `LogRecord` 的对应属性上：

```csharp
internal class LogRecordMap : ClassMap<LogRecord>
{
    public LogRecordMap()
    {
        Map(m => m.LineNo).Index(0);    // 第 0 列 -> LineNo
        Map(m => m.Timestamp).Index(1); // 第 1 列 -> Timestamp
        Map(m => m.PodName).Index(2);   // 第 2 列 -> PodName
        Map(m => m.Message).Index(3);   // 第 3 列 -> Message
    }
}
```

即：`Index(0)` 对应 `lineno`、`Index(1)` 对应 `timestamp`、`Index(2)` 对应 `pod-name`、`Index(3)` 对应 `message`。随后通过 `csv.Context.RegisterClassMap<LogRecordMap>()` 让 CsvHelper 读取时按这个映射把每列填入 `LogRecord`。

## 2. 在哪个方法内、用哪几条语句判断日志种类

在 `Parser/LineParser.cs` 的 `ParseLine(LogRecord logRecord)` 方法内判断。先用 `JsonDocument` 把 `message` 当作 JSON 解析，再读取其中的 `event` 字段，用 `switch` 表达式根据其取值分流到不同的工厂方法：

```csharp
using (var doc = JsonDocument.Parse(logRecord.Message))
{
    var root = doc.RootElement;
    if (root.TryGetProperty("event", out var eventElement))
    {
        return eventElement.GetString() switch
        {
            "call"      => LineParser.CreateCall(logRecord),
            "request"   => LineParser.CreateRequest(logRecord),
            "internal"  => LineParser.CreateInternal(logRecord),
            _ => throw new FormatException(...)
        };
    }
    ...
}
```

也就是说，判断种类的语句是 `root.TryGetProperty("event", out var eventElement)` 配合 `eventElement.GetString() switch { "call" => ..., "request" => ..., "internal" => ... }`。

## 3. 确定种类后调用哪个库方法解析 JSON

确定种类后，在对应的工厂方法（如 `CreateCall` / `CreateRequest` / `CreateInternal`）中调用 `System.Text.Json` 提供的：

```csharp
JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options)
```

把 JSON 字符串反序列化成一个强类型的 `record`（如 `CallMessage`）。

### 3.1 如何防止日志中字段缺失

通过在反序列化目标 `record` 的每个属性上标注 `[property: JsonRequired]` 特性，例如：

```csharp
private record CallMessage(
    [property: JsonRequired] string Severity,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] string TargetService,
    [property: JsonRequired] int DurationMs
);
```

`[JsonRequired]` 告诉 JSON 序列化器这些属性是必需的：当 JSON 中缺少对应键时，`JsonSerializer.Deserialize` 会抛出 `JsonException`，从而把「字段缺失」这一异常情况暴露出来。此外，反序列化结果后还跟了一个 `?? throw new FormatException(...)`，用于在结果为 `null` 时也抛出异常，进一步兜底：

```csharp
var callMessage = JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options)
    ?? throw new FormatException(...);
```

### 3.2 如何让 JSON 解析器完成「烤串命名法 → 大驼峰命名法」的转换

通过配置 `JsonSerializerOptions` 的 `PropertyNamingPolicy`：

```csharp
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
```

`JsonNamingPolicy.KebabCaseLower` 作为命名策略，会在反序列化时把 C# 属性名（大驼峰，如 `RequestId`、`TargetService`、`DurationMs`）转换成小写烤串形式（`request-id`、`target-service`、`duration-ms`）再去和 JSON 中的键匹配。这样就在不修改 C# 属性名的前提下，完成了 `abc-def` 与 `AbcDef` 两种命名法之间的映射。

# (Q1.2)

以一个 Call 事件为例，调用 `KeyValueVisitor.Dump(entry)`（其中 `entry` 的静态类型是 `LogEntry`，实际运行时类型是 `CallLogEntry`）后，方法调用链如下（.NET 内置库方法略）：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
  - 内部执行 `return entry.Accept(this);`，由于 `entry` 的运行时类型是 `CallLogEntry`，发生多态分派，调用 `CallLogEntry` 中被 override 的 `Accept`
+ `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
  - 内部执行 `return visitor.Visit(this);`，此处 `this` 的编译时类型是 `CallLogEntry`，于是通过重载分派选中 `KeyValueVisitor.Visit(CallLogEntry entry)`
+ `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`
  - 构造并返回保存了 `LineNo`、`Timestamp`、`PodName`、`Severity`、`EventType`、`RequestId`、`TargetService`、`DurationMs` 的 `Dictionary<string, string>`

这里正是访问者模式的「双重分派（double dispatch）」：第一重由 `entry.Accept(this)` 按 `entry` 的**运行时类型**分派到 `CallLogEntry.Accept`；第二重由 `visitor.Visit(this)` 按 `this` 的**编译时类型**（`CallLogEntry`）分派到 `KeyValueVisitor.Visit(CallLogEntry)` 的重载，从而对外部屏蔽了具体子类，却仍能对每种日志执行不同的行为。

# (Q1.3.b)
根据TODO框架和guidance.md完成任务××.AI能给出达成任务要求的代码并自行测试验证。有时候AI会有过度、无效兜底的问题，在这次作业中基本没有出现