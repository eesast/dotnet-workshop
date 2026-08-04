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
            if (files.Count == 0)
            {
                Console.WriteLine("No log files found in the current directory.");
                return;
            }

            Console.WriteLine("Log files in current directory:");
            foreach (var file in files)
            {
                Console.WriteLine($" - {file}");
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input file names (separated by comma):");
            Console.Write(">>> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Input cannot be empty.");
                return;
            }
            var fileNames = input.Split(',')
                                 .Select(f => f.Trim())
                                 .Where(f => !string.IsNullOrEmpty(f))
                                 .ToList();

            if (fileNames.Count == 0)
            {
                Console.WriteLine("No valid file names provided.");
                return;
            }

            try
            {
                analyzer.AnalyzeFiles(0, fileNames);
                Console.WriteLine("Analysis completed successfully.");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during analysis: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            try
            {
                analyzer.AnalyzeAll(0);
                Console.WriteLine("All log files analyzed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during analysis: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input log file name:");
            Console.Write(">>> ");
            var fileName = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            if (!analyzer.TryGetAnalysisResult(fileName, out var result) || result is null)
            {
                Console.WriteLine($"File '{fileName}' does not exist or is not in the directory.");
                return;
            }

            switch (result.State)
            {
                case AnalysisState.NotAnalyzed:
                    Console.WriteLine($"File '{fileName}' has NOT been analyzed yet. Please run analysis first.");
                    break;

                case AnalysisState.Succeeded:
                    Console.WriteLine($"Analysis result for '{fileName}' (Worker #{result.WorkerId}):");
                    // 修改部分：遍历传入
                    if (result.Entries != null)
                    {
                        foreach (var entry in result.Entries)
                        {
                            KeyValueVisitor.Dump(entry);
                        }
                    }
                    break;

                case AnalysisState.Failed:
                    Console.WriteLine($"Analysis for '{fileName}' FAILED!");
                    Console.WriteLine($"Error message: {result.ErrorMessage}");
                    break;
            }
        }
    }
}
