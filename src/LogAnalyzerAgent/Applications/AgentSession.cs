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
                if (!_analyzer.ChangeDirectory(request.DirectoryPath))
                {
                    response.Status = _analyzer.IsAnalyzing
                        ? new OperationStatusMessage
                        {
                            Success = false, Code = AgentErrorCode.InvalidOperation,
                            Message = "Analysis is in progress."
                        }
                        : new OperationStatusMessage
                        {
                            Success = false, Code = AgentErrorCode.DirectoryNotFound,
                            Message = $"Directory '{request.DirectoryPath}' does not exist."
                        };
                    return Task.FromResult(response);    
                }
                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Unable to change directory to {Directory}.",request.DirectoryPath);
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
                    response.Status = new OperationStatusMessage
                    {
                        Success = false, Code = AgentErrorCode.InvalidOperation,
                        Message = "Please select a log directory first."
                    };
                }
                else
                {
                    _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                    response.Status = CreateNoErrorOperationStatus();
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false, Code = AgentErrorCode.InvalidArgument, Message=ex.Message
                };
            }
            catch (InvalidOperationException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false, Code = AgentErrorCode.InvalidOperation, Message=ex.Message
                };
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Unable to analyze all files.");
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
                    response.Status = new OperationStatusMessage
                    {
                        Success = false, Code = AgentErrorCode.InvalidOperation,
                        Message = "Please select a log directory first."
                    };
                }
                else
                {
                    _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                    response.Status = CreateNoErrorOperationStatus();
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false, Code = AgentErrorCode.InvalidArgument, Message=ex.Message
                };
            }
            catch (InvalidOperationException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false, Code = AgentErrorCode.InvalidOperation, Message=ex.Message
                };
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Unable to analyze specified files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    return new[]
                    {
                        new GetAnalysisResultResponse
                        {
                            Status = new OperationStatusMessage
                            {
                                Success = false, Code = AgentErrorCode.FileNotFound,
                                Message = $"File '{request.FileName}' does not exist."
                            }
                        }
                    };
                }
                var responses = new List<GetAnalysisResultResponse>
                {
                    new()
                    {
                        Status = CreateNoErrorOperationStatus(),
                        Header = new AnalysisResultHeaderMessage
                        {
                            FileName = result.FileName,
                            FullName = result.FullName,
                            State = GrpcTypeConverter.ConvertToGrpc(result.State),
                            WorkerId = result.WorkerId
                        }
                    }
                };
                if (result.ErrorMessage is not null)
                {
                    responses[0].Header.ErrorMessage = result.ErrorMessage;
                }
                if (result.State == AnalysisState.Succeeded)
                {
                    responses.AddRange(result.Entries.Select(entry =>
                        new GetAnalysisResultResponse
                        {
                            Status = CreateNoErrorOperationStatus(),
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(entry)
                        }));
                }
                return responses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to retrieve analysis result for {FileName}",request.FileName);
                return new[]
                {
                    new GetAnalysisResultResponse
                    {
                        Status = CreateInternalErrorOperationStatus(ex)
                    }
                };
            }
        }
    }
}
