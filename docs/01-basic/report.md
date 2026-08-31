 01-basic 问答题
Q1.1
1. 分割与映射：使用 `CsvHelper` 库的 `LogRecordMap` 类，通过 `Map(m => m.LineNo).Index(0)` 指定第 0 列为行号，第 1 列时间戳，第 2 列 Pod 名，第 3 列为 Message。

2. 类型判断：在 `LineParser.ParseLine` 方法中，通过 `JsonDocument.Parse` 解析 `Message`，再用 `TryGetProperty("event", out var eventElement)` 获取事件字段，最后用 `switch` 匹配 `"call"`、`"request"`、`"internal"`。

3. JSON 解析方法：调用 `System.Text.Json.JsonSerializer.Deserialize<T>()` 将 JSON 转为强类型对象。

4. 防止字段缺失：在 `CallMessage`、`RequestMessage` 等 record 的属性上标注 `[JsonRequired]`，确保字段存在。

5. 命名转换：通过 `JsonSerializerOptions` 设置 `PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower`，自动映射 `RequestId` ↔ `request-id`。

Q1.2

调用链：
1. `KeyValueVisitor.Dump(LogEntry entry)`
2. `CallLogEntry.Accept<TResult>(ILogEntryVisitor<TResult> visitor)`（多态匹配实际类型）
3. `KeyValueVisitor.Visit(CallLogEntry entry)`

Q1.3.b
交互过程：提供了题目和代码文件，要求进行逐行讲解和代码实现指导，并在环境配置（WSL 安装 .NET 10 SDK）上获得了操作命令帮助。
AI 优点：AI 相比我自行查阅文档，AI 能结合具体任务提供更加定制化方案，节省了寻找合适 API 的时间。同时对代码可以有更详细地讲解，降低了我的理解门槛。
AI不足：存在幻觉没看到的文档/内容也会自己生成。