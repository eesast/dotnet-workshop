## Q2.1
### 2.1.1
- WorkQueue共享变量有：`_items`、`_isCompleted`
  - `_items`通过`lock(_items){...}`防止竞争；
  - `_isCompleted`通过每次读取时`lock (_items)`保证读到的值正确，写入时`lock (_items)`保证单一线程访问
- LogFileAnalyzer的共享变量有：`_analysisResults`、`_isAnalyzing`、`_logFiles`、`_currentDirectory`
  - `_analysisResults`、`_logFiles`通过`lock(_syncRoot){...}`保护
  - `_isAnalyzing`通过每次读取时`lock (_syncRoot)`保证读到的值正确
  - `_currentDirectory`写入在 `lock (_syncRoot)` 中，读取未加锁，不过其一般也不会被多个线程写入
- 不用while的话，wait被唤醒后就继续向下执行了，就像顾客没有等到生产者通知生产完成就尝试去仓库抢东西，可能产生不符合预期的bug
## Q2.2
- 扫描全部 .log 后缀的日志文件：
```csharp
var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)
                        .Select(filePath => Path.GetFileName(filePath))
                        .OrderBy(fileName => fileName);
                    foreach (var fileName in logFiles){...}
```
- `SearchOption.AllDirectories`
## Q2.3
- 有使用
### 2.3.b
- 主要使用Copilot的自动补全，以及让ai解释一些语句的含义、函数的用法，提示词就是“解释xxx的参数含义与用法”
- 由于自动补全是顺着我的思路写的，可能我起头起偏了，ai写的也会出问题，比如`worker.Join()`的时候没有注意到这个函数会自动阻塞，就写了个while，然后copilot就顺着开编了，这时就需要另外让ai解释一下函数用法，检查一下逻辑是否合理，多给几个角度再自行核对
- 本节难度适中