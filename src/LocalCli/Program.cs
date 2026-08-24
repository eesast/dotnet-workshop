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
                        actions[choice](analyzer);
                        break;
                    case 2:
                        actions[choice](analyzer);
                        break;
                    case 3:
                        actions[choice](analyzer);
                        break;
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
            analyzer.GetLogFiles().ToList().ForEach(fileName => Console.WriteLine(fileName));
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input log file names, separated by space:");
            var input = Console.ReadLine();
            if (input is null)
            {
                return;
            }
            var fileNames = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            Console.WriteLine("Please input degree of parallelism:");
            var degreeOfParallelismStr = Console.ReadLine();
            if (degreeOfParallelismStr is null)
            {
                return;
            }
            if (!int.TryParse(degreeOfParallelismStr, out var degreeOfParallelism))
            {
                Console.WriteLine("Invalid input for degree of parallelism.");
                return;
            }
            analyzer.AnalyzeFiles(degreeOfParallelism, fileNames);
            Console.WriteLine("Done.");
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Please input degree of parallelism:");
            var degreeOfParallelismStr = Console.ReadLine();
            if (degreeOfParallelismStr is null)
            {
                return;
            }
            if (!int.TryParse(degreeOfParallelismStr, out var degreeOfParallelism))
            {
                Console.WriteLine("Invalid input for degree of parallelism.");
                return;
            }
            analyzer.AnalyzeAll(degreeOfParallelism);
            Console.WriteLine("Done.");
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            analyzer.GetLogFiles().ToList().ForEach(fileName =>
            {
                if (analyzer.TryGetAnalysisResult(fileName, out var result))
                {
                    if (result == null)
                    {
                        Console.WriteLine($"No analysis result for file: {fileName}");
                    }
                    Console.WriteLine($"File: {fileName}");
                    Console.WriteLine($"State: {result.State}");
                    if (result.State == AnalysisState.Failed)
                    {
                        Console.WriteLine($"Error Message: {result.ErrorMessage}");
                    }
                    else
                    {
                        Console.WriteLine($"Log Entries Count: {result.Entries.Count}");
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"No analysis result for file: {fileName}");
                }
            });
        }
    }
}
