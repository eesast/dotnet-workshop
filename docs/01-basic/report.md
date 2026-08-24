# 01-basic 作业报告

## Q1.1

### Q1.1.1：哪条语句或哪几条语句将日志按逗号进行分割？代码中，我们是如何指定每一行的第几个字段代表何种意义的？

(1)`LogFileParser.Parse(TextReader logFile)` 中，`using var csv = new CsvReader(logFile, config);` 创建 CsvHelper 的 CSV 读取器，`csv.GetRecords<LogRecord>()` 逐行读取并按 CSV 规则分列为 `LogRecord`。

(2)`LogRecordMap` 的构造方法使用 `Map(m => m.LineNo).Index(0)`、`Map(m => m.Timestamp).Index(1)`、`Map(m => m.PodName).Index(2)`、`Map(m => m.Message).Index(3)`。因此第 0、1、2、3 列依次表示行号、时间戳、Pod 名和 JSON message。

### Q1.1.2：在对日志中 JSON 格式的 message 字段进行读取时，我们是在哪个方法内用哪几条语句判断这一行日志的种类（Call / Request / Internal）的？

`LineParser.ParseLine(LogRecord logRecord)` 中，先以 `JsonDocument.Parse(logRecord.Message)` 取得 JSON，再用 `root.TryGetProperty("event", out var eventElement)` 读取 `event`，最后用 `eventElement.GetString() switch` 分别选择 `CreateCall`、`CreateRequest`、`CreateInternal`。

### Q1.1.3：在确定了日志种类后，我们是调用了哪个库方法对 JSON 进行解析的？进一步，我们的框架代码是如何防止日志中有字段缺失的？（例如所给的 Call 日志的 message 中缺失 request_id 字段）更进一步，日志中的 JSON 的键是 abc-def 命名法（称为烤串命名法），而我们的解析结果却是放在 AbcDef 命名法（称为大驼峰命名法）的属性里，我们的框架代码中是如何告诉 JSON 解析器完成这一命名法转换的？

(1)`CreateCall`、`CreateRequest`、`CreateInternal` 分别调用 `JsonSerializer.Deserialize<CallMessage>()`、 `JsonSerializer.Deserialize<RequestMessage>()`、 `JsonSerializer.Deserialize<InternalMessage>()`。

(2)`LineParser` 类末尾的三个 Message record 将必填属性标注为 `[property: JsonRequired]`；缺少相应属性时 `JsonSerializer.Deserialize` 会抛出 `JsonException`。

(3)`LineParser` 类中的 `options` 字段设置了 `PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower`，因此 JSON 的 `request-id`、`status-code` 可映射至 C# 的 `RequestId`、`StatusCode`。

## Q1.2：以一个 Call 事件的解析结果为例，当调用 KeyValueVisitor 的 Dump 方法后，都有哪些方法被调用？请补充完整如下的方法调用链.

以 Call 类型日志为例，完整调用链为：
1. `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`  
2. `TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`  
3. `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`  

第 1 步传入的是运行时类型为 `CallLogEntry` 的对象；第 2 步的
`visitor.Visit(this)` 因而调用第 3 步的 Call 重载并返回字典。

## Q1.3 b：如果使用了 AI，你给予 AI 的提示词是什么？你认为 AI 给出的解答、你完全凭借传统搜索引擎以及自己的能力能够写出的解答之间，AI 的解答比你好在哪？AI 又有哪些解答是存在问题的，或者至少是不如你自己的解答的？给出你的理由。

(1)提示词：请根据本讲讲义内容以及作业题干，提取相关代码文件并将相关知识点标注在代码的注释中，并给出几个报告问题的讲解。（大意，经过多次迭代；代码暂未利用AI）

(2)好处：能够帮助找到讲义中未讲解清楚或未涉及的知识点，理清回答思路，更清晰地掌握相应知识点。

(3)问题：在问答轮数较少时容易偏离问题本身或采用不符合要求的方法。