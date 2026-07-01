using CsvHelper;
using CsvHelper.Configuration;
using LogParser.Models;
using System.Globalization;

namespace LogParser.Parser
{
    internal class LogRecord
    {
        public int LineNo { get; set; } = -1;
        public string Timestamp { get; set; } = "";
        public string PodName { get; set; } = "";
        public string Message { get; set; } = "";
    }

    internal class LogRecordMap : ClassMap<LogRecord>
    {
        public LogRecordMap()
        {
            Map(m => m.LineNo).Index(0);
            Map(m => m.Timestamp).Index(1);
            Map(m => m.PodName).Index(2);
            Map(m => m.Message).Index(3);
        }
    }

    public sealed class LogFileParser
    {
        public IEnumerable<LogEntry> Parse(TextReader logFile)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false
            };
            using var csv = new CsvReader(logFile, config);
            csv.Context.RegisterClassMap<LogRecordMap>();
            
            foreach (var logRecord in csv.GetRecords<LogRecord>())
            {
                yield return LineParser.ParseLine(logRecord);
            }
        }
    }
}
