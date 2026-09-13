# 第二讲：多线程日志分析作业报告

姓名：谭翔之  
班级：无57班  
学号：不填写

## 已实现功能

- `WorkQueue<T>` 使用 `lock` 保护队列和完成标志，并使用 `Monitor.Wait`、`Pulse`、`PulseAll` 实现生产者—消费者同步；消费者用 `while` 重新检查条件，避免虚假唤醒。
- `LogFileAnalyzer` 支持扫描目录、按并行度启动工作线程、跳过已经成功或失败的文件、保存成功结果及失败信息，并保护共享状态。
- `LocalCli` 支持输入目录、列出日志、分析指定文件、分析全部文件、查看结果和更换目录；非法目录、文件名、选项和并行分析状态都会给出提示，不会直接崩溃。

## Q2.1

`WorkQueue<T>` 的共享变量是 `_items` 和 `_isCompleted`。所有读写都在对 `_items` 加锁的临界区中进行；等待使用 `Monitor.Wait`，入队使用 `Monitor.Pulse`，完成添加时使用 `Monitor.PulseAll` 唤醒全部消费者。

`LogFileAnalyzer` 的共享变量包括 `_currentDirectory`、`_isAnalyzing`、`_logFiles` 和 `_analysisResults`。这些变量由 `_syncRoot` 保护；文件解析在工作线程中进行，写回 `_analysisResults` 时重新加锁。

如果把条件判断从 `while` 改成 `if`，线程可能因虚假唤醒而在队列仍为空时继续执行 `Dequeue`，导致异常或错误结果。使用 `while` 可以在线程被唤醒后重新检查“队列非空或已经完成添加”的条件。

## Q2.2

目录扫描在 `ChangeDirectory` 中通过 `Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)` 完成。若需要递归扫描全部子目录，应将搜索选项改为 `SearchOption.AllDirectories`，并对无权限目录等 `IOException`/`UnauthorizedAccessException` 做相应处理。

## Q2.3.b（使用 AI）

本次作业使用了 AI 辅助。提示词的核心内容是：阅读给定的 C# 项目框架和测试，补全线程安全队列、并行日志分析器、控制台交互界面，并解释同步、互斥和虚假唤醒。AI 主要用于梳理接口要求、检查并发边界和生成初始实现，之后结合讲义与官方测试逐项核对。最终实现中保留了人工检查和测试验证，没有把无法验证的输出当作测试结果提交。

本节难度评价：中等。队列本身较清晰，日志分析器的共享状态、重复分析和异常状态处理需要仔细设计。

## 运行验证

已运行官方测试项目：

```text
src/test-02-multithreading/test-02-multithreading.csproj
```

提交前请补充以下真实截图（截图应来自实际终端或程序窗口）：

- LocalCli 正常流程：列出文件、分析全部、查看成功结果；
- LocalCli 鲁棒性流程：非法目录、非法选项、不存在文件名、重复分析；
- 官方测试通过的终端输出（若终端没有显示输出，可使用 IDE 的测试结果面板）。
