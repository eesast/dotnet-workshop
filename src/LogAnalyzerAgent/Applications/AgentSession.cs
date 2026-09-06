using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;
using LogParser.Visitors;
using LogParser.Models;

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
                var directoryPath = request.DirectoryPath;
                if (!_analyzer.ChangeDirectory(string.IsNullOrEmpty(directoryPath) ? null : directoryPath))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.DirectoryNotFound,
                        $"Directory not found: {directoryPath}");
                    return Task.FromResult(response);
                }

                response.Status = CreateNoErrorOperationStatus();
                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message);
                _logger.LogError(ex, "Invalid directory path from client.");
            }
            catch (Exception ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InternalError,
                    $"An error occurred while changing directory: {ex.Message}");
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
                        "No directory has been set.");
                    return Task.FromResult(response);
                }
                if (request.DegreeOfParallelism < 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be non-negative.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
                _logger.LogError(ex, "Invalid analyze-all operation from client.");
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message);
                _logger.LogError(ex, "Invalid analyze-all argument from client.");
            }
            catch (Exception ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InternalError,
                    $"An error occurred while analyzing all files: {ex.Message}");
                _logger.LogError(ex, "An error occurred while analyzing all files.");
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
                        "No directory has been set.");
                    return Task.FromResult(response);
                }
                if (request.DegreeOfParallelism < 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be non-negative.");
                    return Task.FromResult(response);
                }
                if (request.FileNames.Count == 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "No file names specified.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
                _logger.LogError(ex, "Invalid analyze-files operation from client.");
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound, ex.Message);
                _logger.LogError(ex, "File not found while analyzing files.");
            }
            catch (Exception ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InternalError,
                    $"An error occurred while analyzing files: {ex.Message}");
                _logger.LogError(ex, "An error occurred while analyzing files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var analysisResult) || analysisResult is null)
                {
                    return
                    [
                        new GetAnalysisResultResponse()
                        {
                            Status = CreateErrorOperationStatus(
                                AgentErrorCode.FileNotFound,
                                $"File not found: {request.FileName}"),
                        }
                    ];
                }

                var header = new AnalysisResultHeaderMessage()
                {
                    FileName = analysisResult.FileName,
                    FullName = analysisResult.FullName,
                    State = GrpcTypeConverter.ConvertToGrpc(analysisResult.State),
                    WorkerId = analysisResult.WorkerId,
                };
                if (analysisResult.ErrorMessage is not null)
                {
                    header.ErrorMessage = analysisResult.ErrorMessage;
                }

                var responses = new List<GetAnalysisResultResponse>
                {
                    new()
                    {
                        Header = header,
                        Status = CreateNoErrorOperationStatus(),
                    }
                };

                if (analysisResult.State == AnalysisState.Succeeded)
                {
                    foreach (var entry in analysisResult.Entries)
                    {
                        responses.Add(new GetAnalysisResultResponse()
                        {
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                            Status = CreateNoErrorOperationStatus(),
                        });
                    }
                }
                return responses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting analysis result.");
                return
                [
                    new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.InternalError,
                            $"An error occurred while getting analysis result: {ex.Message}"),
                    }
                ];
            }
        }

        public IReadOnlyList<GetAnalysisResultResponse> QueryAnalysisResult(QueryAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var allResponses = GetAnalysisResult(new GetAnalysisResultRequest()
                {
                    FileName = request.FileName,
                }, cancellationToken);

                var criteria = request.Criteria;
                if (criteria is null)
                {
                    return allResponses;
                }

                var filteredResponses = new List<GetAnalysisResultResponse>();
                foreach (var response in allResponses)
                {
                    if (response.PayloadCase != GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        filteredResponses.Add(response);
                        continue;
                    }

                    var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                    if (MatchesCriteria(entry, criteria))
                    {
                        filteredResponses.Add(response);
                    }
                }
                return filteredResponses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while querying analysis result.");
                return
                [
                    new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.InternalError,
                            $"An error occurred while querying analysis result: {ex.Message}"),
                    }
                ];
            }
        }

        private static bool MatchesCriteria(LogEntry entry, LogQueryCriteria criteria)
        {
            if (criteria.HasRequestId)
            {
                var requestId = entry switch
                {
                    CallLogEntry call => call.RequestId,
                    RequestLogEntry request => request.RequestId,
                    _ => null,
                };
                if (requestId != criteria.RequestId)
                {
                    return false;
                }
            }

            if (criteria.HasServiceName
                && !entry.PodName.StartsWith(criteria.ServiceName + "-", StringComparison.Ordinal))
            {
                return false;
            }

            if (criteria.HasSeverity && entry.Severity != GrpcTypeConverter.ConvertFromGrpc(criteria.Severity))
            {
                return false;
            }

            if (criteria.HasEventType && entry.EventType != GrpcTypeConverter.ConvertFromGrpc(criteria.EventType))
            {
                return false;
            }

            if (criteria.StartTime is not null && entry.Timestamp < criteria.StartTime.ToDateTimeOffset())
            {
                return false;
            }

            if (criteria.EndTime is not null && entry.Timestamp > criteria.EndTime.ToDateTimeOffset())
            {
                return false;
            }

            return true;
        }
    }
}
