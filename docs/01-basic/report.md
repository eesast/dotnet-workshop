# 01-basic 问答题报告

## (Q1.1) 代码框架分析

### 1. 哪条语句将日志按逗号分割？如何指定第几个字段代表何种意义？

`LogFileParser.cs` 中的这一句把每行日志按逗号分割：

```csharp
var fields = line.Split(',');
```

分割后按**位置约定**对应字段意义：`fields[0]` 是 `lineno`、`fields[1]` 是 `timestamp`、`fields[2]` 是 `pod-name`、`fields[3]` 是 `message`，随后用它们构造 `LogRecord`：

```csharp
var logRecord = new LogRecord(
    LineNo: int.Parse(fields[0]),
    Timestamp: fields[1],
    PodName: fields[2],
    Message: Unescape(fields[3])   // message 内的 CSV 引号转义还原
);
```

也就是说，字段含义不是写在数据里的，而是框架代码里"位置 → 属性名"的固定映射。

### 2. 在哪个方法内判断日志种类？用哪几条语句？

在 `LineParser.ParseLine` 方法内。先用 `JsonDocument.Parse` 把 `message` 解析成只读 DOM，再用 `TryGetProperty("event", ...)` 取出 `event` 字段，然后用 `switch` 表达式按其值分派到 `CreateCall` / `CreateRequest` / `CreateInternal`：

```csharp
using (var doc = JsonDocument.Parse(logRecord.Message))
{
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
}
```

### 3. 确定日志种类后，调用了哪个库方法解析 JSON？

调用 `System.Text.Json.JsonSerializer.Deserialize<T>(json, options)`，把 message 反序列化为强类型的 `CallMessage` / `RequestMessage` / `InternalMessage` record。

**如何防止字段缺失？** 三个层面：

1. record 的属性标注了 `[property: JsonRequired]`——缺失该键时 `Deserialize` 会抛 `JsonException`（测试 T1.2.5/T1.2.6 正是验证这一点）；
2. record 属性是非可空类型（如 `int DurationMs`），值为 `null` 时同样失败；
3. 反序列化结果用 `?? throw new FormatException(...)` 兜底，防止整个 message 不是合法 JSON 对象时返回 `null`。

**命名法转换（kebab-case → 大驼峰）** 是通过共享的序列化选项完成的：

```csharp
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
```

`PropertyNamingPolicy = KebabCaseLower` 告诉序列化器：CLR 属性 `RequestId` 对应 JSON 键 `request-id`、`TargetService` 对应 `target-service`，反序列化时即完成双向映射。

## (Q1.2) Dump 的方法调用链

以 Call 事件为例，调用 `KeyValueVisitor.Dump(entry)` 后：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult LogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)` —— 实际派发到 `CallLogEntry.Accept<TResult>`
+ `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`
+ `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

关键点：`Dump` 内只有一句 `entry.Accept(this)`。静态类型是 `LogEntry`，但 `Accept` 是虚方法（record 的 override），实际执行的是**运行时具体类型** `CallLogEntry.Accept`，它再回调 `visitor.Visit(this)`，此时 `this` 的静态类型已经是 `CallLogEntry`，于是重载决议选中 `Visit(CallLogEntry)` 重载。这就是访问者模式的"**双重分派**"：第一次分派由 `Accept` 的虚机制完成（选具体 entry 类型），第二次由 `Visit` 的重载决议完成（选具体访问逻辑），数据结构与操作就此解耦。

## (Q1.3.b) AI 使用情况

本次作业我使用了 AI 辅助。

**提示词要点**：我向 AI 提供了仓库中 `LineParser.cs`、`LogEntries.cs`、`KeyValueVisitor.cs` 的完整源码与任务文档节选，要求"仿照 Call 类型的既有实现，补全 Request / Internal 两种日志的解析、Accept 与 Visit 实现，保持与 Call 完全一致的代码风格（JsonRequired record + switch 分派 + 同样的异常消息格式），不要改动无关代码"。

**AI 解答比我强的地方**：

1. **速度**。三种类型的解析在结构上是同构的，AI 数秒内就给出了与参考实现风格一致的完整代码；若我自己查 `System.Text.Json` 文档里的 `JsonNamingPolicy` 枚举、`JsonRequired` 特性的确切用法，至少多花半小时。
2. **API 记忆的准确性**。`JsonNamingPolicy.KebabCaseLower`、`[property: JsonRequired]` 这类 API 名字手写容易拼错或用成旧 API（如 ` camelCase` 策略），AI 一次给对。

**AI 不如我的地方**：

1. **契约细节**。AI 初版对 Internal 日志的 `ExceptionName: message` 切分用了 `Split(':')`，没有意识到异常信息本身可能含冒号——正确做法是 `IndexOf(": ")` 只切第一个分隔符。这个 bug 是我对照测试样例 `InternalLogExampleFailed`（缺失冒号+空格的样例必须抛异常）时发现并改正的。
2. **上下文一致性**。AI 生成的字典键（如 `"DurationMs"`）需要人工逐一对照讲义里的键表核对，它并不会主动去校验"文档约定了哪些键"这类跨文件契约。

结论：AI 适合干"同构复制 + API 检索"的活，而"跨文档契约核对 + 边界情况（分隔符歧义、失败样例）"仍必须靠人。
