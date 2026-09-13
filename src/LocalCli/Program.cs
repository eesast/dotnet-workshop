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
                Console.WriteLine("No .log files found.");
                return;
            }

            Console.WriteLine("Log files:");
            foreach (var file in files)
            {
                Console.WriteLine($"- {file}");
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input comma-separated log file names:");
            var input = Console.ReadLine();
            if (input is null)
            {
                return;
            }

            var fileNames = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fileNames.Length == 0)
            {
                Console.WriteLine("No file name was provided.");
                return;
            }

            try
            {
                analyzer.AnalyzeFiles(0, fileNames);
                Console.WriteLine("Analysis completed.");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Console.WriteLine($"Unable to analyze files: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            try
            {
                analyzer.AnalyzeAll(0);
                Console.WriteLine("Analysis completed.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Unable to analyze files: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input a log file name:");
            var fileName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            try
            {
                if (!analyzer.TryGetAnalysisResult(fileName.Trim(), out var result) || result is null)
                {
                    Console.WriteLine("The specified file does not exist in the current directory.");
                    return;
                }

                switch (result.State)
                {
                    case AnalysisState.NotAnalyzed:
                        Console.WriteLine("This file has not been analyzed yet.");
                        break;
                    case AnalysisState.Failed:
                        Console.WriteLine($"Analysis failed: {result.ErrorMessage}");
                        break;
                    case AnalysisState.Succeeded:
                        var visitor = new KeyValueVisitor();
                        foreach (var entry in result.Entries)
                        {
                            var values = visitor.Dump(entry);
                            Console.WriteLine(string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}")));
                        }
                        break;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Console.WriteLine($"Unable to get analysis result: {ex.Message}");
            }
        }
    }
}
