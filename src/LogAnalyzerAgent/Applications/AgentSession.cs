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

        public QueryLogEntriesResponse QueryLogEntries(QueryLogEntriesRequest request, CancellationToken cancellationToken)
        {
            var response = new QueryLogEntriesResponse();
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.FileNotFound,
                        $"File '{request.FileName}' is not found.");
                    return response;
                }

                if (result.State != AnalysisState.Succeeded)
                {
                    var reason = result.State == AnalysisState.Failed && result.ErrorMessage is not null
                        ? $" (reason: {result.ErrorMessage})"
                        : string.Empty;
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        $"File '{request.FileName}' has not been successfully analyzed. State = {result.State}.{reason}");
                    return response;
                }

                var filter = new LogEntryQueryFilter
                {
                    EventType = request.HasEventType ? GrpcTypeConverter.ConvertFromGrpc(request.EventType) : null,
                    Severity = request.HasSeverity ? GrpcTypeConverter.ConvertFromGrpc(request.Severity) : null,
                    ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim(),
                    RequestId = string.IsNullOrWhiteSpace(request.RequestId) ? null : request.RequestId.Trim(),
                    StartTime = request.StartTime?.ToDateTimeOffset(),
                    EndTime = request.EndTime?.ToDateTimeOffset(),
                };

                var matched = new List<LogEntry>();
                foreach (var entry in result.Entries)
                {
                    if (filter.IsMatch(entry))
                    {
                        matched.Add(entry);
                    }
                }

                response.TotalCount = result.Entries.Count;
                response.MatchedCount = matched.Count;
                response.Status = CreateNoErrorOperationStatus();
                foreach (var entry in matched)
                {
                    response.LogEntries.Add(GrpcTypeConverter.ConvertToGrpc(entry));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while querying log entries.");
                response.Status = CreateInternalErrorOperationStatus(ex);
            }
            return response;
        }

        /// <summary>
        /// Filters a parsed <see cref="LogEntry"/> against a set of query conditions.
        /// A null condition means "no constraint"; all set conditions must match.
        /// </summary>
        private sealed class LogEntryQueryFilter
        {
            public LogEventType? EventType { get; init; }

            public LogSeverity? Severity { get; init; }

            /// <summary>Prefix of the producing service/pod name.</summary>
            public string? ServiceName { get; init; }

            public string? RequestId { get; init; }

            public DateTimeOffset? StartTime { get; init; }

            public DateTimeOffset? EndTime { get; init; }

            public bool IsMatch(LogEntry entry)
            {
                if (EventType is { } eventType && entry.EventType != eventType)
                {
                    return false;
                }

                if (Severity is { } severity && entry.Severity != severity)
                {
                    return false;
                }

                if (ServiceName is not null
                    && !entry.PodName.StartsWith(ServiceName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (RequestId is not null)
                {
                    var entryRequestId = entry switch
                    {
                        CallLogEntry call => call.RequestId,
                        RequestLogEntry request => request.RequestId,
                        _ => null,
                    };
                    if (!string.Equals(entryRequestId, RequestId, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (StartTime is not null && entry.Timestamp < StartTime.Value)
                {
                    return false;
                }

                if (EndTime is not null && entry.Timestamp > EndTime.Value)
                {
                    return false;
                }

                return true;
            }
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
