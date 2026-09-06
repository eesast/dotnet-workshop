# 实验报告

## 运行

分别在 `src\LogAnalyzerAgent` 和 `src\LogAnalyzerClient\LogAnalyzerClient.Desktop` 目录运行：

```bash
dotnet run
```

## 功能

实现了以下三项功能：

### 1. Parquet 文件的读写

现在在分析文件时同时支持 `.log` 和 `.parquet` 文件：

- 对于 `.log` 文件，行为保持不变
- 对于 `.parquet` 文件，使用新添加的解析器解析，二者解析结果以相同格式返回

同时增加对于单个分析成功的日志转存为 `.parquet` 文件的功能：

- 允许用户自定义转存的目标目录
- 文件名默认为原文件名更换后缀 `.parquet`
- 前端用户可通过右键对应文件进行保存

上述两种功能的 `.parquet` 文件格式完全相同，如下所示：

```text
required int32 LineNo;
required int64 Timestamp (TIMESTAMP[MICROS]);
required binary PodName (UTF8);
required binary Severity (UTF8);
required binary EventType (UTF8);
optional binary RequestId (UTF8);
optional binary TargetService (UTF8);
optional int32 DurationMs;
optional binary Method (UTF8);
optional binary Path (UTF8);
optional int32 StatusCode;
optional binary ExceptionName (UTF8);
optional binary ExceptionMessage (UTF8);
```

### 2. 日志分析结果的表格显示

- 对于成功的分析结果，现在会以表格形式呈现，并且自动按照日志严重等级分配颜色
- 失败结果另外弹出窗口
- 对于该行不适用的字段，目前将默认留空

### 3. 简化的 token 验证

- 每个请求需要在请求头中携带 token，请求会根据 token 转发到不同的 LogAnalyzer 实例，实现操作互不干扰
- 不携带 token 或 token 非法的请求将被拒绝
- 通过修改 LogAnalyzerAgent 的 `appsettings.json` 文件中的 `AgentAuth:Tokens` 字段，可配置允许的 token（需要重启 Agent 才能生效）

```json
"AgentAuth": {
  "Tokens": [
    "token-a",
    "token-b"
  ]
}
```

UI 方面，在 connect 页面增加了 token 输入框，其余操作不变。

> 以上功能均只对 Desktop UI 进行了完善的适配。

## AI 使用

1. 使用了 Copilot 用于补全样板代码
2. 使用了 opencode 用于辅助查找 API 及用例、完善 UI、检查代码潜在漏洞、完善报告格式

以上使用帮助我寻找了更简洁的代码编写方法，以及减少了 debug 时间。
