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
                bool success = _analyzer.ChangeDirectory(request.DirectoryPath);
                response.Status = success
                    ? CreateNoErrorOperationStatus()
                    : new OperationStatusMessage()
                    {
                        Success = false,
                        Code = AgentErrorCode.DirectoryNotFound,
                        Message = $"Invalid directory path: {request.DirectoryPath}.",
                    };
                if (success)
                {
                    response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                    response.FileNames.AddRange(_analyzer.GetLogFiles());
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
                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex){
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
                bool is_found = _analyzer.TryGetAnalysisResult(request.FileName, out var result);
                if (!is_found || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = new OperationStatusMessage()
                        {
                            Success = false,
                            Code = AgentErrorCode.FileNotFound,
                            Message = $"Analysis result for file '{request.FileName}' not found.",
                        },
                    });
                    return responses;
                }
                responses.Add(new GetAnalysisResultResponse()
                {
                    Header = new AnalysisResultHeaderMessage()
                    {
                        FileName = result.FileName,
                        FullName = result.FullName,
                        State = GrpcTypeConverter.ConvertToGrpc(result.State),
                        WorkerId = result.WorkerId,
                        ErrorMessage = result.ErrorMessage ?? "",
                    },
                    Status = CreateNoErrorOperationStatus(),
                });
                if (result.State == AnalysisState.Succeeded)
                {
                    foreach (var logEntry in result.Entries)
                    {
                        responses.Add(new GetAnalysisResultResponse()
                        {
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(logEntry),
                            Status = CreateNoErrorOperationStatus(),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse()
                {
                    Status = CreateInternalErrorOperationStatus(ex),
                });
                _logger.LogError(ex, "An error occurred while retrieving analysis result for file '{FileName}'.", request.FileName);
            }
            return responses;
        }
    }
}
