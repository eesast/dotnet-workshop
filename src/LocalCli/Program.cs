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
            var _logfiles = analyzer.GetLogFiles();
            foreach (var file in _logfiles)
            {
                Console.WriteLine(file);
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input the degree of parallelism (0 means auto):");
                Console.Write(">>> ");
                Console.Out.Flush();
                var str = Console.ReadLine();
                if (str is null)
                {
                    return 0;
                }
                if (int.TryParse(str, out var degree) && degree >= 0)
                {
                    return degree;
                }
                Console.WriteLine("Invalid input, please try again.");
            }
        }

        private static List<string> ReadFileNames()
        {
            Console.WriteLine("Please input file names to analyze, separated by commas:");
            Console.Write(">>> ");
            Console.Out.Flush();
            var str = Console.ReadLine();
            if (str is null)
            {
                return [];
            }
            return [.. str.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            var fileNames = ReadFileNames();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("No file names input.");
                return;
            }

            try
            {
                analyzer.AnalyzeFiles(degreeOfParallelism, fileNames);
                Console.WriteLine("Analysis finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            try
            {
                analyzer.AnalyzeAll(degreeOfParallelism);
                Console.WriteLine("Analysis finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input the file name:");
            Console.Write(">>> ");
            Console.Out.Flush();
            var fileName = Console.ReadLine();
            if (fileName is null)
            {
                return;
            }

            if (!analyzer.TryGetAnalysisResult(fileName, out var result))
            {
                Console.WriteLine($"File '{fileName}' not found.");
                return;
            }

            switch (result!.State)
            {
                case AnalysisState.NotAnalyzed:
                    Console.WriteLine($"File '{fileName}' has not been analyzed.");
                    break;
                case AnalysisState.Succeeded:
                    var dumper = new KeyValueVisitor();
                    foreach (var entry in result.Entries)
                    {
                        var kvPairs = dumper.Dump(entry);
                        Console.WriteLine(string.Join(", ", kvPairs.Select(kv => $"{kv.Key}: {kv.Value}")));
                    }
                    break;
                case AnalysisState.Failed:
                    Console.WriteLine($"Analysis of file '{fileName}' failed: {result.ErrorMessage}");
                    break;
            }
        }
    }
}
