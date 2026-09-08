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
                directory = directory.Trim();
                if (directory.Length == 0)
                {
                    Console.WriteLine("Directory cannot be empty, please try again:");
                    continue;
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
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to open directory: {ex.Message}");
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
            Console.WriteLine($"[{string.Join(", ", files)}]");
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            try
            {
                var degreeOfParallelism = ReadDegreeOfParallelism();
                var fileNames = ReadFileNames();

                analyzer.AnalyzeFiles(degreeOfParallelism, fileNames);
                Console.WriteLine($"Analysis completed: [{string.Join(", ", fileNames)}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            try
            {
                var degreeOfParallelism = ReadDegreeOfParallelism();
                var fileNames = analyzer.GetLogFiles();

                analyzer.AnalyzeAll(degreeOfParallelism);
                Console.WriteLine($"Analysis completed: [{string.Join(", ", fileNames)}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input log file name:");
            var input = Console.ReadLine();
            if (input is null)
            {
                return;
            }

            var fileName = input.Trim();
            if (fileName.Length == 0)
            {
                Console.WriteLine("File name cannot be empty.");
                return;
            }

            if (!analyzer.TryGetAnalysisResult(fileName, out var result) || result is null)
            {
                Console.WriteLine($"File {fileName} does not exist.");
                return;
            }

            switch (result.State)
            {
                case AnalysisState.NotAnalyzed:
                    Console.WriteLine($"File {fileName} has not been analyzed yet.");
                    break;

                case AnalysisState.Failed:
                    Console.WriteLine(
                        $"Analysis failed for {fileName}: {result.ErrorMessage ?? "Unknown error"}");
                    break;

                case AnalysisState.Succeeded:
                    Console.WriteLine($"Analysis result for {fileName}:");
                    var visitor = new KeyValueVisitor();
                    foreach (var entry in result.Entries)
                    {
                        var values = visitor.Dump(entry);
                        Console.WriteLine(string.Join(", ",
                            values.Select(pair => $"{pair.Key}: {pair.Value}")));
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown analysis state for {fileName}: {result.State}");
                    break;
            }
        }

        private static int ReadDegreeOfParallelism()
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism:");
                var input = Console.ReadLine();
                if (input is null)
                {
                    throw new EndOfStreamException("Input ended.");
                }

                if (int.TryParse(input.Trim(), out var degreeOfParallelism)
                    && degreeOfParallelism >= 0)
                {
                    return degreeOfParallelism;
                }

                Console.WriteLine("Invalid degree of parallelism, please try again:");
            }
        }

        private static List<string> ReadFileNames()
        {
            while (true)
            {
                Console.WriteLine("Please input log file names (comma separated):");
                var input = Console.ReadLine();
                if (input is null)
                {
                    throw new EndOfStreamException("Input ended.");
                }

                var fileNames = input
                    .Split(',')
                    .Select(fileName => fileName.Trim())
                    .Where(fileName => fileName.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (fileNames.Count > 0)
                {
                    return fileNames;
                }

                Console.WriteLine("No log file names provided, please try again:");
            }
        }
    }
}
