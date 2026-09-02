using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;
using LogParser.Visitors;

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
                Message = $"An error occurred while retrieving agent status: {ex.Message}",
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

        private static OperationStatusMessage CreateErrorOperationStatus(AgentErrorCode code, string message)
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = code,
                Message = message,
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
                if (_analyzer.IsAnalyzing)
                {
                    response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation,
                        "Cannot change directory while analysis is in progress.");
                    return Task.FromResult(response);
                }

                if (!_analyzer.ChangeDirectory(request.DirectoryPath))
                {
                    response.Status = CreateErrorOperationStatus(AgentErrorCode.DirectoryNotFound,
                        $"Directory not found: {request.DirectoryPath}");
                    return Task.FromResult(response);
                }

                response.Status = CreateNoErrorOperationStatus();
                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while changing directory.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeAllResponse();
            try
            {
                if (!_analyzer.HasDirectory)
                {
                    response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation,
                        "No log directory has been set.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument,
                    "Degree of parallelism must be non-negative.");
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing all log files.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeFilesResponse();
            try
            {
                if (!_analyzer.HasDirectory)
                {
                    response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation,
                        "No log directory has been set.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument,
                    "Degree of parallelism must be non-negative.");
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing log files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse
                    {
                        Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound,
                            $"File not found: {request.FileName}"),
                    });
                    return responses;
                }

                var noError = CreateNoErrorOperationStatus();

                var header = new AnalysisResultHeaderMessage
                {
                    FileName = result.FileName,
                    FullName = result.FullName,
                    State = GrpcTypeConverter.ConvertToGrpc(result.State),
                    WorkerId = result.WorkerId,
                };
                if (result.ErrorMessage is not null)
                {
                    header.ErrorMessage = result.ErrorMessage;
                }

                responses.Add(new GetAnalysisResultResponse
                {
                    Header = header,
                    Status = noError,
                });

                // 仅当分析成功时，流式返回每一条日志
                if (result.State == AnalysisState.Succeeded)
                {
                    foreach (var entry in result.Entries)
                    {
                        responses.Add(new GetAnalysisResultResponse
                        {
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                            Status = noError,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Clear();
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex),
                });
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
            }
            return responses;
        }
    }
}
