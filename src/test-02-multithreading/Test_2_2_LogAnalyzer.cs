using LogAnalyzer;
using LogParser.Models;
using System.Dynamic;
using System.Net.WebSockets;
using TestUtils;
using TestUtils.dataset;

namespace test_02_multithreading
{
    [TestClass]
    public sealed class Test_2_2_LogAnalyzer
    {
        [TestMethod(DisplayName = "T2.2.1 TestBasic")]
        [Timeout(1000, CooperativeCancellation = true)]
        public void TestBasic()
        {
            var analyzer = new LogFileAnalyzer("dataset");
            analyzer.AnalyzeFiles(1, ["basic.log", "basic-multiple.log"]);
            {
                // Verify analysis result for 'basic.log'

                Assert.IsTrue(analyzer.TryGetAnalysisResult("basic.log", out var result)
                    , $"Failed to get analysis result for 'basic.log'.");
                Assert.AreEqual(AnalysisState.Succeeded, result!.State,
                    $"Unexpected analysis state for 'basic.log', expected: {AnalysisState.Succeeded}, actual: {result.State}.");

                var entries = result.Entries;
                Assert.HasCount(3, entries,
                    $"Expected {3} log entries in 'basic.log', but found {result.Entries.Count}.");

                // Validate each entry type
                Assert.IsInstanceOfType(entries[0], typeof(CallLogEntry),
                    $"The first entry should be of type CallLogEntry, got {entries[0].GetType().Name}");
                Assert.IsInstanceOfType(entries[1], typeof(RequestLogEntry),
                    $"The second entry should be of type RequestLogEntry, got {entries[1].GetType().Name}");
                Assert.IsInstanceOfType(entries[2], typeof(InternalLogEntry),
                    $"The third entry should be of type InternalLogEntry, got {entries[2].GetType().Name}");

                TestUtilsClass.VerifyCallLogEntryExample(entries[0]);
                TestUtilsClass.VerifyRequestLogEntryExample(entries[1]);
                TestUtilsClass.VerifyInternalLogEntryExample(entries[2]);
            }
            {
                // Verify analysis result for 'basic-multiple.log'

                Assert.IsTrue(analyzer.TryGetAnalysisResult("basic-multiple.log", out var result)
                    , $"Failed to get analysis result for 'basic-multiple.log'.");
                Assert.AreEqual(AnalysisState.Succeeded, result!.State,
                    $"Unexpected analysis state for 'basic-multiple.log', expected: {AnalysisState.Succeeded}, actual: {result.State}.");

                var entries = result.Entries;
                Assert.IsNotNull(entries, $"Analysis result for 'basic-multiple.log' should not be null.");
                TestUtilsClass.VerifyLogFile(BasicMultiple.LogData, entries);
            }
        }

        [TestMethod(DisplayName = "T2.2.2 TestFailed")]
        [Timeout(1000, CooperativeCancellation = true)]
        public void TestFailed()
        {
            var analyzer = new LogFileAnalyzer("dataset");
            analyzer.AnalyzeAll(4);
            {
                // Verify analysis result for 'basic-fail.log'

                Assert.IsTrue(analyzer.TryGetAnalysisResult("basic-fail.log", out var result)
                    , $"Failed to get analysis result for 'basic-fail.log'.");
                Assert.AreEqual(AnalysisState.Failed, result!.State,
                    $"Unexpected analysis state for 'basic-fail.log', expected: {AnalysisState.Failed}, actual: {result.State}.");
            }
        }

        [TestMethod(DisplayName = "T2.2.3 TestMultipleFiles")]
        [Timeout(240000, CooperativeCancellation = false)]
        public void TestMultipleFiles()
        {
            const int nIter = 4;
            for (int iter = 0; iter < nIter; ++iter)
            {
                var analyzer = new LogFileAnalyzer("dataset/multiple-logs");
                analyzer.AnalyzeAll(4);
                Assert.IsFalse(analyzer.IsAnalyzing, $"Analyzer should not be in analyzing state after calling AnalyzeAll.");
                for (int i = 1; i < 31; ++i)
                {
                    var dateStr = $"202607{i:D2}";
                    var fileName = $"{dateStr}.log";
                    int count = 40000 + int.Parse(dateStr) - 20260601;

                    Assert.IsTrue(analyzer.TryGetAnalysisResult(fileName, out var result),
                        $"Failed to get analysis result for '{fileName}'.");
                    Assert.AreEqual(AnalysisState.Succeeded, result!.State,
                        $"Unexpected analysis state for '{fileName}', expected: {AnalysisState.Succeeded}, actual: {result.State}.");
                    Assert.IsNotNull(result!.Entries, $"Analysis result for '{fileName}' should not be null.");
                    Assert.HasCount(count, result.Entries,
                        $"Expected {count} log entries in '{fileName}', but found {result.Entries.Count}.");
                }
            }
        }

        [TestMethod(DisplayName = "T2.2.4 TestMultipleAnalysis")]
        [Timeout(240000, CooperativeCancellation = false)]
        public void TestMultipleAnalysis()
        {
            const int nIter = 16;
            var threads = new Thread[nIter];
            var states = new int[nIter];
            for (int iter = 0; iter < nIter; ++iter)
            {
                states[iter] = 0;
            }

            int analyzedCount = 0;
            int notAnalyzedCount = 0;
            var analyzer = new LogFileAnalyzer("dataset/multiple-logs");
            for (int iter = 0; iter < nIter; ++iter)
            {
                int threadIdx = iter;
                threads[iter] = new Thread(() =>
                {
                    try
                    {
                        analyzer.AnalyzeAll(4);
                        states[threadIdx] = 1;
                        ++analyzedCount;
                    }
                    catch (Exception)
                    {
                        states[threadIdx] = -1;
                        ++notAnalyzedCount;
                    }
                });
                threads[iter].Start();
            }
            for (int iter = 0; iter < nIter; ++iter)
            {
                threads[iter].Join();
            }

            List<int> analyzedIndex = new(), notAnalyzedIndex = new();
            for (int i = 0; i < nIter; ++i)
            {
                switch (states[i])
                {
                    case 1:
                        analyzedIndex.Add(i);
                        break;
                    case -1:
                        notAnalyzedIndex.Add(i);
                        break;
                }
            }
            var analyzedIdxStr = string.Join(", ", analyzedIndex);
            var notAnalyzedIdxStr = string.Join(", ", notAnalyzedIndex);
            Assert.AreEqual(1, analyzedCount,
                $"Expected exactly one successful analysis, but found {analyzedCount}, analyzed thread indices: {analyzedIdxStr}.");
            Assert.AreEqual(nIter - 1, notAnalyzedCount,
                $"Expected {nIter - 1} failed analyses, but found {notAnalyzedCount}, not analyzed thread indices: {notAnalyzedIdxStr}.");
        }

        [TestMethod(DisplayName = "T2.2.5 TestParallelAcceleration")]
        [Timeout(120000, CooperativeCancellation = false)]
        public void TestParallelAcceleration()
        {
            int degree1 = 2;
            int degree2 = 8;
            if (Environment.ProcessorCount < 4)
            {
                // Skip this test if the machine has less than 4 logical processors,
                // as it may not show meaningful parallel acceleration.
                return;
            }
            else if (Environment.ProcessorCount < 8)
            {
                degree1 = 1;
                degree2 = 4;
            }

            double time1 = 0;
            double time2 = 0;
            {
                var analyzer = new LogFileAnalyzer("dataset/multiple-logs");
                var startTime = DateTime.Now;
                analyzer.AnalyzeAll(degree1);
                var endTime = DateTime.Now;
                time1 = (double)(endTime - startTime).TotalMilliseconds;
                for (int i = 1; i < 31; ++i)
                {
                    var dateStr = $"202607{i:D2}";
                    var fileName = $"{dateStr}.log";
                    int count = 40000 + int.Parse(dateStr) - 20260601;

                    Assert.IsTrue(analyzer.TryGetAnalysisResult(fileName, out var result),
                        $"Failed to get analysis result for '{fileName}'.");
                    Assert.AreEqual(AnalysisState.Succeeded, result!.State,
                        $"Unexpected analysis state for '{fileName}', expected: {AnalysisState.Succeeded}, actual: {result.State}.");
                    Assert.IsNotNull(result!.Entries, $"Analysis result for '{fileName}' should not be null.");
                    Assert.HasCount(count, result.Entries,
                        $"Expected {count} log entries in '{fileName}', but found {result.Entries.Count}.");
                }
            }
            {
                var analyzer = new LogFileAnalyzer("dataset/multiple-logs");
                var startTime = DateTime.Now;
                analyzer.AnalyzeAll(degree2);
                var endTime = DateTime.Now;
                time2 = (double)(endTime - startTime).TotalMilliseconds;
                for (int i = 1; i < 31; ++i)
                {
                    var dateStr = $"202607{i:D2}";
                    var fileName = $"{dateStr}.log";
                    int count = 40000 + int.Parse(dateStr) - 20260601;

                    Assert.IsTrue(analyzer.TryGetAnalysisResult(fileName, out var result),
                        $"Failed to get analysis result for '{fileName}'.");
                    Assert.AreEqual(AnalysisState.Succeeded, result!.State,
                        $"Unexpected analysis state for '{fileName}', expected: {AnalysisState.Succeeded}, actual: {result.State}.");
                    Assert.IsNotNull(result!.Entries, $"Analysis result for '{fileName}' should not be null.");
                    Assert.HasCount(count, result.Entries,
                        $"Expected {count} log entries in '{fileName}', but found {result.Entries.Count}.");
                }
            }
            Assert.IsLessThan(time1 * 0.75, time2,
                $"Expected the 2nd with degree {degree2} to be faster than the 1st {degree1}. First: {time1} ms, second: {time2} ms.");
        }
    }
}
