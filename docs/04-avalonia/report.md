# T4.1 Avalonia 图形界面客户端实现报告

## 实现的功能

本次任务完成了 `LogAnalyzerClient` 中所有标记为 `TODO: T4.1` 的功能：

1. **刷新日志文件列表**：`RefreshAsync` 异步调用 `GetLogFiles` RPC，并使用返回的文件名更新 `LogFiles`。如果 Agent 返回失败状态，则通过消息框提示错误。
2. **分析选中的多个文件**：`AnalyzeSelectedFilesAsync` 将 `SelectedFiles` 传入 `AnalyzeFiles` RPC，支持通过 Ctrl 键多选文件。在请求前会校验并行度是否为非负整数，并检查是否至少选择了一个文件。
3. **分析全部文件**：在界面中新增 `All` 按钮并绑定 `AnalyzeAllCommand`。`AnalyzeAllAsync` 会校验并行度，然后异步调用 `AnalyzeAll` RPC。
4. **分析右键选中的文件**：`AnalyzeRightClickedFileAsync` 使用 `SelectedLogFile` 调用 `AnalyzeFiles` RPC，从而实现右键菜单中的单文件分析功能。
5. **查看分析结果**：`GetAnalysisResultAsync` 异步读取 `GetAnalysisResult` 的服务端流，分别处理分析成功、分析失败和尚未分析等状态，并将每条日志转换为键值字段后写入 `ResultEntries`。
6. **格式化结果显示**：`LogFields.Summary` 会将结果格式化为“序号 | 字段名: 字段值”的形式；文件信息、失败原因和尚未分析提示则作为独立消息显示。
7. **异常与非法输入处理**：所有新增的 RPC 操作均通过统一的客户端检查和异常处理执行。未连接 Agent、RPC 失败、非法并行度、未选择文件及异常响应均通过消息框告知用户，避免 GUI 程序崩溃。

所有 gRPC 请求均采用异步调用并进行 `await`，避免阻塞 Avalonia UI 线程。

## 功能截图

### 完整界面

客户端成功连接到 Agent，界面包含日志目录操作、刷新按钮、并行度输入框、`Selected` 和 `All` 分析按钮，以及分析结果列表。

![客户端完整界面](./assets/image-1.png)

### 分析失败的日志文件

对于内容不符合日志格式的文件，客户端能够显示 Agent 返回的具体解析失败原因。

![日志分析失败结果](./assets/image-fail.png)

### 显示多条日志的分析结果

分析结果中显示文件名、工作线程编号，并按序号展示每条日志的全部字段。

![多条日志分析结果](./assets/image-multiple.png)

### 显示普通日志文件的分析结果

客户端能够正确显示包含 Call、Request 和 Internal 等不同事件类型的日志字段。

![普通日志分析结果](./assets/image-basic.png)

## 鲁棒性测试

### 并行度为负数

输入 `-1` 时，请求不会发送到 Agent，客户端弹出消息框提示并行度必须为非负整数。

![负数并行度测试](./assets/image-minus1.png)

### 并行度不是整数

输入 `null` 等非整数文本时，客户端能够识别非法输入并显示错误信息。

![非整数并行度测试](./assets/image-null.png)

### Agent 地址不可用

当目标地址或端口没有 Agent 监听时，连接异常会被捕获，并通过消息框显示连接失败信息，程序不会崩溃。

![Agent 不可用测试](./assets/image-wrongport.png)
