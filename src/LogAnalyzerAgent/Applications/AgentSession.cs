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
        private static OperationStatusMessage CreateAnalyzingOperationStatus()
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = AgentErrorCode.InvalidOperation,
                Message = "Invalid request while analyzing"
            };
        }

        private static OperationStatusMessage CreateInvalidArgumentOperationStatus(ArgumentException ex)
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = AgentErrorCode.InvalidArgument,
                Message = $"Invalid argument: {ex.Message}"
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
                    response.Status = CreateAnalyzingOperationStatus();
                }
                else
                {
                    var success = _analyzer.ChangeDirectory(request.DirectoryPath);
                    if (success)
                    {
                        response.FileNames.AddRange(_analyzer.GetLogFiles());
                        response.CurrentDirectory = _analyzer.CurrentDirectory;
                        response.Status = CreateNoErrorOperationStatus();
                    }
                    else
                    {
                        response.Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.DirectoryNotFound,
                            Message = $"No such directory: {request.DirectoryPath}"
                        };
                    }
                }
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
                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = CreateInvalidArgumentOperationStatus(ex);
            }
            catch (InvalidOperationException)
            {
                response.Status = CreateAnalyzingOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing all.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeFilesResponse();
            try
            {
                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = CreateInvalidArgumentOperationStatus(ex);
            }
            catch (ArgumentException ex)
            {
                response.Status = new OperationStatusMessage()
                {
                    Success = false,
                    Code = AgentErrorCode.FileNotFound,
                    Message = ex.Message
                };
            }
            catch (InvalidOperationException)
            {
                response.Status = CreateAnalyzingOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                var success = _analyzer.TryGetAnalysisResult(request.FileName, out var result);
                if (!success || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = new OperationStatusMessage()
                        {
                            Success = false,
                            Code = AgentErrorCode.FileNotFound,
                            Message = $"File not found: {request.FileName}"
                        }
                    });
                    return responses;
                }
                else
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Header = new AnalysisResultHeaderMessage()
                        {
                            FileName = result.FileName,
                            FullName = result.FullName,
                            State = GrpcTypeConverter.ConvertToGrpc(result.State),
                            ErrorMessage = result.ErrorMessage ?? "",
                            WorkerId = result.WorkerId
                        },
                        Status = CreateNoErrorOperationStatus()
                    });
                    foreach (var entry in result.Entries)
                    {
                        responses.Add(new GetAnalysisResultResponse()
                        {
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                            Status = CreateNoErrorOperationStatus()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex)
                });
                _logger.LogError(ex, "An error occurred while getting analysis result.");
            }
            return responses;
        }
    }
}
