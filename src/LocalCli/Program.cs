using LogAnalyzer;
using LogParser.Models;
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

            Console.WriteLine($"Log files in '{analyzer.CurrentDirectory}' ({files.Count}):");
            foreach (var file in files)
            {
                Console.WriteLine($"  {file}");
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input file names separated by ',' to analyze:");
            var line = Console.ReadLine();
            if (line is null)
            {
                return;
            }

            var fileNames = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fileNames.Length == 0)
            {
                Console.WriteLine("No file names provided.");
                return;
            }

            try
            {
                Console.WriteLine($"Analyzing {fileNames.Length} file(s) with parallelism 0 (= ProcessorCount = {Environment.ProcessorCount})...");
                analyzer.AnalyzeFiles(0, fileNames);
                Console.WriteLine("Analysis completed.");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            try
            {
                Console.WriteLine($"Analyzing all log files with parallelism 0 (= ProcessorCount = {Environment.ProcessorCount})...");
                analyzer.AnalyzeAll(0);
                Console.WriteLine("Analysis completed.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input the file name to get analysis result:");
            var fileName = Console.ReadLine();
            if (fileName is null)
            {
                return;
            }

            fileName = fileName.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine("No file name provided.");
                return;
            }

            if (!analyzer.TryGetAnalysisResult(fileName, out var result))
            {
                Console.WriteLine($"File '{fileName}' does not exist in the current directory.");
                return;
            }

            switch (result!.State)
            {
                case AnalysisState.NotAnalyzed:
                    Console.WriteLine($"File '{fileName}' has not been analyzed yet. Please analyze it first.");
                    break;
                case AnalysisState.Succeeded:
                    Console.WriteLine($"Analysis result for '{fileName}' (parsed by worker {result.WorkerId}, {result.Entries.Count} entries):");
                    var visitor = new KeyValueVisitor();
                    foreach (var entry in result.Entries)
                    {
                        var dump = visitor.Dump(entry);
                        Console.WriteLine($"  [{entry.EventType}] {string.Join(", ", dump.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    }
                    break;
                case AnalysisState.Failed:
                    Console.WriteLine($"Failed to analyze '{fileName}': {result.ErrorMessage}");
                    break;
            }
        }
    }
}
