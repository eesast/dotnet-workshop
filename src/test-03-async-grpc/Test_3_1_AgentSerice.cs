using Google.Protobuf.WellKnownTypes;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerAgent.Applications;
using Microsoft.Extensions.Logging.Abstractions;
using TestUtils.dataset;
using LogAnalyzerRpc;
using TestUtils;

namespace test_03_async_grpc
{
    [TestClass]
    public sealed class Test_3_1_AgentSerice
    {
        [TestMethod(DisplayName = "T3.1.1 TestPing")]
        [Timeout(1000, CooperativeCancellation = true)]
        public void TestPing()
        {
            var session = new AgentSession(new LogFileAnalyzer(), NullLoggerFactory.Instance);
            session.Ping(new Empty(), CancellationToken.None);
        }

        [TestMethod(DisplayName = "T3.1.2 TestChangeDirectory")]
        [Timeout(2000, CooperativeCancellation = true)]
        public void TestChangeDirectory()
        {
            var session = new AgentSession(new LogFileAnalyzer(), NullLoggerFactory.Instance);
            _ = session.GetLogFiles(new Empty(), CancellationToken.None);   // Should not crash
            {
                var response = session.ChangeDirectory(new ChangeDirectoryRequest()
                {
                    DirectoryPath = "dataset",
                }, CancellationToken.None).Result;
                Assert.IsTrue(response.Status.Success);
                Assert.AreEqual(AgentErrorCode.NoAgentError, response.Status.Code);

                var targetResultFiles = new List<string>()
                {
                    "basic.log",
                    "basic-fail.log",
                    "basic-multiple.log",
                };
                Assert.HasCount(targetResultFiles.Count, response.FileNames);

                var files = response.FileNames.ToHashSet();
                var filesStr = $"[{string.Join(", ", response.FileNames)}]";
                foreach (var file in targetResultFiles)
                {
                    Assert.Contains(file, files, $"File '{file}' is expected but not found in the response {filesStr}.");
                }
            }
            {
                var response = session.GetLogFiles(new Empty(), CancellationToken.None).Result;
                Assert.IsTrue(response.Status.Success);
                Assert.AreEqual(AgentErrorCode.NoAgentError, response.Status.Code);

                var targetResultFiles = new List<string>()
                {
                    "basic.log",
                    "basic-fail.log",
                    "basic-multiple.log",
                };
                Assert.HasCount(targetResultFiles.Count, response.FileNames);

                var files = response.FileNames.ToHashSet();
                var filesStr = $"[{string.Join(", ", response.FileNames)}]";
                foreach (var file in targetResultFiles)
                {
                    Assert.Contains(file, files, $"File '{file}' is expected but not found in the response {filesStr}.");
                }
            }
            {
                var response = session.ChangeDirectory(new ChangeDirectoryRequest()
                {
                    DirectoryPath = "dataset/multiple-logs",
                }, CancellationToken.None).Result;
                Assert.IsTrue(response.Status.Success);
                Assert.AreEqual(AgentErrorCode.NoAgentError, response.Status.Code);

                var targetResultFiles = new List<string>();
                for (int i = 1; i < 31; ++i)
                {
                    targetResultFiles.Add($"202607{i:D2}.log");
                }
                Assert.HasCount(targetResultFiles.Count, response.FileNames);

                var files = response.FileNames.ToHashSet();
                var filesStr = $"[{string.Join(", ", response.FileNames)}]";
                foreach (var file in targetResultFiles)
                {
                    Assert.Contains(file, files, $"File '{file}' is expected but not found in the response {filesStr}.");
                }
            }
        }

        [TestMethod(DisplayName = "T3.1.3 TestAnalyzeFiles")]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestAnalyzeFiles()
        {
            var session = new AgentSession(new LogFileAnalyzer(), NullLoggerFactory.Instance);
            session.Ping(new Empty(), CancellationToken.None);
            {
                var cdResponse = session.ChangeDirectory(new ChangeDirectoryRequest()
                {
                    DirectoryPath = "dataset",
                }, CancellationToken.None).Result;
                Assert.IsTrue(cdResponse.Status.Success);
                Assert.AreEqual(AgentErrorCode.NoAgentError, cdResponse.Status.Code);

                var targetResultFiles = new List<string>()
                {
                    "basic.log",
                    "basic-fail.log",
                    "basic-multiple.log",
                };
                var fileNames = new List<string>()
                {
                    "basic.log",
                    "basic-fail.log",
                };
                var analyzeRequest = new AnalyzeFilesRequest()
                {
                    DegreeOfParallelism = 4,
                };
                analyzeRequest.FileNames.AddRange(fileNames);
                var analyzeResult = session.AnalyzeFiles(analyzeRequest, CancellationToken.None).Result;
                Assert.IsTrue(analyzeResult.Status.Success);
                Assert.AreEqual(AgentErrorCode.NoAgentError, analyzeResult.Status.Code);

                {
                    var basicAnalyzeResult = session.GetAnalysisResult(new GetAnalysisResultRequest()
                    {
                        FileName = "basic.log",
                    }, CancellationToken.None);
                    Assert.HasCount(4, basicAnalyzeResult);

                    var firstResponse = basicAnalyzeResult[0];
                    Assert.IsTrue(firstResponse.Status.Success);
                    Assert.AreEqual(AgentErrorCode.NoAgentError, firstResponse.Status.Code);
                    Assert.AreEqual(GetAnalysisResultResponse.PayloadOneofCase.Header, firstResponse.PayloadCase);

                    var header = firstResponse.Header;
                    Assert.AreEqual(AnalysisStateEnum.Succeeded, header.State);

                    foreach (var result in basicAnalyzeResult.Skip(1))
                    {
                        Assert.IsTrue(result.Status.Success);
                        Assert.AreEqual(AgentErrorCode.NoAgentError, result.Status.Code);
                        Assert.AreEqual(GetAnalysisResultResponse.PayloadOneofCase.LogEntry, result.PayloadCase);
                    }

                    TestUtilsClass.VerifyCallLogEntryExample(
                        GrpcTypeConverter.ConvertFromGrpc(basicAnalyzeResult[1].LogEntry));
                    TestUtilsClass.VerifyRequestLogEntryExample(
                        GrpcTypeConverter.ConvertFromGrpc(basicAnalyzeResult[2].LogEntry));
                    TestUtilsClass.VerifyInternalLogEntryExample(
                        GrpcTypeConverter.ConvertFromGrpc(basicAnalyzeResult[3].LogEntry));
                }
                {
                    var basicFailAnalyzeResult = session.GetAnalysisResult(new GetAnalysisResultRequest()
                    {
                        FileName = "basic-fail.log",
                    }, CancellationToken.None);
                    Assert.HasCount(1, basicFailAnalyzeResult);

                    var firstResponse = basicFailAnalyzeResult[0];
                    Assert.IsTrue(firstResponse.Status.Success);
                    Assert.AreEqual(AgentErrorCode.NoAgentError, firstResponse.Status.Code);
                    Assert.AreEqual(GetAnalysisResultResponse.PayloadOneofCase.Header, firstResponse.PayloadCase);

                    var header = firstResponse.Header;
                    Assert.AreEqual(AnalysisStateEnum.Failed, header.State);
                }
                {
                    var basicMultipleAnalyzeResult = session.GetAnalysisResult(new GetAnalysisResultRequest()
                    {
                        FileName = "basic-multiple.log",
                    }, CancellationToken.None);
                    Assert.HasCount(1, basicMultipleAnalyzeResult);

                    var firstResponse = basicMultipleAnalyzeResult[0];
                    Assert.IsTrue(firstResponse.Status.Success);
                    Assert.AreEqual(AgentErrorCode.NoAgentError, firstResponse.Status.Code);
                    Assert.AreEqual(GetAnalysisResultResponse.PayloadOneofCase.Header, firstResponse.PayloadCase);

                    var header = firstResponse.Header;
                    Assert.AreEqual(AnalysisStateEnum.NotAnalyzed, header.State);
                }
                {
                    var hahahahaAnalyzeResult = session.GetAnalysisResult(new GetAnalysisResultRequest()
                    {
                        FileName = "hahahaha.log",
                    }, CancellationToken.None);
                    Assert.HasCount(1, hahahahaAnalyzeResult);

                    var firstResponse = hahahahaAnalyzeResult[0];
                    Assert.IsFalse(firstResponse.Status.Success);
                    Assert.AreEqual(AgentErrorCode.FileNotFound, firstResponse.Status.Code);
                }

                var analyzeAllRequest = new AnalyzeAllRequest()
                {
                    DegreeOfParallelism = 4,
                };
                var analyzeAllResult = session.AnalyzeAll(analyzeAllRequest, CancellationToken.None).Result;
                Assert.IsTrue(analyzeResult.Status.Success);
                Assert.AreEqual(AgentErrorCode.NoAgentError, analyzeResult.Status.Code);

                {
                    var basicMultipleAnalyzeResult = session.GetAnalysisResult(new GetAnalysisResultRequest()
                    {
                        FileName = "basic-multiple.log",
                    }, CancellationToken.None);
                    Assert.HasCount(BasicMultiple.LogData.Count + 1, basicMultipleAnalyzeResult);

                    var firstResponse = basicMultipleAnalyzeResult[0];
                    Assert.IsTrue(firstResponse.Status.Success);
                    Assert.AreEqual(AgentErrorCode.NoAgentError, firstResponse.Status.Code);
                    Assert.AreEqual(GetAnalysisResultResponse.PayloadOneofCase.Header, firstResponse.PayloadCase);

                    var header = firstResponse.Header;
                    Assert.AreEqual(AnalysisStateEnum.Succeeded, header.State);

                    foreach (var result in basicMultipleAnalyzeResult.Skip(1))
                    {
                        Assert.IsTrue(result.Status.Success);
                        Assert.AreEqual(AgentErrorCode.NoAgentError, result.Status.Code);
                        Assert.AreEqual(GetAnalysisResultResponse.PayloadOneofCase.LogEntry, result.PayloadCase);
                    }

                    var entries = basicMultipleAnalyzeResult.Skip(1).Select(
                                    x => GrpcTypeConverter.ConvertFromGrpc(x.LogEntry)).ToList();
                    TestUtilsClass.VerifyLogFile(BasicMultiple.LogData, entries);
                }
            }
        }
    }
}
