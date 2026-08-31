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

        private static OperationStatusMessage CreateErrorOperationStatus(
            AgentErrorCode code,
            string message)
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = code,
                Message = message,
            };
        }

        private static OperationStatusMessage CreateInternalErrorOperationStatus(
            Exception ex,
            string operation)
        {
            return CreateErrorOperationStatus(
                AgentErrorCode.InternalError,
                $"An error occurred while {operation}: {ex.Message}");
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
                response.Status = CreateInternalErrorOperationStatus(
                    ex,
                    "retrieving agent status");
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
                response.Status = CreateInternalErrorOperationStatus(
                    ex,
                    "retrieving log files");
                _logger.LogError(ex, "An error occurred while retrieving log files.");
            }
            return Task.FromResult(response);
        }

        public Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, CancellationToken cancellationToken)
        {
            var response = new ChangeDirectoryResponse();
            try
            {
                var success = _analyzer.ChangeDirectory(request.DirectoryPath);
                if (!success)
                {
                    response.Status = _analyzer.IsAnalyzing
                        ? CreateErrorOperationStatus(
                            AgentErrorCode.InvalidOperation,
                            "Cannot change directory while log analysis is in progress.")
                        : CreateErrorOperationStatus(
                            AgentErrorCode.DirectoryNotFound,
                            $"Directory not found: {request.DirectoryPath}");
                    return Task.FromResult(response);
                }
                response.CurrentDirectory = request.DirectoryPath;
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidArgument,
                    ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidOperation,
                    ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(
                    ex,
                    "changing directory");
                _logger.LogError(ex, "An error occurred while changing directory.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeAllResponse();
            try
            {
                if (request.DegreeOfParallelism < 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be non-negative.");
                    return Task.FromResult(response);
                }
                if (!_analyzer.HasDirectory)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "No log directory has been configured.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidArgument,
                    ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidOperation,
                    ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(
                    ex,
                    "analyzing all log files");
                _logger.LogError(ex, "An error occurred while analyzing all log files.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, CancellationToken cancellationToken)
        {
            var response = new AnalyzeFilesResponse();
            try
            {
                if (request.DegreeOfParallelism < 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be non-negative.");
                    return Task.FromResult(response);
                }
                if (!_analyzer.HasDirectory)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "No log directory has been configured.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidArgument,
                    ex.Message);
            }
            catch (ArgumentException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.FileNotFound,
                    ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidOperation,
                    ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(
                    ex,
                    "analyzing specified log files");
                _logger.LogError(ex, "An error occurred while analyzing specified log files.");
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
                            Message = $"File not found: {request.FileName}",
                        }
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
                        ErrorMessage = result.ErrorMessage ?? "",
                        WorkerId = result.WorkerId,
                    },
                    Status = CreateNoErrorOperationStatus()
                });

                foreach (var entry in result.Entries)
                {
                    var entryMessage = GrpcTypeConverter.ConvertToGrpc(entry);
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        LogEntry = entryMessage,
                        Status = CreateNoErrorOperationStatus()
                    });
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse()
                {
                    Header = new AnalysisResultHeaderMessage()
                    {
                        FileName = request.FileName,
                        FullName = "",
                        State = AnalysisStateEnum.NotAnalyzed,
                        ErrorMessage = $"An error occurred while retrieving analysis result: {ex.Message}",
                        WorkerId = -1,
                    },
                    Status = CreateInternalErrorOperationStatus(
                        ex,
                        "retrieving an analysis result")
                });
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
            }
            return responses;
        }

        public IReadOnlyList<QueryAnalysisResultResponse> QueryAnalysisResult(
            QueryAnalysisResultRequest request,
            CancellationToken cancellationToken)
        {
            var responses = new List<QueryAnalysisResultResponse>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    responses.Add(new QueryAnalysisResultResponse
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound,
                            $"File not found: {request.FileName}")
                    });
                    return responses;
                }

                var queryResult = result.State == AnalysisState.Succeeded
                    ? LogEntryQuery.Execute(result.Entries, request)
                    : new LogEntryQueryResult([], 0, request.PageNumber, request.PageSize, 0, 0, 0);

                var queryHeader = new QueryAnalysisResultHeaderMessage
                {
                    Analysis = new AnalysisResultHeaderMessage
                    {
                        FileName = result.FileName,
                        FullName = result.FullName,
                        State = GrpcTypeConverter.ConvertToGrpc(result.State),
                        ErrorMessage = result.ErrorMessage ?? "",
                        WorkerId = result.WorkerId,
                    },
                    TotalCount = queryResult.TotalCount,
                    PageNumber = queryResult.PageNumber,
                    PageSize = queryResult.PageSize,
                    InfoCount = queryResult.InfoCount,
                    WarningCount = queryResult.WarningCount,
                    ErrorCount = queryResult.ErrorCount,
                };
                queryHeader.ServiceNames.AddRange(result.Entries
                    .Select(entry => LogEntryQuery.GetServiceName(entry.PodName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(serviceName => serviceName, StringComparer.OrdinalIgnoreCase));

                responses.Add(new QueryAnalysisResultResponse
                {
                    Header = queryHeader,
                    Status = CreateNoErrorOperationStatus()
                });

                foreach (var entry in queryResult.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    responses.Add(new QueryAnalysisResultResponse
                    {
                        LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                        Status = CreateNoErrorOperationStatus()
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                responses.Add(new QueryAnalysisResultResponse
                {
                    Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message)
                });
            }
            catch (OverflowException ex)
            {
                responses.Add(new QueryAnalysisResultResponse
                {
                    Status = CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message)
                });
            }
            catch (Exception ex)
            {
                responses.Add(new QueryAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex, "querying an analysis result")
                });
                _logger.LogError(ex, "An error occurred while querying analysis result.");
            }

            return responses;
        }
    }
}
