using System.Net;
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
            var files=analyzer.GetLogFiles();
            if (files.Count == 0)
            {
                Console.WriteLine("No log files found in the current directory.");
                return;
            }
            int index=1;
            foreach (var file in files)
            {
                Console.WriteLine($"{index}.{file}");
                index++;
            }
        }

        private static void AnalyzeFiles(LogFileAnalyzer analyzer)
        {   while(true){
            Console.WriteLine("Please input log files to analyze (separated by comma):");
            var str=Console.ReadLine();
            if (str is null)
            {
                return;
            }
            var selectedFiles = str.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToArray();
            if (selectedFiles.Length == 0)
            {
                Console.WriteLine("No files specified, please try again.");
                continue;
            }
            int degree = 0; 
            while (true) 
            {
                Console.WriteLine("Please input degree of parallelism:");
                var degreeStr = Console.ReadLine();
                if (degreeStr is null) return;
                if (!int.TryParse(degreeStr, out degree))
                {
                    Console.WriteLine("Invalid number, please try again.");
                    continue; 
                }
                break; 
            }
            try
                {
                    analyzer.AnalyzeFiles(degree,selectedFiles);
                    break;
                }
                catch (InvalidOperationException)
                {
                    Console.WriteLine("Analysis is already running, please wait.");
                    break;
                }
                catch(ArgumentException)
                {
                    Console.WriteLine("Invalid files, please try again.");
                    continue;
                }
            }
 
        }


        private static void AnalyzeAll(LogFileAnalyzer analyzer)
        {
            while (true)
            {
                Console.WriteLine("Please input degree of parallelism:");
                var degreeStr = Console.ReadLine();
                if (degreeStr is null)
                {
                    return; 
                }
                if (!int.TryParse(degreeStr, out int degree))
                {
                    Console.WriteLine("Invalid number, please try again.");
                    continue; 
                }
                try
                {
                    analyzer.AnalyzeAll(degree);
                    break; 
                }
                catch (InvalidOperationException)
                {
                    Console.WriteLine("Analysis is already running, please wait.");
                    break; 
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Invalid degree of parallelism, please try again.");
                    continue; 
                }
            }
        }

        private static void GetAnalysisResult(LogFileAnalyzer analyzer)
        {
            while (true)
            {
                Console.WriteLine("Please input log file name:");
                var fileName = Console.ReadLine();
                if (fileName is null)
                {
                    return;
                }
                fileName = fileName.Trim();
                if (fileName.Length == 0)
                {
                    Console.WriteLine("Invalid file name, please try again.");
                    continue;
                }
                if (!analyzer.TryGetAnalysisResult(fileName, out var result))
                {
                    Console.WriteLine("File not found, please try again.");
                    continue;
                }
                if (result is null)
                {
                    Console.WriteLine("File not found, please try again.");
                    continue;
                }
                switch (result.State)
                {
                    case AnalysisState.NotAnalyzed:
                        Console.WriteLine("Not analyzed.");
                        break;
                    case AnalysisState.Failed:
                        Console.WriteLine($"Failed: {result.ErrorMessage}");
                        break;
                    case AnalysisState.Succeeded:
                        var visitor = new KeyValueVisitor();
                        foreach (var entry in result.Entries)
                            {
                                var logDict = visitor.Dump(entry);
                                foreach (var kvp in logDict)
                                {
                                    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
                                }
                            }
                        break;

                }
                break;
            }
        }
    }
}
