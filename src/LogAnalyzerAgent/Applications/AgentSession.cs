using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;
using LogParser.Visitors;
using Microsoft.Extensions.Logging;

namespace LogAnalyzerAgent.Applications
{
    public class AgentSession
    {
        private readonly LogFileAnalyzer _analyzer;
        private readonly ILogger _logger;

        public AgentSession(LogFileAnalyzer analyzer, ILoggerFactory loggerFactory)
        {
            _analyzer = analyzer;
            _logger = loggerFactory.CreateLogger<AgentSession>();
        }

        private static OperationStatusMessage CreateInternalErrorOperationStatus(Exception ex)
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = AgentErrorCode.InternalError,
                Message = $"An error occurred: {ex.Message}",
            };
        }

        private static OperationStatusMessage CreateNoErrorOperationStatus()
        {
            return new OperationStatusMessage()
            {
                Success = true,
                Code = AgentErrorCode.NoAgentError,
                Message = "",
            };
        }

        public Task<Empty> Ping(Empty empty, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Empty());
        }

        public Task<GetAgentStatusResponse> GetAgentStatus(Empty empty, CancellationToken cancellationToken)
        {
            var response = new GetAgentStatusResponse();
            try
            {
                response.HasDirectory = _analyzer.HasDirectory;
                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.IsAnalyzing = _analyzer.IsAnalyzing;
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while retrieving agent status.");
            }
            return Task.FromResult(response);
        }

        public Task<GetLogFilesResponse> GetLogFiles(Empty empty, CancellationToken cancellationToken)
        {
            var response = new GetLogFilesResponse();
            try
            {
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while retrieving log files.");
            }
            return Task.FromResult(response);
        }

        public Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, CancellationToken cancellationToken)
        {
            var response = new ChangeDirectoryResponse();
            try
            {
                _analyzer.ChangeDirectory(request.DirectoryPath);

                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (DirectoryNotFoundException ex)
            {
                response.Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.DirectoryNotFound, Message = ex.Message };
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Failed to change directory.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeAllResponse();
            try
            {
                if (request.DegreeOfParallelism <= 0)
                {
                    response.Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.InvalidArgument, Message = "Degree of parallelism must be greater than 0." };
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (InvalidOperationException ex)
            {
                response.Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.InvalidOperation, Message = ex.Message };
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Failed to analyze all files.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeFilesResponse();
            try
            {
                if (request.DegreeOfParallelism <= 0)
                {
                    response.Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.InvalidArgument, Message = "Degree of parallelism must be greater than 0." };
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames.ToList());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (FileNotFoundException ex)
            {
                response.Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.FileNotFound, Message = ex.Message };
            }
            catch (InvalidOperationException ex)
            {
                response.Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.InvalidOperation, Message = ex.Message };
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Failed to analyze files.");
            }
            return Task.FromResult(response);
        }

        // 恢复为返回 IReadOnlyList，匹配测试要求
        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            var responses = new List<GetAnalysisResultResponse>();
            var fileName = request.FileName;

            if (!_analyzer.HasDirectory)
            {
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.InvalidOperation, Message = "Directory not set." }
                });
                return responses;
            }

            var fileNames = _analyzer.GetLogFiles();

            if (!fileNames.Contains(fileName))
            {
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.FileNotFound, Message = $"File {fileName} not found in the current directory." }
                });
                return responses;
            }

            try
            {
                if (!_analyzer.TryGetAnalysisResult(fileName, out var result) || result == null)
                {
                    responses.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage { Success = false, Code = AgentErrorCode.InternalError, Message = "Failed to read analysis result." }
                    });
                    return responses;
                }

                var header = new AnalysisResultHeaderMessage
                {
                    FileName = fileName,
                    FullName = result.FullName ?? "",
                    State = GrpcTypeConverter.ConvertToGrpc(result.State),
                    WorkerId = result.WorkerId,
                };

                if (result.ErrorMessage != null)
                {
                    header.ErrorMessage = result.ErrorMessage;
                }

                // 2. 无论是否分析成功，先存入 header
                responses.Add(new GetAnalysisResultResponse
                {
                    Header = header,
                    Status = CreateNoErrorOperationStatus()
                });

                // 3. 若分析成功，则存入一系列 LogEntry
                if (result.State == AnalysisState.Succeeded && result.Entries != null)
                {
                    foreach (var entry in result.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        responses.Add(new GetAnalysisResultResponse
                        {
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                            Status = CreateNoErrorOperationStatus()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get analysis result for {fileName}.");
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex)
                });
            }

            return responses;
        }
    }
}