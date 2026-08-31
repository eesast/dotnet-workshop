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
            var files = analyzer.GetLogFiles(); // 问分析器要文件列表
            if (files.Count == 0)
            {
                Console.WriteLine("目录下没有 .log 文件。");
                return;
            }
            Console.WriteLine("文件列表:");
            foreach (var file in files) // 遍历输出
            {
                Console.WriteLine($"  - {file}");
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.Write("请输入要分析的文件名（多个用逗号分隔）: ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return; // 如果输入空，直接返回

            var names = input.Split(',')
                            .Select(n => n.Trim())
                            .Where(n => !string.IsNullOrEmpty(n))
                            .ToList();

            try
            {
                analyzer.AnalyzeFiles(0, names); // 0 表示自动用 CPU 核心数
                Console.WriteLine("分析任务已执行完毕。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"分析过程中发生错误: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
           try
            {
                analyzer.AnalyzeAll(0);
                Console.WriteLine("全部分析任务已执行完毕。");
            }
            catch (Exception ex) { /* 抓异常防止崩溃 */ }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.Write("请输入要查看的文件名: ");
            string? fileName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(fileName)) return;

            if (analyzer.TryGetAnalysisResult(fileName, out var result))
            {
                switch (result.State)
            {
                    case AnalysisState.NotAnalyzed:
                        Console.WriteLine($"文件 {fileName} 尚未分析。");
                        break;
                    case AnalysisState.Succeeded:
                        var visitor = new KeyValueVisitor();
                        foreach (var entry in result.Entries)
                        {
                            var dict = visitor.Dump(entry);
                            Console.WriteLine(string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}")));
                        }
                        break;
                    case AnalysisState.Failed:
                        Console.WriteLine($"文件 {fileName} 分析失败: {result.ErrorMessage}");
                        break;
            }
            }
            else
            {
                Console.WriteLine($"未找到文件 {fileName} 的分析结果。");
            }
        }
    }
}
