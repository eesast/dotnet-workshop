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
            foreach (var file in analyzer.GetLogFiles())
            {
                Console.WriteLine(file);
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Degree of parrallelism:");
            var parrallelism = Console.ReadLine();
            if (parrallelism == null)
            {
                return;
            }
            Console.WriteLine("Filenames, split with ',':");
            var filesStr = Console.ReadLine();
            if (filesStr == null)
            {
                return;
            }
            try
            {
                analyzer.AnalyzeFiles(int.Parse(parrallelism), filesStr.Split(',').Select(x => x.Trim()));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Degree of parrallelism:");
            var input = Console.ReadLine();
            if (input == null)
            {
                return;
            }
            int degreeOfParallelism;
            try
            {
                degreeOfParallelism = int.Parse(input);
                analyzer.AnalyzeAll(degreeOfParallelism);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            Console.WriteLine("Filename:");
            var filename = Console.ReadLine().Trim();
            if (filename == null)
            {
                return;
            }
            if (analyzer.TryGetAnalysisResult(filename, out AnalysisResult? result))
            {
                if (result.State == AnalysisState.Succeeded)
                {
                    var visitor = new KeyValueVisitor();
                    foreach (var entry in result.Entries)
                    {
                        var dumpedInfo = visitor.Dump(entry);
                        foreach (var item in dumpedInfo)
                        {
                            Console.Write($"[{item.Key}] {item.Value} ");
                        }
                        Console.WriteLine();
                    }
                }
                else if (result.State == AnalysisState.Failed)
                {
                    Console.WriteLine("Analysis failed");
                }
                else if (result.State == AnalysisState.NotAnalyzed)
                {
                    Console.WriteLine("Not analyzed yet");
                }
            }
            else
            {
                Console.WriteLine($"No parse result for file {filename}.");
            }
        }
    }
}
