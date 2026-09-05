using LogParser.Models;
using LogParser.Parser;
using LogParser.Visitors;
using Parquet.Serialization;

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
                        .OrderBy(fileName => fileName)
                        .Concat(Directory.EnumerateFiles(directoryPath, "*.parquet", SearchOption.TopDirectoryOnly)
                        .Select(filePath => Path.GetFileName(filePath))
                        .OrderBy(fileName => fileName));
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

        public async Task SaveAnalysisResultAsync(string fileName, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Output directory path cannot be empty.", nameof(directoryPath));

            directoryPath = Path.GetFullPath(directoryPath);

            IReadOnlyList<LogEntry> entries;
            lock (_syncRoot)
            {
                if (!_analysisResults.TryGetValue(fileName, out var result))
                    throw new FileNotFoundException($"File '{fileName}' is not in the current directory or does not exist.", fileName);

                if (result.State != AnalysisState.Succeeded)
                    throw new InvalidOperationException($"Cannot save analysis result for file '{fileName}' because its analysis state is {result.State}.");

                if (!Directory.Exists(directoryPath))
                    throw new DirectoryNotFoundException($"Directory '{directoryPath}' does not exist.");

                entries = result.Entries;
            }

            string outputFileName = Path.ChangeExtension(fileName, ".parquet");
            string outputFilePath = Path.Combine(directoryPath, outputFileName);
            if (File.Exists(outputFilePath))
                throw new InvalidOperationException($"Output file '{outputFilePath}' already exists.");

            string temporaryFilePath = Path.Combine(directoryPath, $".{outputFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                var visitor = new ParquetVisitor();
                var data = entries.Select(visitor.Dump);
                await using (var stream = new FileStream(
                    temporaryFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await ParquetSerializer.SerializeAsync(
                        data,
                        stream);
                }

                File.Move(temporaryFilePath, outputFilePath, overwrite: false);
            }
            finally
            {
                File.Delete(temporaryFilePath);
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
            }

            try
            {
                RunWorkers(degreeOfParallelism, fileList);
            }
            finally
            {
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
                    try
                    {
                        if (_analysisResults[file.Name].State != AnalysisState.NotAnalyzed)
                        {
                            continue;
                        }
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new InvalidOperationException($"Unknown file name {file.Name}");
                    }
                    logFilesToParse.Add(file);
                }
            }

            if (logFilesToParse.Count == 0)
            {
                return;
            }

            var queue = new WorkQueue<FileInfo>();

            foreach (var logFile in logFilesToParse)
            {
                queue.Enqueue(logFile);
            }
            queue.CompleteAdding();

            degreeOfParallelism = Math.Max(Math.Min(degreeOfParallelism, logFilesToParse.Count), 1);
            var workers = new Thread[degreeOfParallelism];
            for (int i = 0; i < degreeOfParallelism; i++)
            {
                int workerId = i;
                string threadName = $"log-analyzer-worker-{workerId}";
                Thread worker = new Thread(() => WorkerMain(workerId, queue))
                {
                    Name = threadName
                };
                worker.Start();
                workers[i] = worker;
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
                    if (file.Extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase))
                    {
                        var logEntries = ParquetParser.ParseAsync(file.FullName).Result;
                        result = new AnalysisResult
                        (
                            FileName: file.Name,
                            FullName: file.FullName,
                            State: AnalysisState.Succeeded,
                            Entries: logEntries.ToArray(),
                            ErrorMessage: null,
                            WorkerId: workerId
                        );
                    }
                    else
                    {
                        using var reader = new StreamReader(file.FullName);
                        result = new AnalysisResult
                        (
                            FileName: file.Name,
                            FullName: file.FullName,
                            State: AnalysisState.Succeeded,
                            Entries: parser.Parse(reader).ToArray(),
                            ErrorMessage: null,
                            WorkerId: workerId
                        );
                    }
                }
                catch (Exception ex)
                {
                    result = new AnalysisResult
                    (
                        FileName: file.Name,
                        FullName: file.FullName,
                        State: AnalysisState.Failed,
                        Entries: Array.Empty<LogEntry>(),
                        ErrorMessage: ex.ToString(),
                        WorkerId: workerId
                    );
                }

                lock (_syncRoot)
                {
                    _analysisResults[file.Name] = result;
                }
            }
        }
    }
}
