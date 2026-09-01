# 02-multithreading 报告

## 功能实现说明

### T2.1 WorkQueue<T>（线程安全阻塞队列）

用 `lock (_items)` + `Monitor` 条件变量实现生产者-消费者队列：

- `Enqueue`：持锁入队后 `Monitor.Pulse` 唤醒一个等待的消费者；`CompleteAdding` 之后再入队抛 `InvalidOperationException`；
- `TryDequeue`：持锁后 **while** 循环检查（而非 if，防虚假唤醒）：队列空且未完成 → `Monitor.Wait` 挂起等待；队列空且已完成 → 返回 false；否则出队返回；
- `CompleteAdding`：置 `_isCompleted = true` 后 `Monitor.PulseAll` 唤醒**所有**等待者，让它们重新检查条件并退出。

T2.1.3 用 8 线程 × 50000 条数据验证了 FIFO 语义与线程安全（每个生产者的序列在消费端仍严格递增）。

### T2.2 LogFileAnalyzer（多线程并行分析）

- `AnalyzeFiles`：进入临界区校验文件名合法后置 `_isAnalyzing = true`（`finally` 中持锁复位，防异常泄漏状态）；
- `RunWorkers`：持锁过滤出 `NotAnalyzed` 状态的文件（未知文件抛 `InvalidOperationException`）→ 全部入队后 `CompleteAdding` → 创建 `min(并行度, 文件数)` 个后台线程跑 `WorkerMain` → `Join` 等待全部结束；
- `WorkerMain`：循环 `TryDequeue` 领任务，`parser.Parse(reader).ToList()` 解析成功 → `Succeeded` 结果；任何异常被捕获 → `Failed` 结果（`ex.Message` 存入 `ErrorMessage`，不使 worker 崩溃）；写回 `_analysisResults` 前持 `_syncRoot` 锁。

T2.2.5 验证了多线程对多文件分析的加速比。

### T2.3 LocalCli 控制台界面

实现了菜单 1-6 全部功能：列文件 / 指定文件分析 / 全部分析 / 查询结果（NotAnalyzed 提示未分析、Succeeded 用 `KeyValueVisitor.Dump` 逐条输出键值对、Failed 输出错误消息）/ 换目录 / 退出。所有用户输入路径都包了 try-catch（`ArgumentException`/`InvalidOperationException`），非法输入只提示不崩溃。

运行截图与鲁棒性测试截图见 `screenshots/` 目录（后续运行 LocalCli 后补充）。

## 问答题

### (Q2.1) 临界区与共享变量

**WorkQueue<T> 的共享变量**：`_items`（`Queue<T>`，队列本体）和 `_isCompleted`（完成标志）。二者都只在与 `_items` 互斥的临界区内读写——`Enqueue`/`TryDequeue`/`CompleteAdding`/`IsCompleted` 全部 `lock (_items)`。锁本身还兼任条件变量：消费者用 `Monitor.Wait(_items)` 挂起，生产者用 `Pulse`/`PulseAll` 唤醒，等待时自动放锁、被唤醒后重新竞争锁，因此不会死锁互斥量。

**LogFileAnalyzer 的共享变量**：`_currentDirectory`、`_isAnalyzing`、`_logFiles`、`_analysisResults` 四个字段，全部以 `_syncRoot`（私有 object）作互斥量保护。`AnalyzeAll` 先持锁拷贝文件名列表再放锁执行，`WorkerMain` 写结果时再短暂持锁，减小临界区粒度。

**if 改 while 的后果（虚假唤醒）**：无限容量生产者-消费者中，消费者伪码 `if (queue empty) wait(); dequeue();` 若被虚假唤醒（无人 signal 的情况下 `wait` 返回），消费者会**跳过重新检查**直接执行 `dequeue()`——此时队列可能仍为空，导致从空队列取元素（返回错误数据或抛异常）；若用 `while (queue empty) wait();`，虚假唤醒后条件重新判定仍为真，再次进入等待，逻辑不受影响。这就是条件变量必须配合循环使用的原因。

### (Q2.2) 目录扫描代码

扫描全部 `.log` 文件的是 `ChangeDirectory` 中的：

```csharp
var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)
    .Select(filePath => Path.GetFileName(filePath))
    .OrderBy(fileName => fileName);
```

若要递归扫描所有子目录，把 `SearchOption.TopDirectoryOnly` 改为 `SearchOption.AllDirectories` 即可（`.NET 8+` 也可用 `EnumerationOptions` 控制忽略权限错误等细节）。

### (Q2.3.b) AI 使用情况

**提示词**：向 AI 提供了 WorkQueue/LogFileAnalyzer 的完整框架源码，要求"实现生产者-消费者语义的阻塞队列，TryDequeue 必须用 while 循环防虚假唤醒；RunWorkers 按 TODO 注释的语义补全，保持锁约定（_syncRoot 保护结果字典）"。

**使用方式**：介于"讲解框架"与"写部分代码"之间——锁与条件变量的语义是我先理解的，AI 主要负责把语义翻译成符合框架风格的 C# 代码。

**AI 的错误**：一次生成中 `TryDequeue` 用了 `if`，被我按 Q2.1 同样的虚假唤醒理由要求改成 `while`；另一次在 `RunWorkers` 过滤文件时漏了 `CompleteAdding()`（消费者会在空队列上永久 Wait），对照 T2.2 测试挂起超时的现象定位后补上。

**难度评价**：适中偏上。C# 的 `lock`/`Monitor` 语义与操作系统课的互斥量/条件变量一一对应，理论不新；难在并发 bug 不可复现，必须靠测试压力验证（T2.2.5 跑了 54 秒）。
