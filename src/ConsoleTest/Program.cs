using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using LogParser.Parser;
using LogParser.Visitors;
using LogAnalyzer;

Console.WriteLine("Hello, ConsoleTest of dotnet-workshop!");
Console.WriteLine();

{
    var parser = new LogFileParser();
    var results = parser.Parse(new StreamReader("dataset/basic.log")).ToList();
    var kvdumper = new KeyValueVisitor();
    var kvresults = results.Select(entry => kvdumper.Dump(entry)).ToList();

    Console.WriteLine(string.Join("\n", kvresults.Select(kvresult => string.Join(", ", kvresult.Select(kv => $"{kv.Key}: {kv.Value}")))));
    Console.WriteLine();
}

{
    var analyzer = new LogFileAnalyzer("dataset");
    Console.WriteLine(analyzer.CurrentDirectory);
    Console.WriteLine(string.Join(", ", analyzer.GetLogFiles()));
    analyzer.AnalyzeAll(4);
    analyzer.TryGetAnalysisResult("basic.log", out var analysisResults);
    var kvdumper = new KeyValueVisitor();
    var kvresults = analysisResults!.Entries.Select(entry => kvdumper.Dump(entry)).ToList();
    Console.WriteLine(string.Join("\n", kvresults.Select(kvresult => string.Join(", ", kvresult.Select(kv => $"{kv.Key}: {kv.Value}")))));
    Console.WriteLine();
}

{
    var strs = (IEnumerable<string>)new List<string>() { "a", "b", "c" };
    Console.WriteLine(strs.FirstOrDefault());
    Console.WriteLine(strs.ToList().Count);
}
