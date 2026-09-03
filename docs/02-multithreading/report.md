# 02-multithreading 实验报告

## 实现的功能介绍

本节完成了三个部分：

### T2.1 线程安全队列 `WorkQueue<T>`（`src/LogAnalyzer/WorkQueue.cs`）

基于 C# 非线程安全的 `Queue<T>` 实现了一个线程安全队列，作为"无限仓库容量"生产者消费者模型：

+ `Enqueue`：加锁后入队，并通过 `Monitor.Pulse` 唤醒一个等待中的消费者；若队列已 `CompleteAdding` 则抛出 `InvalidOperationException`。
+ `TryDequeue`：加锁后，在「队列为空且未结束放入」时用 `while (条件) Monitor.Wait` 阻塞等待；一旦有元素被放入或放入结束即被唤醒。有元素则取出返回 `true`，已结束且队列为空则返回 `false`。
+ `CompleteAdding`：加锁后置 `_isCompleted = true` 并 `Monitor.PulseAll` 唤醒所有等待的消费者，使它们能够退出。
+ `IsCompleted`：在锁内读取标记。

### T2.2 并行日志分析 `LogFileAnalyzer`（`src/LogAnalyzer/LogFileAnalyzer.cs`）

+ `AnalyzeFiles`：校验参数后，在锁内设置 `_isAnalyzing = true`；`RunWorkers` 执行完毕后，在 `finally` 中加锁复位 `_isAnalyzing = false`，从而保证同一时刻只有一个分析任务，其他并发调用会抛出 `InvalidOperationException`。
+ `RunWorkers`：在锁内过滤「已经分析过（Succeeded / Failed）」的文件（跳过以节省计算资源），对未知文件抛 `InvalidOperationException`；将待解析文件全部入队并 `CompleteAdding`；按 `degreeOfParallelism` 创建后台线程，以 `WorkerMain` 为入口；最后 `Join` 等待所有线程结束。
+ `WorkerMain`：循环 `TryDequeue` 文件，使用上一章的 `LogFileParser` 解析；成功时构造 `Succeeded` 结果，失败时捕获异常并构造 `Failed` 结果（错误信息存入 `ErrorMessage`）；在锁内把结果写入 `_analysisResults`，避免数据竞争。

### T2.3 控制台交互界面 `LocalCli`（`src/LocalCli/Program.cs`）

+ `ShowLogFiles`：调用 `GetLogFiles` 列出当前目录的全部日志文件。
+ `AnalyzeFiles`：接受逗号分隔的文件名列表并调用 `AnalyzeFiles(0, fileNames)` 分析，捕获异常并提示，不会崩溃。
+ `AnalyzeAll`：调用 `AnalyzeAll(0)` 分析全部文件，同样做了异常捕获。
+ `GetAnalysisResult`：按文件名查询 `TryGetAnalysisResult`：
  - 未分析 → 给出提示；
  - 成功 → 用 `KeyValueVisitor.Dump` 输出每一条日志的键值对；
  - 失败 → 输出 `ErrorMessage`；
  - 不存在 → 给出提示。

## 功能记录（终端输出）

以下为 `src/` 目录下运行 `LocalCli` 的完整功能流程记录（命令行环境无法截取位图，故以终端文本记录代替截图）：

```
Please input directory containing log files:      <- 输入 dataset
Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 1                                              <- 功能1：列出日志文件
Log files in the current directory:
- basic-fail.log
- basic-multiple.log
- basic.log

Please choose: ...
>>> 3                                              <- 功能3：分析全部文件
Analysis completed.

Please choose: ...
>>> 4                                              <- 功能4：查询分析结果
Please input a file name to query:
>>> basic.log
Analysis result for 'basic.log':
LineNo=0, Timestamp=2026-06-05T16:00:29.0450000+00:00, PodName=userservice-0, Severity=Info, EventType=Call, RequestId=3a013a08-6853-49fc-8f06-50daeb5c1e51, TargetService=authservice, DurationMs=18
LineNo=1, Timestamp=2026-06-05T16:00:31.0860000+00:00, PodName=userservice-1, Severity=Info, EventType=Request, RequestId=1177c344-115e-4f85-b8ec-c9164d132b79, Method=GET, Path=/api/user/john, StatusCode=404
LineNo=2, Timestamp=2026-06-05T16:05:45.3220000+00:00, PodName=gateway-0, Severity=Error, EventType=Internal, ExceptionName=System.InvalidOperationException, ExceptionMessage=Failed to load gateway routing configuration.
```

## 鲁棒性测试记录（终端输出）

```
Please choose: ...
>>> abc                                           <- 非法菜单输入
Invalid input, please try again.

Please choose: ...
>>> 4
Please input a file name to query:
>>> nonexist.log                                  <- 查询不存在的文件
No analysis result found for 'nonexist.log'.

Please choose: ...
>>> 2
Please input file names to analyze (separated by commas):
>>> basic.log, no-such-file.log                   <- 分析列表中含不存在的文件
Analysis failed: File 'no-such-file.log' is not in the current directory or does not exist.

Please choose: ...
>>> 5
Please input directory containing log files:
>>> nonexist-dir                                  <- 切换到不存在的目录
Directory not exists, please try again:
Please input directory containing log files:
>>> dataset                                       <- 重新输入有效目录，恢复正常
```

可以看到，各种非法输入（非法菜单选项、不存在的文件名、不存在的目录）都会被捕获并提示用户重新输入，程序不会崩溃。


## (Q2.1)

### `WorkQueue<T>` 类中的共享变量与保护方式

+ 共享变量：
  - `_items`：`Queue<T>`，队列本身；
  - `_isCompleted`：`bool`，标记是否已结束放入。
+ 保护方式：对 `_items` 使用 `lock (_items)` 进行互斥保护。`_isCompleted` 的读（`IsCompleted`）与写（`CompleteAdding`、`Enqueue` 的检查）全部位于对 `_items` 的 `lock` 临界区内，因此同一个互斥锁同时保护了这两个共享变量。此外，线程间协作使用 C# 的管程（`Monitor.Wait` / `Monitor.Pulse` / `Monitor.PulseAll`，即条件变量）。

### `LogFileAnalyzer` 类中的共享变量与保护方式

+ 共享变量：`_currentDirectory`（当前目录）、`_isAnalyzing`（是否正在分析）、`_logFiles`（文件名 → `FileInfo`）、`_analysisResults`（文件名 → `AnalysisResult`）。
+ 保护方式：以上共享变量一律通过 `lock (_syncRoot)` 保护。`IsAnalyzing` 属性、`ChangeDirectory`、`GetLogFiles`、`TryGetAnalysisResult`、`AnalyzeFiles`、`RunWorkers` 以及 `WorkerMain` 中写回结果的代码，都先加锁再访问共享状态，从而避免数据竞争。

### 条件变量用 `if` 判断而非 `while` 的后果

在 MESA 模型（以及类 UNIX 系统的信号导致的虚假唤醒）下，条件变量的 `wait` 可能在没有任何 `signal` / `broadcast` 的情况下被唤醒。若使用 `if (条件) { wait(); }`，线程被唤醒后不会重新检查条件就继续往下执行。

以无限仓库容量的生产者消费者问题为例：消费者在「仓库为空」时 `wait`，如果它被**虚假唤醒**（或虽然被正常唤醒但商品已被另一个消费者取走），用 `if` 的消费者会直接执行"取商品"操作，而此时仓库里并没有商品，导致**空取出（取出不存在的商品 / 缓冲区下溢）**，产生逻辑错误甚至未定义行为。

而用 `while (条件) { wait(); }` 时，每次从 `wait` 返回后都会**重新检查条件**，只有条件真正满足（仓库非空）才会继续取商品，因此即使出现虚假唤醒也能安全地回到 `wait` 中，保证正确性。

## (Q2.2)

### 扫描给定目录全部 `.log` 文件的代码

在 `LogFileAnalyzer.ChangeDirectory` 方法中：

```csharp
var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)
    .Select(filePath => Path.GetFileName(filePath))
    .OrderBy(fileName => fileName);
```

### 若需要递归扫描子目录

将 `SearchOption.TopDirectoryOnly` 改为 `SearchOption.AllDirectories` 即可，例如：

```csharp
var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.AllDirectories)
    .Select(filePath => Path.GetFileName(filePath))
    .OrderBy(fileName => fileName);
```


## (Q2.3.b)

本次作业使用了 AI（Cline 编码助手）。

### 我给予 AI 的提示词

> 阅读 `docs/02-multithreading` 的说明文档与 `src/test-02-multithreading` 测试代码，在 `src/LogAnalyzer`（`WorkQueue.cs`、`LogFileAnalyzer.cs`）与 `src/LocalCli/Program.cs` 中补齐所有 `TODO` 实现，使 `dotnet test test-02-multithreading -c Release` 全部通过，并运行 `LocalCli` 验证功能与鲁棒性，最后撰写 `docs/02-multithreading/report.md`。

### 对 AI 的使用方式

主要让 AI 编写一部分作业代码（T2.1 / T2.2 / T2.3 的 TODO 实现），同时让 AI 对照测试用例解释代码框架（如 `_syncRoot` 的加锁位置、`WorkerMain` 的职责），以便我理解后再落地。

### AI 的解答是否出现过错误

出现过一次：初版 `WorkQueue.TryDequeue` 直接写 `item = _items.Dequeue();`，编译时产生 CS8762 警告（`[NotNullWhen(true)]` 保证返回 `true` 时 `item` 非空，但编译器无法推导 `Dequeue()` 对无约束泛型 `T` 一定返回非空）。AI 随后使用空包容运算符 `Dequeue()!` 修复，重新编译后警告消除、测试通过。

### 难度评价

偏低到适中。如果已经理解「互斥锁 + 条件变量 + 生产者消费者」模型，T2.1 的 `WorkQueue` 几乎是教科书实现；T2.2 的难点在于想清楚加锁的位置（尤其是 `_isAnalyzing` 的设置/复位与 `WorkerMain` 写回结果）以及如何在多线程竞争下保证"只有一次分析成功"；T2.3 主要是把已有接口串起来并做好异常捕获，难度不大。总体属于需要认真思考并发同步、但逻辑并不复杂的程度。

