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
                catch (Exception)
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
                Console.WriteLine("No log files found in the current directory.");
                return;
            }

            Console.WriteLine("Log files:");
            foreach (var fileName in logFiles)
            {
                Console.WriteLine(fileName);
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            while (true)
            {
                Console.WriteLine("Please input file names to analyze (separated by comma):");
                var input = Console.ReadLine();
                if (input is null)
                {
                    return;
                }

                var fileNames = input.Split(',')
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
                if (fileNames.Count == 0)
                {
                    Console.WriteLine("No valid file names, please try again.");
                    continue;
                }

                try
                {
                    analyzer.AnalyzeFiles(0, fileNames);
                    Console.WriteLine("Analysis finished.");
                    return;
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"Invalid file name: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Cannot analyze right now: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                }
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            try
            {
                analyzer.AnalyzeAll(0);
                Console.WriteLine("Analysis finished.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Cannot analyze right now: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            while (true)
            {
                Console.WriteLine("Please input file name to get analysis result:");
                var fileName = Console.ReadLine();
                if (fileName is null)
                {
                    return;
                }

                if (!analyzer.TryGetAnalysisResult(fileName, out var result) || result is null)
                {
                    var logFiles = analyzer.GetLogFiles();
                    Console.WriteLine($"File '{fileName}' not found.");
                    Console.WriteLine(logFiles.Count == 0
                        ? "No log files in the current directory."
                        : $"Available files: {string.Join(", ", logFiles)}");
                    continue;
                }

                switch (result.State)
                {
                    case AnalysisState.NotAnalyzed:
                        Console.WriteLine($"File '{fileName}' has not been analyzed yet.");
                        break;
                    case AnalysisState.Succeeded:
                        Console.WriteLine($"File '{fileName}' analysis succeeded, {result.Entries.Count} entries:");
                        var visitor = new KeyValueVisitor();
                        foreach (var entry in result.Entries)
                        {
                            var keyValues = visitor.Dump(entry)
                                .Select(pair => $"{pair.Key}: {pair.Value}");
                            Console.WriteLine(string.Join(", ", keyValues));
                        }
                        break;
                    case AnalysisState.Failed:
                        Console.WriteLine($"File '{fileName}' analysis failed: {result.ErrorMessage}");
                        break;
                }
                return;
            }
        }
    }
}
