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

                if (string.IsNullOrWhiteSpace(directory))
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
                catch (Exception ex) when (ex is ArgumentException
                    or IOException
                    or NotSupportedException
                    or UnauthorizedAccessException)
                {
                    Console.WriteLine($"Directory illegal: {ex.Message}, please try again:");
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
            var fileNames = analyzer.GetLogFiles();
            Console.WriteLine($"[{string.Join(", ", fileNames)}]");
        }

        private static int? ReadDegreeOfParallelism()
        {
            Console.WriteLine("Please input degree of parallelism (0 uses the logical processor count):");
            var input = Console.ReadLine();
            if (input is null)
            {
                return null;
            }

            if (!int.TryParse(input.Trim(), out var degreeOfParallelism)
                || degreeOfParallelism < 0)
            {
                Console.WriteLine("Degree of parallelism must be a non-negative integer.");
                return null;
            }

            return degreeOfParallelism;
        }

        private static List<string>? ReadFileNames()
        {
            Console.WriteLine("Please input log file names (comma separated):");
            var input = Console.ReadLine();
            if (input is null)
            {
                return null;
            }

            var fileNames = input
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
            if (fileNames.Count == 0)
            {
                Console.WriteLine("At least one log file name is required.");
                return null;
            }

            return fileNames;
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            if (degreeOfParallelism is null)
            {
                return;
            }

            var fileNames = ReadFileNames();
            if (fileNames is null)
            {
                return;
            }

            try
            {
                analyzer.AnalyzeFiles(degreeOfParallelism.Value, fileNames);
                Console.WriteLine($"Analysis completed: [{string.Join(", ", fileNames)}]");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Console.WriteLine($"Unable to analyze files: {ex.Message}");
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            var degreeOfParallelism = ReadDegreeOfParallelism();
            if (degreeOfParallelism is null)
            {
                return;
            }

            try
            {
                analyzer.AnalyzeAll(degreeOfParallelism.Value);
                Console.WriteLine($"Analysis completed: [{string.Join(", ", analyzer.GetLogFiles())}]");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Console.WriteLine($"Unable to analyze files: {ex.Message}");
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input log file name:");
            var fileName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("Log file name cannot be empty.");
                return;
            }

            fileName = fileName.Trim();
            if (!analyzer.TryGetAnalysisResult(fileName, out var result) || result is null)
            {
                Console.WriteLine($"File {fileName} does not exist in the current directory.");
                return;
            }

            switch (result.State)
            {
                case AnalysisState.NotAnalyzed:
                    Console.WriteLine($"File {fileName} has not been analyzed yet.");
                    break;
                case AnalysisState.Succeeded:
                    Console.WriteLine($"Analysis result for {fileName}:");
                    var visitor = new KeyValueVisitor();
                    foreach (var entry in result.Entries)
                    {
                        var keyValues = visitor.Dump(entry);
                        Console.WriteLine(string.Join(", ",
                            keyValues.Select(keyValue => $"{keyValue.Key}: {keyValue.Value}")));
                    }
                    break;
                case AnalysisState.Failed:
                    Console.WriteLine($"Analysis failed for {fileName}: {result.ErrorMessage}");
                    break;
                default:
                    Console.WriteLine($"File {fileName} has an unknown analysis state.");
                    break;
            }
        }
    }
}
