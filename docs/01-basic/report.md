# 第三讲：基础日志解析作业报告

姓名：谭翔之  
班级：无57班  
学号：不填写

## 已实现功能

- 完成 `request` 和 `internal` 两类日志的 JSON 解析，并保留错误日志的异常名称和异常信息。
- 为 `CallLogEntry`、`RequestLogEntry`、`InternalLogEntry` 实现访问者分派。
- 完成 `KeyValueVisitor.Dump` 以及三种日志对应的 `Visit` 方法。

## Q1.1

1. `LogFileParser.Parse` 使用 `ReadLine` 逐行读取日志，再把每行交给 CSV 解析逻辑；解析后的记录按 `lineno`、`timestamp`、`pod-name`、`message` 的顺序保存到 `LogRecord`，因此 `LineParser` 通过记录的各属性而不是模糊的字符串位置来使用字段。
2. `LineParser.ParseLine` 先解析 `message` 的 JSON，再读取 `event` 字段，根据 `event == "call"`、`event == "request"` 或 `event == "internal"` 选择对应的日志类型。
3. JSON 使用 `System.Text.Json.JsonSerializer.Deserialize<T>` 解析。各事件的专用字段通过可空属性和显式检查处理；字段缺失时不会悄悄生成一个有效结果，而是抛出格式错误，交由上层报告该行无效。
4. 烤串命名法到 C# 属性名的转换由 `JsonPropertyName` 特性完成，例如把 `request-id` 映射到 `RequestId`，把 `status-code` 映射到 `StatusCode`。

## Q1.2

以 Call 日志为例，主要调用链为：

1. `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`；
2. `entry.Accept(this)`；
3. `Dictionary<string, string> KeyValueVisitor.Visit(CallLogEntry entry)`；
4. `CallLogEntry` 的 `Accept` 实现调用 `visitor.Visit(this)`，由静态类型把分派落到 `Visit(CallLogEntry)`；
5. `Visit` 读取 `LineNo`、`Timestamp`、`PodName`、`Severity`、`EventType`、`RequestId`、`TargetService` 和 `DurationMs`，组装并返回字典。

## Q1.3.b（使用 AI）

我使用 AI 辅助阅读项目框架和测试，提示词要求它解释日志模型、JSON 字段映射、访问者模式，并给出 request/internal 解析和 `KeyValueVisitor` 的实现建议。AI 的优势是能快速整理多个文件之间的接口关系，并提醒检查缺失字段、烤串命名和多态分派；不足是初始建议可能忽略项目已有的异常类型和测试对错误输入的要求。因此我逐项对照 `guidance.md`、测试代码和实际构建结果，修改后再提交，未把未经验证的代码或测试结果当作结论。

## 验证

提交前应在 `src` 目录运行：

```shell
dotnet test test-01-basic/test-01-basic.csproj -c Release
```

并在 PR 中附上实际测试通过的终端或 IDE 截图。
