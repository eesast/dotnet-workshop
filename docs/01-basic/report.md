# 01-basic 实验报告

## (Q1.1)

以下分析均针对 `src/LogParser` 目录下的框架代码。

### 1. 按逗号分割日志、指定字段含义的语句

框架代码并没有手写 `Split(',')` 之类的语句，而是借助 **CsvHelper** 库来完成按逗号（默认分隔符）分割整行的工作。

在 `Parser/LogFileParser.cs` 的 `Parse` 方法中：

```csharp
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

`CsvReader.GetRecords<LogRecord>()` 会按 `CsvConfiguration` 的默认分隔符（`,`）将每一行拆分成字段，并填充到 `LogRecord` 对象中。

而“每一行的第几个字段代表何种意义”是通过 `LogRecordMap : ClassMap<LogRecord>` 中按列下标（`Index`）映射的方式指定的：

```csharp
Map(m => m.LineNo).Index(0);    // 第 0 列 -> LineNo
Map(m => m.Timestamp).Index(1); // 第 1 列 -> Timestamp
Map(m => m.PodName).Index(2);   // 第 2 列 -> PodName
Map(m => m.Message).Index(3);   // 第 3 列 -> Message
```

### 2. 判断日志种类（Call / Request / Internal）的方法与语句

在 `Parser/LineParser.cs` 的 `ParseLine(LogRecord logRecord)` 方法中：

```csharp
using (var doc = JsonDocument.Parse(logRecord.Message))
{
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
    ...
}
```

即：

+ 先用 `JsonDocument.Parse(logRecord.Message)` 把 `message` 字段（JSON 字符串）解析为 JSON 文档；
+ 再用 `root.TryGetProperty("event", out var eventElement)` 取出名为 `event` 的字段；
+ 最后用 `eventElement.GetString()` 得到的字符串配合 `switch` 表达式分发到对应的创建方法（`CreateCall` / `CreateRequest` / `CreateInternal`）。

### 3. 解析 JSON 的库方法

在确定了日志种类后，框架使用 **`System.Text.Json`** 中的 `JsonSerializer.Deserialize<T>(...)` 方法进行强类型解析，例如：

```csharp
var callMessage = JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options)
    ?? throw new FormatException(...);
```

#### 3.1 防止字段缺失

框架在 message 的内部 record 的每个必填属性上标注了 `[property: JsonRequired]`，例如：

```csharp
private record CallMessage(
    [property: JsonRequired] string Severity,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] string TargetService,
    [property: JsonRequired] int DurationMs
);
```

当 JSON 中缺少被标记为 `JsonRequired` 的字段（例如 Call 日志的 `message` 中缺失 `duration-ms`）时，`JsonSerializer.Deserialize<T>` 会抛出 `JsonException`，从而防止静默地把字段当默认值使用。

#### 3.2 kebab-case 到 PascalCase 的命名转换

框架在 `LineParser` 中定义了静态的 `JsonSerializerOptions`：

```csharp
private static JsonSerializerOptions options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
};
```

`JsonNamingPolicy.KebabCaseLower` 会把 JSON 中的烤串命名（kebab-case）键（如 `request-id`、`target-service`、`duration-ms`）自动转换为大驼峰（PascalCase）的 C# 属性名（如 `RequestId`、`TargetService`、`DurationMs`），从而在反序列化时自动完成命名法的相互映射。

## (Q1.2)

以一个 Call 事件的解析结果为例，调用 `KeyValueVisitor.Dump` 后，完整的方法调用链为：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`（对 `LogEntry` 上抽象方法 `Accept` 的动态绑定实现）
+ `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

调用过程说明：

1. 用户调用 `Dump(entry)`；
2. `Dump` 内部执行 `return entry.Accept(this);`，由于运行时的实际类型是 `CallLogEntry`，会调用 `CallLogEntry.Accept` 的重写实现；
3. `CallLogEntry.Accept` 内部执行 `return visitor.Visit(this);`，其中 `visitor` 就是 `KeyValueVisitor` 实例，`this` 的静态类型为 `CallLogEntry`，因此重载决议调用 `KeyValueVisitor.Visit(CallLogEntry entry)`，完成 Call 类型日志的键值对提取。

## (Q1.3.b)

本次作业使用了 AI（Cline 编码助手）辅助完成。

### 我给予 AI 的提示词

我向 AI 提供了如下任务描述：

> 阅读本仓库 `docs/00-prepare` 与 `docs/01-basic` 的说明文档，以及 `src/test-01-basic` 下的测试代码与 `src/TestUtils` 中的样例数据；随后在 `src/LogParser` 中补齐所有 `TODO` 标记的实现（`LogEntries.cs` 中 `Accept` 方法、`LineParser.cs` 中 request/internal 的解析、`KeyValueVisitor.cs` 中 Request/Internal 的 `Visit` 方法），使得 `dotnet test test-01-basic -c Release` 全部通过，并为 Q1.1–Q1.3 撰写 `docs/01-basic/report.md`。

### AI 的解答比传统搜索引擎 + 自己写的解答好在哪里

+ **定位精准、效率高**：AI 通过直接搜索 `TODO` 标记和通读测试文件，一次就锁定了所有需要修改的文件与方法，省去了人工逐篇阅读文档、试错的时间。
+ **能综合“测试用例 + 样例数据”反向推导精确格式**：例如 `Timestamp` 必须用 `ToString("O")` 输出、Internal 日志的 `exception` 字段需要按第一个冒号拆分为 `ExceptionName` 与 `ExceptionMessage`，这些细节仅凭文档难以确定，但 AI 能从 `TestUtilsClass` 的样例与 `Test_1_3` 的断言中推导出来并直接落地。
+ **产出与既有代码风格一致、可直接运行**：AI 生成的代码复用了框架已有的 `JsonSerializerOptions`、`[property: JsonRequired]`、`ParseSeverity` 等既有设施，并且会主动运行 `dotnet build` / `dotnet test` 进行验证，保证结果可用。

### AI 的解答存在的问题或不如自己写的地方

+ **容易“只对齐测试、不解释意图”**：例如为什么 internal 的 exception 拆分要取第一个冒号而非最后一个、为什么要用访问者模式而不是 `switch` 类型判断，AI 只会按测试通过为目标来写，如果不追问，它不会主动解释设计动机。
+ **在缺乏测试约束的细节上可能凭猜测**：例如 `ExceptionName` / `ExceptionMessage` 是否需要 `Trim()`、异常类型用 `FormatException` 还是自定义异常等，AI 的默认选择不一定是最贴合项目约定的，需要人工审查其是否符合题目本意。
+ **生成结果仍需人工验证与理解**：AI 给出的代码只是“能通过测试”的充分条件，而非“符合设计意图”的充分条件。若不阅读、不理解就照抄，本次作业就失去了训练目的。因此我对照 guidance 中的“简单工厂模式 / 访问者模式”章节逐行核对了最终实现。

综上，AI 极大地提升了定位与落地的效率，但最终对代码语义与设计模式的理解、以及对生成结果的审查仍然需要由我本人完成。
