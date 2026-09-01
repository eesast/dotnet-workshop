# 04-avalonia 报告

## 功能实现说明（T4.1）

全部功能在 `MainViewModel` 中以 `[RelayCommand]` + `WithClientNotNull` 包装实现（未连接 Agent / RPC 异常统一弹 `MessageDialog`，绝不让 UI 崩溃）：

| 功能 | 实现 |
|---|---|
| Refresh | `GetLogFilesAsync` → 清空并回填 `LogFiles` 集合 |
| Analyze Selected | 校验 `SelectedFiles` 非空、并行度可解析 → `AnalyzeFilesAsync(FileNames=SelectedFiles)` |
| Analyze All（新增 All 按钮） | XAML 在 Selected 右侧加 `Grid.Column=4` 的 `All` 按钮，绑定 `AnalyzeAllCommand` → `AnalyzeAllAsync` |
| 右键 Analyze File | 用 `SelectedLogFile`（`OneWayToSource` 绑定）单文件 `AnalyzeFilesAsync` |
| 右键 View Analysis Results | server-streaming 消费：Header 按状态处理（Failed → 一条含 `ErrorMessage` 的 `LogFields`；NotAnalyzed → 提示行；Succeeded → 跳过等条目）；每个 `LogEntry` 经 `ConvertFromGrpc` + `KeyValueVisitor.Dump` 转成 `LogFieldItem` 列表 |
| 结果显示 `LogFields.Summary` | `[行号] K=V, K=V, ...`；错误时 `[行号] Error: 消息` |

分析完成后自动 `RefreshAsync()` 同步列表状态。

### 验证

- `LogAnalyzerClient.Desktop` Release 构建 0 错误；
- 桌面客户端可正常启动（无运行时崩溃）；
- 与 03 节同一 Agent 联调：连接 `localhost:5000` → 换目录 → 刷新列表 → 分析 → 查看结果的完整链路代码路径与 RemoteCli 完全一致（复用同一 gRPC 协议）。

运行截图与鲁棒性截图见 `screenshots/`（GUI 截图需在本机运行后手动补拍：包括未连接时点分析、非法并行度、分析失败文件、未分析文件四种情况）。

## 问答题

### (Q4.1) MVVM 的理解

见下方问答题原文的回答——MVVM 把界面拆成 Model（数据：`LogFileItem`/`LogFields`）、View（纯声明式 XAML：`MainView.axaml`）、ViewModel（状态+命令：`MainViewModel`）。View 通过数据绑定只读 ViewModel 的属性、调用其命令；ViewModel 不持有任何控件引用，只改ObservableProperty`（自动触发 `PropertyChanged` 通知界面刷新）。

相比直接在 code-behind 写事件处理的优势，在本节体现得最明显的三点：

1. **可测试性**：`MainViewModel` 的全部逻辑不依赖 UI 线程和真实控件，`DialogHelper`/`IClientFactory` 都是可替换的接口（框架给了 `NullDialogHelper`/`NullClientFactory` 空实现），未来可以注入 mock 做单元测试。
2. **跨平台**：同一 `LogAnalyzerClient` 项目承载全部逻辑，Desktop/Browser 只是不同的壳（`Program.cs` 里替换 ClientFactory 即可），业务代码零改动。
3. **命令可用性**：`[RelayCommand]` 生成的 `IAsyncRelayCommand` 自带 `IsRunning`/`CanExecute`，天然支持按钮禁用与防重复点击，不需要在事件处理器里手工管理状态。

### (Q4.2.b) AI 使用情况

**提示词**：提供了 MainViewModel/MainView.axaml 框架源码与功能清单，要求"按 CommunityToolkit.Mvvm 风格补全五个命令，错误一律走 DialogHelper 弹窗，流式结果复用 GrpcTypeConverter+KeyValueVisitor"。

**使用方式**：让 AI 写命令主体，我负责核对 XAML 绑定名与 ViewModel 属性名的一致性（如 `AnalyzeSelectedFilesCommand` 的自动命名规则）。

**AI 的错误/不足**：AI 在 Header 处理上曾把 Succeeded 分支也生成一条占位 `LogFields`，导致结果列表多一行空数据——按"Header 只标记状态、条目随后到达"的流语义删掉。另外 All 按钮的 `Grid.Column` 它起先没给（框架 Grid 是固定列定义），补上 `Grid.Column="4"`。

**新学到的知识**：① `[ObservableProperty]`/`[RelayCommand]` 源码生成器的约定（partial 类、私有字段命名）；② Avalonia XAML 的 `OneWayToSource` 绑定方向（控件→VM，用于 SelectedItem）；③ record + 计算属性 `Summary` 作为 DataTemplate 显示源的轻量做法。

**难度评价**：四节中最贴近"真实产品开发"的一节，代码不难但接口面广（绑定、命令、对话框、流式 gRPC 全要接起来）。
