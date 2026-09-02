using LogParser.Models;
using LogParser.Parser;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace LogAnalyzer
{
    public class LogFileAnalyzer
    {
        private readonly object _syncRoot = new(); // 互斥量
        private string? _currentDirectory = null;
        private bool _isAnalyzing = false;
        private readonly Dictionary<string, FileInfo> _logFiles = new();
        private readonly Dictionary<string, AnalysisResult> _analysisResults = new();

        public string? CurrentDirectory => _currentDirectory;
        public bool HasDirectory => _currentDirectory is not null;
        public bool IsAnalyzing
        {
            get
            {
                lock (_syncRoot) return _isAnalyzing;
            }
        }

        // ... (省略构造函数和 ChangeDirectory，保持你原有的不变) ...
        public LogFileAnalyzer(string? directoryPath = null)
        {
            var cdResult = ChangeDirectory(directoryPath);
            if (!cdResult)
            {
                throw new ArgumentException($"Invalid directory path: {directoryPath}.");
            }
        }

        public bool ChangeDirectory(string? directoryPath)
        {
            lock (_syncRoot)
            {
                if (_isAnalyzing)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(directoryPath))
                {
                    directoryPath = null;
                }
                else
                {
                    directoryPath = Path.GetFullPath(directoryPath);
                    if (!Directory.Exists(directoryPath))
                    {
                        return false;
                    }
                }

                _currentDirectory = directoryPath;
                _logFiles.Clear();
                _analysisResults.Clear();
                if (directoryPath is not null)
                {
                    var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)
                        .Select(filePath => Path.GetFileName(filePath))
                        .OrderBy(fileName => fileName);
                    foreach (var fileName in logFiles)
                    {
                        _logFiles.Add(fileName, new FileInfo(Path.Join(_currentDirectory, fileName)));
                        _analysisResults.Add(fileName, new AnalysisResult(
                            FileName: fileName,
                            FullName: _logFiles[fileName].FullName,
                            State: AnalysisState.NotAnalyzed,
                            Entries: Array.Empty<LogEntry>(),
                            ErrorMessage: null,
                            WorkerId: -1
                        ));
                    }
                }
                return true;
            }
        }

        public IReadOnlyList<string> GetLogFiles()
        {
            lock (_syncRoot)
            {
                return _logFiles.Keys.ToList();
            }
        }

        public bool TryGetAnalysisResult(string fileName, out AnalysisResult? result)
        {
            lock (_syncRoot)
            {
                return _analysisResults.TryGetValue(fileName, out result);
            }
        }

        public void AnalyzeAll(int degreeOfParallelism)
        {
            List<string> fileNames;
            lock (_syncRoot)
            {
                fileNames = _logFiles.Keys.ToList();
            }
            AnalyzeFiles(degreeOfParallelism, fileNames);
        }
        public void AnalyzeFiles(int degreeOfParallelism, IEnumerable<string> fileNames)
        {
            if (degreeOfParallelism < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(degreeOfParallelism), "Degree of parallelism must be non-negative.");
            }

            if (degreeOfParallelism == 0)
            {
                degreeOfParallelism = Environment.ProcessorCount;
            }

            List<string> fileNameList = fileNames.ToList();
            List<FileInfo> fileList;
            lock (_syncRoot)
            {
                if (_isAnalyzing) throw new InvalidOperationException("Analysis already in progress.");

                foreach (var fileName in fileNameList)
                {
                    if (!_logFiles.ContainsKey(fileName))
                        throw new ArgumentException($"File '{fileName}' does not exist.");
                }
                fileList = fileNameList.Select(fileName => _logFiles[fileName]).ToList();

                // 【T2.2: 设置正在分析标记】
                _isAnalyzing = true;
            }

            try
            {
                RunWorkers(degreeOfParallelism, fileList);
            }
            finally
            {
                // 【T2.2: 无论成功失败，最后都要解除标记】
                lock (_syncRoot)
                {
                    _isAnalyzing = false;
                }
            }
        }

        private void RunWorkers(int degreeOfParallelism, IReadOnlyList<FileInfo> fileList)
        {
            var logFilesToParse = new List<FileInfo>();
            lock (_syncRoot)
            {
                foreach (var file in fileList)
                {
                    // 【T2.2: 过滤未解析或需重解析的文件】
                    if (!_analysisResults.TryGetValue(file.Name, out var currentResult))
                    {
                        throw new InvalidOperationException($"File {file.Name} is unknown.");
                    }

                    // 只有未分析或失败的文件需要重新解析（已成功的跳过）
                    if (currentResult.State != AnalysisState.Succeeded)
                    {
                        logFilesToParse.Add(file);
                    }
                }
            }

            if (logFilesToParse.Count == 0) return;

            // 创建之前完成的阻塞队列
            var queue = new WorkQueue<FileInfo>();

            // 【T2.2: 生产者 - 放入所有文件并结束生产】
            foreach (var file in logFilesToParse)
            {
                queue.Enqueue(file);
            }
            queue.CompleteAdding(); // 必须调用，否则工人会死等

            // 确定实际需要的线程数
            degreeOfParallelism = Math.Max(Math.Min(degreeOfParallelism, logFilesToParse.Count), 1);
            var workers = new Thread[degreeOfParallelism];
            for (int i = 0; i < degreeOfParallelism; i++)
            {
                int workerId = i; // 闭包变量，必须在循环内声明
                // 【T2.2: 创建并启动工人线程】
                workers[i] = new Thread(() => WorkerMain(workerId, queue))
                {
                    Name = $"log-analyzer-worker-{workerId}"
                };
                workers[i].Start();
            }

            // 【T2.2: 等待所有工人收工】
            foreach (var worker in workers)
            {
                worker.Join();
            }
        }

        private void WorkerMain(int workerId, WorkQueue<FileInfo> queue)
        {
            var parser = new LogFileParser();

            while (queue.TryDequeue(out var file))
            {
                AnalysisResult result;
                try
                {
                    // 【T2.2: 调用解析器解析文件】
                    // 注意：Parse 是惰性求值，真正的读文件/解析发生在 ToArray() 时，
                    // 所以必须把 ToArray() 放在锁外，否则解析会被锁串行化、失去并行性
                    using var reader = new StreamReader(file.FullName);
                    var entries = parser.Parse(reader).ToArray();

                    lock (_syncRoot)
                    {
                        result = _analysisResults[file.Name] with
                        {
                            State = AnalysisState.Succeeded,
                            Entries = entries,
                            WorkerId = workerId,
                            ErrorMessage = null
                        };
                    }
                }
                catch (Exception ex)
                {
                    // 【T2.2: 异常处理】
                    lock (_syncRoot)
                    {
                        result = _analysisResults[file.Name] with
                        {
                            State = AnalysisState.Failed,
                            ErrorMessage = ex.Message,
                            WorkerId = workerId
                        };
                    }
                }

                // 【T2.2: 保存结果，必须加锁防止数据竞争】
                lock (_syncRoot)
                {
                    _analysisResults[file.Name] = result;
                }
            }
        }
    }
}