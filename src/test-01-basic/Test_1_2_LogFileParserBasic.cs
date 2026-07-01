using LogParser.Models;
using LogParser.Parser;
using System.Text.Json;
using TestUtils;
using TestUtils.dataset;

namespace test_01_basic
{
    [TestClass]
    public sealed class Test_1_2_LogFileParserBasic
    {
        [TestMethod(DisplayName = "T1.2.1 TestParseCallLogEntry")]
        public void TestParseCallLogEntry()
        {
            var parser = new LogFileParser();
            var result = parser.Parse(new StringReader(TestUtilsClass.CallLogExample)).ToList();

            Assert.IsNotNull(result, "Parse result should not be null");
            Assert.HasCount(1, result, $"Parse result should contain exactly 1 entry, got {result.Count}");

            TestUtilsClass.VerifyCallLogEntryExample(result[0]);
        }

        [TestMethod(DisplayName = "T1.2.2 TestParseRequestLogEntry")]
        public void TestParseRequestLogEntry()
        {
            var parser = new LogFileParser();
            var result = parser.Parse(new StringReader(TestUtilsClass.RequestLogExample)).ToList();

            Assert.IsNotNull(result, "Parse result should not be null");
            Assert.HasCount(1, result, $"Parse result should contain exactly 1 entry, got {result.Count}");

            TestUtilsClass.VerifyRequestLogEntryExample(result[0]);
        }

        [TestMethod(DisplayName = "T1.2.3 TestParseInternalLogEntry")]
        public void TestParseInternalLogEntry()
        {
            var parser = new LogFileParser();
            var result = parser.Parse(new StringReader(TestUtilsClass.InternalLogExample)).ToList();

            Assert.IsNotNull(result, "Parse result should not be null");
            Assert.HasCount(1, result, $"Parse result should contain exactly 1 entry, got {result.Count}");

            TestUtilsClass.VerifyInternalLogEntryExample(result[0]);
        }

        [TestMethod(DisplayName = "T1.2.4 TestParseNoEventLogEntryFailed")]
        public void TestParseNoEventLogEntryFailed()
        {
            var parser = new LogFileParser();
            Assert.Throws<FormatException>(() => parser.Parse(new StringReader(TestUtilsClass.NoEventExampleFailed)).ToList(),
                "Parsing a log entry with no event type should throw a FormatException");
        }

        [TestMethod(DisplayName = "T1.2.5 TestParseCallLogExampleFailed")]
        public void TestParseCallLogExampleFailed()
        {
            var parser = new LogFileParser();
            Assert.Throws<JsonException>(() => parser.Parse(new StringReader(TestUtilsClass.CallLogExampleFailed)).ToList(),
                "Parsing a call log entry with no duration-ms should throw a JsonException");
        }

        [TestMethod(DisplayName = "T1.2.6 TestParseRequestLogExampleFailed")]
        public void TestParseRequestLogExampleFailed()
        {
            var parser = new LogFileParser();
            Assert.Throws<JsonException>(() => parser.Parse(new StringReader(TestUtilsClass.RequestLogExampleFailed)).ToList(),
                "Parsing a request log entry with no method should throw a JsonException");
        }

        [TestMethod(DisplayName = "T1.2.7 TestParseInternalLogExampleFailed")]
        public void TestParseInternalLogExampleFailed()
        {
            var parser = new LogFileParser();
            Assert.Throws<Exception>(() => parser.Parse(new StringReader(TestUtilsClass.InternalLogExampleFailed)).ToList(),
                "Parsing a internal log entry with an exeption in a wrong format should throw an Exception");
        }

        [TestMethod(DisplayName = "T1.2.8 TestParseMultipleLogEntries")]
        public void TestParseMultipleLogEntries()
        {
            var parser = new LogFileParser();
            var logEntries = new List<string>
            {
                TestUtilsClass.CallLogExample,
                TestUtilsClass.RequestLogExample,
                TestUtilsClass.InternalLogExample
            };
            var result = parser.Parse(new StringReader(string.Join(Environment.NewLine, logEntries))).ToList();
            Assert.IsNotNull(result, "Parse result should not be null");
            Assert.HasCount(3, result, $"Parse result should contain exactly 3 entries, got {result.Count}");

            // Validate each entry type
            Assert.IsInstanceOfType(result[0], typeof(CallLogEntry),
                $"The first entry should be of type CallLogEntry, got {result[0].GetType().Name}");
            Assert.IsInstanceOfType(result[1], typeof(RequestLogEntry),
                $"The second entry should be of type RequestLogEntry, got {result[1].GetType().Name}");
            Assert.IsInstanceOfType(result[2], typeof(InternalLogEntry),
                $"The third entry should be of type InternalLogEntry, got {result[2].GetType().Name}");

            TestUtilsClass.VerifyCallLogEntryExample(result[0]);
            TestUtilsClass.VerifyRequestLogEntryExample(result[1]);
            TestUtilsClass.VerifyInternalLogEntryExample(result[2]);
        }

        [TestMethod(DisplayName = "T1.2.9 TestParseLogEntriesInFile")]
        public void TestParseLogEntriesInFile()
        {
            var parser = new LogFileParser();
            List<LogEntry> result = new();
            using (var reader = new StreamReader("dataset/basic.log"))
            {
                result = parser.Parse(reader).ToList();
            }
            Assert.IsNotNull(result, "Parse result should not be null");
            Assert.HasCount(3, result, $"Parse result should contain exactly 3 entries, got {result.Count}");

            // Validate each entry type
            Assert.IsInstanceOfType(result[0], typeof(CallLogEntry),
                $"The first entry should be of type CallLogEntry, got {result[0].GetType().Name}");
            Assert.IsInstanceOfType(result[1], typeof(RequestLogEntry),
                $"The second entry should be of type RequestLogEntry, got {result[1].GetType().Name}");
            Assert.IsInstanceOfType(result[2], typeof(InternalLogEntry),
                $"The third entry should be of type InternalLogEntry, got {result[2].GetType().Name}");

            TestUtilsClass.VerifyCallLogEntryExample(result[0]);
            TestUtilsClass.VerifyRequestLogEntryExample(result[1]);
            TestUtilsClass.VerifyInternalLogEntryExample(result[2]);
        }

        [TestMethod(DisplayName = "T1.2.10 TestParseMultipleLogEntriesInFile")]
        public void TestParseMultipleLogEntriesInFile()
        {
            var parser = new LogFileParser();
            List<LogEntry> result = new();
            using (var reader = new StreamReader("dataset/basic-multiple.log"))
            {
                result = parser.Parse(reader).ToList();
            }
            Assert.IsNotNull(result, "Parse result should not be null");

            var targetLogEntries = BasicMultiple.LogData;
            TestUtilsClass.VerifyLogFile(targetLogEntries, result);
        }
    }
}
