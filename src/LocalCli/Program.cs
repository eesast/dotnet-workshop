using LogAnalyzer;
using LogParser.Visitors;

namespace LocalCli
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var analyzer = InputDirectory();
            if (analyzer is null)
            {
                return;
            }

            ChooseAction(analyzer);
        }

        private static LogFileAnalyzer? InputDirectory()
        {
            var analyzer = new LogFileAnalyzer();
            while (true)
            {
                Console.WriteLine("Please input directory containing log files:");
                var directory = Console.ReadLine();
                if (directory is null)
                {
                    return null;
                }
                try
                {
                    if (!analyzer.ChangeDirectory(directory))
                    {
                        Console.WriteLine("Directory not exists, please try again:");
                        continue;
                    }
                    break;
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Directory illegal, please try again:");
                    continue;
                }
            }
            return analyzer;
        }

        private static void ChooseAction(LogFileAnalyzer analyzer)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("""
                Please choose:
                1. Show log files.
                2. Analyze specified log files.
                3. Analyze all log files.
                4. Get log file analysis result.
                5. Change directory.
                6. Exit.
                """);
                Console.Write(">>> ");
                Console.Out.Flush();

                int choice = 0;
                var choiceStr = Console.ReadLine();
                if (choiceStr is null)
                {
                    return;
                }
                try
                {
                    choice = int.Parse(choiceStr);
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid input, please try again.");
                    continue;
                }

                var actions = new Dictionary<int, Action<LogFileAnalyzer>>
                {
                    { 1, ShowLogFiles },
                    { 2, AnalyzeFiles },
                    { 3, AnalyzeAll },
                    { 4, GetAnalysisResult }
                };
                switch (choice)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                        actions[choice](analyzer);
                        break;
                    case 5:
                        var newAnalyzer = InputDirectory();
                        if (newAnalyzer is null)
                        {
                            return;
                        }
                        analyzer = newAnalyzer;
                        break;
                    case 6:
                        return;
                    default:
                        Console.WriteLine("Invalid choice, please try again.");
                        break;
                }
            }
        }

        private static void ShowLogFiles(LogFileAnalyzer analyzer)
        {
            var files = analyzer.GetLogFiles();
            Console.WriteLine("Log files:");
            foreach (var file in files)
            {
                Console.WriteLine($" - {file}");
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input file names separated by comma (e.g. log1.log,log2.log):");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return;

            var fileNames = input.Split(',').Select(s => s.Trim());
            try
            {
                // 调用分析方法
                analyzer.AnalyzeFiles(0, fileNames);
                Console.WriteLine("Analysis finished.");
            }
            catch (Exception ex)
            {
                // 鲁棒性：捕获异常并提示用户，而不是让程序崩溃
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            try
            {
                analyzer.AnalyzeAll(0);
                Console.WriteLine("All files analysis finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input file name:");
            var fileName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(fileName)) return;

            if (analyzer.TryGetAnalysisResult(fileName, out var result))
            {
                // 根据状态分流处理
                if (result!.State == AnalysisState.NotAnalyzed)
                {
                    Console.WriteLine("File has not been analyzed.");
                }
                else if (result.State == AnalysisState.Failed)
                {
                    Console.WriteLine($"Analysis failed: {result.ErrorMessage}");
                }
                else
                {
                    // 使用 Visitor 输出
                    var visitor = new KeyValueVisitor();
                    foreach (var entry in result.Entries)
                    {
                        var dict = visitor.Dump(entry);
                        foreach (var kv in dict)
                        {
                            Console.WriteLine($"  {kv.Key}: {kv.Value}");
                        }
                        Console.WriteLine();  // 每条日志间空一行
                    }
                }
            }
            else
            {
                Console.WriteLine("File not found.");
            }
        }
    }
}