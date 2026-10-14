 # 01-basic 问答题报告

  ## Q1.1

  **(1)** CsvReader 自动按逗号拆解 CSV，LogRecordMap 里的 Index(0)-Index(3)
  指定了第 0 列到第 3 列分别对应 LineNo、Timestamp、PodName、Message。

  **(2)** 在 LineParser.ParseLine 方法中，先用 JsonDocument.Parse 解析 JSON，
  然后通过 root.TryGetProperty("event", ...) 取出 event 字段，
  再用 switch 表达式匹配 "call"、"request"、"internal" 来判断日志种类。

  **(3)** 使用 JsonSerializer.Deserialize<XxxMessage>() 解析 JSON。
  通过 [JsonRequired] 特性防止字段缺失，缺字段时自动抛 JsonException。
  通过 JsonNamingPolicy.KebabCaseLower 将烤串命名自动转换为大驼峰命名。
  ## Q1.2
    第一步： KeyValueVisitor.Dump(LogEntry entry) 被调用，里面只有一行：

  return entry.Accept(this);   // this 是 KeyValueVisitor 自己

  第二步： entry 实际是 CallLogEntry，所以走 CallLogEntry.Accept：

  return visitor.Visit(this);  // this 是 CallLogEntry，visitor 是
  KeyValueVisitor

  第三步： this 是 CallLogEntry，C# 自动匹配到
  KeyValueVisitor.Visit(CallLogEntry entry)，在里面构建字典并返回。
  ## Q1.3
  使用了GLM5.2帮助我理解JSON与C#的转换。