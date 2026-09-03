using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;
using LogParser.Models;
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
                if (string.IsNullOrEmpty(request.DirectoryPath))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Directory path cannot be empty.");
                    return Task.FromResult(response);
                }

                if (!_analyzer.ChangeDirectory(request.DirectoryPath))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.DirectoryNotFound,
                        $"Directory '{request.DirectoryPath}' does not exist.");
                    return Task.FromResult(response);
                }

                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
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
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "No directory has been set yet.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
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
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "No directory has been set yet.");
                    return Task.FromResult(response);
                }

                if (request.FileNames.Count == 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "No file names are specified.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound, ex.Message);
                _logger.LogError(ex, "An error occurred while analyzing specified log files.");
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
                _logger.LogError(ex, "An error occurred while analyzing specified log files.");
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing specified log files.");
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
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound,
                            $"File '{request.FileName}' is not found.")
                    });
                    return responses;
                }

                var header = new AnalysisResultHeaderMessage()
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

                responses.Add(new GetAnalysisResultResponse()
                {
                    Header = header,
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
                return new List<GetAnalysisResultResponse>()
                {
                    new GetAnalysisResultResponse()
                    {
                        Status = CreateInternalErrorOperationStatus(ex)
                    }
                };
            }
            return responses;
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
    }
}
