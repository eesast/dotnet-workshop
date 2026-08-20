using Google.Protobuf.WellKnownTypes;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;

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
            return new OperationStatusMessage
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
                cancellationToken.ThrowIfCancellationRequested();
                if (!_analyzer.ChangeDirectory(request.DirectoryPath))
                {
                    var code = _analyzer.IsAnalyzing
                        ? AgentErrorCode.InvalidOperation
                        : AgentErrorCode.DirectoryNotFound;
                    response.Status = CreateErrorOperationStatus(code,
                        code == AgentErrorCode.InvalidOperation
                            ? "Cannot change directory while analysis is in progress."
                            : $"Directory does not exist: {request.DirectoryPath}");
                    return Task.FromResult(response);
                }

                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
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
                cancellationToken.ThrowIfCancellationRequested();
                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
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
                cancellationToken.ThrowIfCancellationRequested();
                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message);
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing selected log files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result))
                {
                    responses.Add(new GetAnalysisResultResponse
                    {
                        Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound,
                            $"Log file does not exist: {request.FileName}")
                    });
                    return responses;
                }

                var header = new AnalysisResultHeaderMessage
                {
                    FileName = result!.FileName,
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
                    Status = CreateNoErrorOperationStatus(),
                    Header = header,
                });

                foreach (var entry in result.Entries)
                {
                    responses.Add(new GetAnalysisResultResponse
                    {
                        Status = CreateNoErrorOperationStatus(),
                        LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                responses.Clear();
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex)
                });
                _logger.LogError(ex, "An error occurred while retrieving an analysis result.");
            }
            return responses;
        }
    }
}
