## Q2.1
### 2.1.1
- WorkQueue共享变量有：`_items`、`_isCompleted`
  - `_items`通过`lock(_items){...}`防止竞争；
  - `_isCompleted`通过每次读取时`lock (_items)`保证读到的值正确
- LogFileAnalyzer的共享变量有：`_analysisResults`、`_isAnalyzing`
  - `_analysisResults`通过`lock(_syncRoot){...}`防止竞争
  - `_isAnalyzing`通过每次读取时`lock (_syncRoot)`保证读到的值正确
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
- 主要使用Copilot的自动补全，以及让ai解释一些语句的含义、函数的用法
- 由于自动补全是顺着我的思路写的，可能我起头起偏了，ai写的也会出问题，这时就需要另外让ai检查一下这里是否合理，多给几个角度再自行核对
- 本节难度适中