02-multithreading 问答题

 Q2.1

 1. WorkQueue<T> 类中的共享变量有哪些？是通过什么保护其免于数据竞争的？
- 共享变量有：
  - `_items`（队列本身，存储元素）
  - `_isCompleted`（布尔标志，表示是否已停止添加）
保护方式：使用 `lock (_lock)` 对上述变量的所有读写操作进行互斥。`_lock` 是一个专用锁对象，确保同一时刻只有一个线程能进入临界区。

2. LogFileAnalyzer 类中的共享变量有哪些？是通过什么保护其免于数据竞争？
- 共享变量有：
   `_currentDirectory`（当前目录路径）
   `_isAnalyzing`（是否正在分析）
   `_logFiles`（文件字典，文件名→FileInfo）
  `_analysisResults`（解析结果字典，文件名→AnalysisResult）
- 保护方式：
  使用 `lock (_syncRoot)` 保护所有对这些字段的读写操作
  在 `WorkerMain` 中写入 `_analysisResults` 时也使用 `lock (_syncRoot)`，因为多个工作线程可能同时更新结果字典。

 3. 如果条件变量的判断条件使用了 `if` 判断而非 `while` 判断，当出现虚假唤醒现象时，会出现什么后果？结合无限仓库容量的生产者消费者问题简单叙述。
  如果使用 `if`，线程被唤醒后不会再次检查条件，直接执行后续操作。可能出现：
仓库为空但被唤醒.消费者以为有商品，执行 `Dequeue`，但队列中实际为空，导致抛出异常或取出无效数据。使用 `while` 则可以确保被唤醒后重新检查条件。
 Q2.2

1. 那一段代码扫描了给定的目录中的全部 `.log` 后缀的日志文件？
  在 `LogFileAnalyzer.ChangeDirectory` 方法中，有以下代码：
```csharp
var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)
    .Select(filePath => Path.GetFileName(filePath))
    .OrderBy(fileName => fileName);
2. 假使给定的需求是不仅要扫描给定目录中的日志文件，还要递归地获取给定的目录的全部子目录、子子目录……内的日志文件，应当如何做（简要回答即可）？
将 SearchOption.TopDirectoryOnly 改为 SearchOption.AllDirectories，即可递归扫描所有子目录。
Q2.3.b
我AI提供了本节 guidance.md 的任务描述，并附上已有的WorkQueue.cs、LogFileAnalyzer.cs、Program.cs 等代码文件，特别是多线程中的lock、Monitor.Wait/Pulse操作希望获得指导。在最初给出的WorkerMain代码中，AI将保存结果的操作放在了 catch 块内部，导致成功解析的结果没有被保存。经测试发现后，AI 将错误并修正。难度中等偏上。