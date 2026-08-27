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
            var logFiles = analyzer.GetLogFiles();
            if (logFiles.Count == 0)
            {
                Console.WriteLine("No log files found.");
                return;
            }
            Console.WriteLine("Log files:");
            foreach (var file in logFiles)
            {
                Console.WriteLine($"- {file}");
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Enter the degree of parallelism:");
            int degreeOfParallelism = 0;
            Console.Write(">>> ");
            var degreeStr = Console.ReadLine();
            if (degreeStr is null)
            {
                Console.WriteLine("No degree of parallelism entered.");
                return;
            }
            try
            {
                degreeOfParallelism = int.Parse(degreeStr);
            }
            catch (Exception)
            {
                Console.WriteLine("Invalid input, please try again.");
                return;
            }
            Console.WriteLine("please input log file names (comma separated):");
            Console.Write(">>> ");
            var fileNamesStr = Console.ReadLine();
            if (fileNamesStr is null)
            {
                Console.WriteLine("No file names entered.");
                return;
            }
            var fileNames = fileNamesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            try
            {
                analyzer.AnalyzeFiles(degreeOfParallelism, fileNames);
                Console.WriteLine("Analysis completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Enter the degree of parallelism:");
            int degreeOfParallelism = 0;
            Console.Write(">>> ");
            var degreeStr = Console.ReadLine();
            if (degreeStr is null)
            {
                Console.WriteLine("No degree of parallelism entered.");
                return;
            }
            try
            {
                degreeOfParallelism = int.Parse(degreeStr);
            }
            catch (Exception)
            {
                Console.WriteLine("Invalid input, please try again.");
                return;
            }
            try
            {
                analyzer.AnalyzeAll(degreeOfParallelism);
                Console.WriteLine("Analysis completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }
        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("please input log file name:");
            Console.Write(">>> ");
            var fileName = Console.ReadLine();
            if (fileName is null)
            {
                Console.WriteLine("No file name entered.");
                return;
            }
            try
            {
                if (!analyzer.TryGetAnalysisResult(fileName, out var result) || result is null)
                {
                    Console.WriteLine($"File '{fileName}' not found in the current directory.");
                    return;
                }
                Console.WriteLine($"Analysis result for {fileName}:");
                Console.WriteLine($"- State: {result.State}");
                if (result.State == AnalysisState.Failed)
                {
                    Console.WriteLine($"- Error message: {result.ErrorMessage}");
                }
                else
                {
                    Console.WriteLine($"- Number of entries: {result.Entries.Count}");
                    foreach (var entry in result.Entries)
                    {
                        Console.WriteLine($"  - {entry.Timestamp}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting analysis result: {ex.Message}");
            }
        }
    }
}
