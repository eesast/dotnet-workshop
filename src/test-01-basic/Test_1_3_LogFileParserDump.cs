using LogParser.Models;
using LogParser.Parser;
using LogParser.Visitors;
using TestUtils;
using TestUtils.dataset;

namespace test_01_basic
{
    [TestClass]
    public sealed class Test_1_3_LogFileParserDump
    {
        private static void VerifyLogEntryDumper(LogEntry targetEntry, Dictionary<string, string> kvresult)
        {
            Assert.AreEqual(targetEntry.LineNo, int.Parse(kvresult["LineNo"]),
                $"Expected LineNo to be {targetEntry.LineNo}, got {kvresult["LineNo"]}");
            Assert.AreEqual(targetEntry.Timestamp, DateTimeOffset.Parse(kvresult["Timestamp"]),
                $"Expected Timestamp to be {targetEntry.Timestamp}, got {kvresult["Timestamp"]}, at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.PodName, kvresult["PodName"],
                $"Expected PodName to be '{targetEntry.PodName}', got '{kvresult["PodName"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.Severity, Enum.Parse<LogSeverity>(kvresult["Severity"]),
                $"Expected Severity to be {targetEntry.Severity}, got {kvresult["Severity"]}, at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.EventType, Enum.Parse<LogEventType>(kvresult["EventType"]),
                $"Expected EventType to be {targetEntry.EventType}, got {kvresult["EventType"]}, at line {targetEntry.LineNo}");
        }


        private static void VerifyCallLogEntryDumper(CallLogEntry targetEntry, Dictionary<string, string> kvresult)
        {
            VerifyLogEntryDumper(targetEntry, kvresult);
            Assert.AreEqual(targetEntry.RequestId, kvresult["RequestId"],
                $"Expected RequestId to be '{targetEntry.RequestId}', got '{kvresult["RequestId"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.TargetService, kvresult["TargetService"],
                $"Expected TargetService to be '{targetEntry.TargetService}', got '{kvresult["TargetService"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.DurationMs, int.Parse(kvresult["DurationMs"]),
                $"Expected DurationMs to be {targetEntry.DurationMs}, got {kvresult["DurationMs"]}, at line {targetEntry.LineNo}");
        }

        private static void VerifyRequestLogEntryDumper(RequestLogEntry targetEntry, Dictionary<string, string> kvresult)
        {
            VerifyLogEntryDumper(targetEntry, kvresult);
            Assert.AreEqual(targetEntry.RequestId, kvresult["RequestId"],
                $"Expected RequestId to be '{targetEntry.RequestId}', got '{kvresult["RequestId"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.Method, kvresult["Method"],
                $"Expected Method to be '{targetEntry.Method}', got '{kvresult["Method"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.Path, kvresult["Path"],
                $"Expected Path to be '{targetEntry.Path}', got '{kvresult["Path"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.StatusCode, int.Parse(kvresult["StatusCode"]),
                $"Expected StatusCode to be {targetEntry.StatusCode}, got {kvresult["StatusCode"]}, at line {targetEntry.LineNo}");
        }

        private static void VerifyInternalLogEntryDumper(InternalLogEntry targetEntry, Dictionary<string, string> kvresult)
        {
            VerifyLogEntryDumper(targetEntry, kvresult);
            Assert.AreEqual(targetEntry.ExceptionName, kvresult["ExceptionName"],
                $"Expected ExceptionName to be '{targetEntry.ExceptionName}', got '{kvresult["ExceptionName"]}', at line {targetEntry.LineNo}");
            Assert.AreEqual(targetEntry.ExceptionMessage, kvresult["ExceptionMessage"],
                $"Expected ExceptionMessage to be '{targetEntry.ExceptionMessage}', got '{kvresult["ExceptionMessage"]}', at line {targetEntry.LineNo}");
        }

        [TestMethod(DisplayName = "T1.3.1 TestDumpCallLogEntry")]
        public void TestDumpCallLogEntry()
        {
            var parser = new LogFileParser();
            var result = parser.Parse(new StringReader(TestUtilsClass.CallLogExample)).ToList();
            var kvdumper = new KeyValueVisitor();
            var kvresult = kvdumper.Dump(result[0]);
            VerifyCallLogEntryDumper(TestUtilsClass.CallLogExampleEntry, kvresult);
        }

        [TestMethod(DisplayName = "T1.3.2 TestDumpRequestLogEntry")]
        public void TestDumpRequestLogEntry()
        {
            var parser = new LogFileParser();
            var result = parser.Parse(new StringReader(TestUtilsClass.RequestLogExample)).ToList();
            var kvdumper = new KeyValueVisitor();
            var kvresult = kvdumper.Dump(result[0]);
            VerifyRequestLogEntryDumper(TestUtilsClass.RequestLogExampleEntry, kvresult);
        }

        [TestMethod(DisplayName = "T1.3.3 TestDumpInternalLogEntry")]
        public void TestDumpInternalLogEntry()
        {
            var parser = new LogFileParser();
            var result = parser.Parse(new StringReader(TestUtilsClass.InternalLogExample)).ToList();
            var kvdumper = new KeyValueVisitor();
            var kvresult = kvdumper.Dump(result[0]);
            VerifyInternalLogEntryDumper(TestUtilsClass.InternalLogExampleEntry, kvresult);
        }

        [TestMethod(DisplayName = "T1.3.4 TestDumpMultipleLogEntries")]
        public void TestDumpMultipleLogEntries()
        {
            var parser = new LogFileParser();
            var logEntries = new List<string>
            {
                TestUtilsClass.CallLogExample,
                TestUtilsClass.RequestLogExample,
                TestUtilsClass.InternalLogExample
            };
            var results = parser.Parse(new StringReader(string.Join(Environment.NewLine, logEntries))).ToList();
            var kvdumper = new KeyValueVisitor();
            var kvresults = results.Select(result => kvdumper.Dump(result!)).ToList();

            VerifyCallLogEntryDumper(TestUtilsClass.CallLogExampleEntry, kvresults[0]);
            VerifyRequestLogEntryDumper(TestUtilsClass.RequestLogExampleEntry, kvresults[1]);
            VerifyInternalLogEntryDumper(TestUtilsClass.InternalLogExampleEntry, kvresults[2]);
        }

        [TestMethod(DisplayName = "T1.3.5 TestDumpLogEntriesInFile")]
        public void TestDumpLogEntriesInFile()
        {
            var parser = new LogFileParser();
            List<LogEntry> results = new();
            using (var reader = new StreamReader("dataset/basic.log"))
            {
                results = parser.Parse(reader).ToList();
            }
            var kvdumper = new KeyValueVisitor();
            var kvresults = results.Select(result => kvdumper.Dump(result!)).ToList();

            VerifyCallLogEntryDumper(TestUtilsClass.CallLogExampleEntry, kvresults[0]);
            VerifyRequestLogEntryDumper(TestUtilsClass.RequestLogExampleEntry, kvresults[1]);
            VerifyInternalLogEntryDumper(TestUtilsClass.InternalLogExampleEntry, kvresults[2]);
        }

        [TestMethod(DisplayName = "T1.3.6 TestDumpMultipleLogEntriesInFile")]
        public void TestDumpMultipleLogEntriesInFile()
        {
            var parser = new LogFileParser();
            List<LogEntry> results = new();
            using (var reader = new StreamReader("dataset/basic-multiple.log"))
            {
                results = parser.Parse(reader).ToList();
            }
            Assert.IsNotNull(results, "Parse result should not be null");

            var kvdumper = new KeyValueVisitor();
            var kvresults = results.Select(result => kvdumper.Dump(result!)).ToList();

            var targetLogEntries = BasicMultiple.LogData;
            Assert.HasCount(targetLogEntries.Count, kvresults, $"Parse result should contain exactly {targetLogEntries.Count} entries, got {kvresults.Count}");

            // Zip together target and actual entries to validate them in pairs
            foreach (var ((targetEventType, targetLogEntry), kvdict) in targetLogEntries.Zip(kvresults, (target, actual) => (Target: target, Actual: actual)))
            {
                switch (targetEventType)
                {
                    case LogEventType.Call:
                        VerifyCallLogEntryDumper((CallLogEntry)targetLogEntry, kvdict);
                        break;
                    case LogEventType.Request:
                        VerifyRequestLogEntryDumper((RequestLogEntry)targetLogEntry, kvdict);
                        break;
                    case LogEventType.Internal:
                        VerifyInternalLogEntryDumper((InternalLogEntry)targetLogEntry, kvdict);
                        break;
                    default:
                        Assert.Fail($"Unknown log event type: {targetEventType}, at line {targetLogEntry.LineNo}");
                        break;
                }
            }
        }
    }
}
