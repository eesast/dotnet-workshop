# 报告

## Q1.1 的回答

+ 哪条语句或哪几条语句将日志按逗号进行分割？代码中，我们是如何指定每一行的第几个字段代表何种意义的？
  - "分割"的实现位于 `LogFileParser.cs` 的 38 行 `foreach (var logRecord in csv.GetRecords<LogRecord>())` 中实现，该行调用了 csv 库的 `GetRecords` 方法，将日志转化为我们所需要的 `LogRecord` 类。
  - 我们通过该文件 20-23 行

    ```csharp
    Map(m => m.LineNo).Index(0);
    Map(m => m.Timestamp).Index(1);
    Map(m => m.PodName).Index(2);
    Map(m => m.Message).Index(3);
    ```

    确定了：第一个字段是 `LineNo`，第二个字段是 `Timestamp`，以此类推。

+ 在对日志中 JSON 格式的 `message` 字段进行读取时，我们是在哪个方法内用哪几条语句判断这一行日志的种类（Call / Request / Internal）的？
  - 我们在 `LineParser.cs` 的 `ParseLine` 方法中判断种类，具体来说，我们试图通过

    ```csharp
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
    ```

    检测传入 `LogRecord` 对象的 `message` 属性的 `event` 字段是三者中的哪一个，从而进行判断。

+ 在确定了日志种类后，我们是调用了哪个库方法对 JSON 进行解析的？
  + 进一步，我们的框架代码是如何防止日志中有字段缺失的？（例如所给的 Call 日志的 `message` 中缺失 `request_id` 字段）
  + 更进一步，日志中的 JSON 的键是 `abc-def` 命名法（称为烤串命名法），而我们的解析结果却是放在 `AbcDef` 命名法（称为大驼峰命名法）的属性里，我们的框架代码中是如何告诉 JSON 解析器完成这一命名法转换的？
  - 我们调用了 `JsonSerializer` 的 `Deserialize` 方法解析 JSON。
  - 我们首先通过 `[property: JsonRequired]` 来确保：如果缺失字段，则抛出 `JsonException` 异常。此外，我们还通过 `??` 运算符检测 `JsonSerializer.Deserialize` 方法返回值是否为 `null`，如果在某些情况下该方法返回了 `null`，则抛出 `FormatException` 异常。
  - 我们在 `LineParser.cs` 的 31-34 行将 `options` 设为具有 `PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower` 的 `JsonSerializerOptions`，并在 `Deserialize` 时传入 `options` 参数，从而完成了命名法转换。

## Q1.2 的回答

+ `Dictionary<string, string> KeyValueVisitor.Dump(LogEntry entry)`
+ `TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor);`
+ `Dictionary<string, string> Visit(CallLogEntry entry)`

## Q1.3 的回答

+ 本次作业中，你是否使用了 AI？
  - 我使用了 AI。

### Q1.3.b 的回答

+ 如果使用了 AI，你给予 AI 的提示词是什么？你认为 AI 给出的解答、你完全凭借传统搜索引擎以及自己的能力能够写出的解答之间，AI 的解答比你好在哪？AI 又有哪些解答是存在问题的，或者至少是不如你自己的解答的？给出你的理由。
  - 我主要使用的是 VS Code Copilot 自带的代码补全 AI，故没有给出提示词。
  - AI 的解答相比于自己给出的解答更为安全，且可读性更好，例如：`LineParser.cs` 的 72-75 这几行就是 AI 补充的，防止出现没有冒号的情况。
  - AI 仅仅看到了 `InternalMessage` 上文的两个格式就开始了 `private record InternalMessage` 的编写，但是它没有考虑到原始文本的 `ExceptionName` 和 `ExceptionMessage` 并不是由 JSON 解析给出的，我在测试未通过后，通过检查解决了该问题。
  - 本报告完全由我所写，但是由于我对于markdown的格式并不熟悉，所以我让AI调了一下格式
