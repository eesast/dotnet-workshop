using LogParser.Models;
using LogParser.Parser;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;

namespace LogAnalyzer
{
    public class LogFileAnalyzer
    {
        private readonly object _syncRoot = new();
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
                lock (_syncRoot)
                {
                    return _isAnalyzing;
                }
            }
        }

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
                if (_isAnalyzing)
                {
                    throw new InvalidOperationException("Analysis is already in progress.");
                }

                foreach (var fileName in fileNameList)
                {
                    if (!_logFiles.ContainsKey(fileName))
                    {
                        throw new ArgumentException($"File '{fileName}' is not in the current directory or does not exist.");
                    }
                }
                fileList = fileNameList.Select(fileName => _logFiles[fileName]).ToList();

                _isAnalyzing = true;
                Monitor.PulseAll(_syncRoot);
            }

            try
            {
                RunWorkers(degreeOfParallelism, fileList);
            }
            finally
            {
                lock(_syncRoot)
                {
                    _isAnalyzing = false;
                    Monitor.PulseAll(_syncRoot);
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
                    AnalysisResult? result;
                    if(_analysisResults.TryGetValue(file.Name, out result))
                    {
                        if(result != null && result.State == AnalysisState.NotAnalyzed)
                        {   
                            logFilesToParse.Add(file);
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"File '{file.Name}' is not in the current directory or does not exist.");
                    }
                }
                Monitor.PulseAll(_syncRoot);
            }

            if (logFilesToParse.Count == 0)
            {
                return;
            }

            var queue = new WorkQueue<FileInfo>();

            foreach (var file in logFilesToParse)
            {
                queue.Enqueue(file);
            }
            queue.CompleteAdding();
            
            degreeOfParallelism = Math.Max(Math.Min(degreeOfParallelism, logFilesToParse.Count), 1);
            var workers = new Thread[degreeOfParallelism];
            for (int i = 0; i < degreeOfParallelism; i++)
            {
                int workerId = i;
                string threadName = $"log-analyzer-worker-{workerId}";
                workers[i] = new Thread(() => WorkerMain(workerId, queue))
                {
                    Name = threadName,
                    IsBackground = true
                };
            }

            foreach (var worker in workers)
            {
                worker.Start();
            }

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
                    using var reader = file.OpenText();
                    var entries = parser.Parse(reader).ToList();
                    result = new AnalysisResult(
                        FileName: file.Name,
                        FullName: file.FullName,
                        State: AnalysisState.Succeeded,
                        Entries: entries,
                        ErrorMessage: null,
                        WorkerId: workerId
                    );
                }
                catch (Exception ex)
                {
                    result = new AnalysisResult(
                        FileName: file.Name,
                        FullName: file.FullName,
                        State: AnalysisState.Failed,
                        Entries: Array.Empty<LogEntry>(),
                        ErrorMessage: ex.Message,
                        WorkerId: workerId
                    );
                }

                lock (_syncRoot)
                {
                    _analysisResults[file.Name] = result;
                    Monitor.PulseAll(_syncRoot);
                }
            }
        }
    }
}
