# Task 1 问答作业报告

## (Q1.1)

1. **按逗号分割与字段指定**
   - **分割**：在 `LineParser.cs` 中，通过 `line.Record.Split(',', 4)` 将日志按逗号分割为 4 个部分。
   - **意义**：代码直接通过数组索引（`parts[0]` 代表行号 LineNo，`parts[1]` 代表时间戳 Timestamp，`parts[2]` 代表 Pod 名称，`parts[3]` 代表 JSON 消息 Body）来定位字段意义。

2. **判断日志种类**
   - **位置与语句**：在 `LineParser.ParseLine` 方法中，先通过 `JsonDocument.Parse(jsonString)` 解析 JSON 字符串，再通过 `root.GetProperty("event").GetString()` 获取 `event` 字段的值（如 `call`、`request`、`internal`），并用 `switch` 分支判断日志种类。

3. **JSON 解析与格式转换**
   - **调用的库方法**：使用了 .NET 标准库 `System.Text.Json` 中的 `JsonSerializer.Deserialize<T>(...)`。
   - **防止字段缺失**：在消息接收模型的属性上添加了 `[JsonRequired]` 特性，如果 JSON 中缺失对应的必填字段，反序列化时将自动抛出异常。
   - **命名法转换（kebab-case 转换为 PascalCase）**：在属性上添加 `[JsonPropertyName("abc-def")]` 特性显式指定 JSON 键名，或在 `JsonSerializerOptions` 中设置 `PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower`。

## (Q1.2)

调用 `KeyValueVisitor` 的 `Dump` 方法时，对于 `Call` 事件的方法调用链如下：

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult LogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`（实际运行时动态派发调用 `CallLogEntry.Accept`）
+ `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`

## (Q1.3.b)

+ **给予 AI 的提示词**：报错信息，请ai分析问题
+ **AI 的优势**：AI 能快速排查并指出 `JsonRequired` 大小写拼写错误等语法细节
+ **AI 的问题**：AI 偶尔会写出语法缺失的代码，会缺少部分语句，需要我自己检查。