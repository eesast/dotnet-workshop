# 问答题报告

### (Q1.1)

在给出的代码框架 `Parser` 中：

* 哪条语句或哪几条语句将日志按逗号进行分割？代码中，我们是如何指定每一行的第几个字段代表何种意义的？
> using var csv = new CsvReader(logFile, config);配合循环使用的csv.GetRecords<LogRecord>()；
>在LogRecordMap中一一指定对应

* 在对日志中 JSON 格式的 `message` 字段进行读取时，我们是在哪个方法内用哪几条语句判断这一行日志的种类（Call / Request / Internal）的？
> LineParser 类的 ParseLine 方法，if(root.TryGetProperty("event", out var eventElement))语句，再用swith判断

* 在确定了日志种类后，我们是调用了哪个库方法对 JSON 进行解析的？
> System.Text.Json 库中的 JsonSerializer.Deserialize<T>(...) 方法

* 进一步，我们的框架代码是如何防止日志中有字段缺失的？（例如所给的 Call 日志的 `message` 中缺失 `request_id` 字段）
> 在每个属性前面都强制加上了 [property: JsonRequired] 特性标签

* 更进一步，日志中的 JSON 的键是 `abc-def` 命名法（称为烤串命名法），而我们的解析结果却是放在 `AbcDef` 命名法（称为大驼峰命名法）的属性里，我们的框架代码中是如何告诉 JSON 解析器完成这一命名法转换的？
> 框架代码事先创建了一个名为 options 的配置变量，并在其中设置了 JsonNamingPolicy.KebabCaseLower 这一规则。在调用 JsonSerializer.Deserialize 提取数据时，代码将这个 options 作为参数交给了解析工具。

---

### (Q1.2)

以一个 Call 事件的解析结果为例，当调用 `KeyValueVisitor` 的 `Dump` 方法后，都有哪些方法被调用？请补充完整如下的方法调用链（.NET 内置库无需写出）：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ > TResult CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)
+ > Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)
 

---

### (Q1.3)

本次作业中，你是否使用了 AI？根据你的使用情况，在以下 (Q1.3.a) (Q1.3.b) 两个问题中选择一题作答：


#### (Q1.3.b) 
如果使用了 AI，你给予 AI 的提示词是什么？你认为 AI 给出的解答、你完全凭借传统搜索引擎以及自己的能力能够写出的解答之间，AI 的解答比你好在哪？AI 又有哪些解答是存在问题的，或者至少是不如你自己的解答的？给出你的理由。
+ > 提示词上，在完成代码部分，我只是把ai当做搜索引擎使用，去解释一些我看不懂的C#内置函数，然后再用自己能力补充代码，不过在q1.1中第1，4，5个问题，我确实完全不了解c#内置的库是什么，让ai先给出了解答再自己借助ai去了解；
+ > 代码中，ai的解答和我几乎相同；在问答题中，ai的解答比我更具有专业性，对整个项目的把握比我更透彻
+ > 我让只让ai给出了q1.1中1，4，5题超出我能力范围之外的解答，这些解答核实没发现什么大问题，其他的ai解答和我大致相同，我暂时未发现ai明显不如自己解答的情况。