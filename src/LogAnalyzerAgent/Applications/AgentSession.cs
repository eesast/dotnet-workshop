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

        private static OperationStatusMessage CreateErrorOperationStatus(AgentErrorCode code, string message)
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = code,
                Message = message,
            };
        }

        private static OperationStatusMessage CreateOperationStatusFromException(Exception ex)
        {
            return ex switch
            {
                ArgumentOutOfRangeException => CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, ex.Message),
                InvalidOperationException => CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message),
                ArgumentException => CreateErrorOperationStatus(AgentErrorCode.FileNotFound, ex.Message),
                _ => CreateErrorOperationStatus(AgentErrorCode.InternalError, ex.Message),
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
                    if (_analyzer.IsAnalyzing)
                    {
                        response.Status = CreateErrorOperationStatus(
                            AgentErrorCode.InvalidOperation, "Cannot change directory while the agent is analyzing.");
                    }
                    else
                    {
                        response.Status = CreateErrorOperationStatus(
                            AgentErrorCode.DirectoryNotFound, $"Directory not found: {request.DirectoryPath}");
                    }
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
                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateOperationStatusFromException(ex);
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
            catch (Exception ex)
            {
                response.Status = CreateOperationStatusFromException(ex);
                _logger.LogError(ex, "An error occurred while analyzing specified log files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result))
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound, $"File '{request.FileName}' is not in the current directory."),
                    });
                    return responses;
                }

                var header = new AnalysisResultHeaderMessage()
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

                responses.Add(new GetAnalysisResultResponse()
                {
                    Header = header,
                    Status = CreateNoErrorOperationStatus(),
                });

                if (result.State == AnalysisState.Succeeded)
                {
                    foreach (var entry in result.Entries)
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
                responses.Add(new GetAnalysisResultResponse()
                {
                    Status = CreateInternalErrorOperationStatus(ex),
                });
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
                return responses;
            }
        }

        public Task<QueryLogEntriesResponse> QueryLogEntries(QueryLogEntriesRequest request, CancellationToken cancellationToken)
        {
            var response = new QueryLogEntriesResponse();
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.FileNotFound, $"File '{request.FileName}' is not in the current directory.");
                    return Task.FromResult(response);
                }

                if (result!.State != AnalysisState.Succeeded)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation, $"File '{request.FileName}' has not been analyzed successfully yet.");
                    return Task.FromResult(response);
                }

                var entries = result.Entries.AsEnumerable();
                entries = ApplyCondition(entries, request.Condition);
                entries = ApplySort(entries, request.SortBy, request.SortAscending);

                foreach (var entry in entries)
                {
                    response.Entries.Add(GrpcTypeConverter.ConvertToGrpc(entry));
                }
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateOperationStatusFromException(ex);
                _logger.LogError(ex, "An error occurred while querying log entries.");
            }
            return Task.FromResult(response);
        }

        public Task<GetStatisticsResponse> GetStatistics(GetStatisticsRequest request, CancellationToken cancellationToken)
        {
            var response = new GetStatisticsResponse();
            try
            {
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.FileNotFound, $"File '{request.FileName}' is not in the current directory.");
                    return Task.FromResult(response);
                }

                if (result!.State != AnalysisState.Succeeded)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation, $"File '{request.FileName}' has not been analyzed successfully yet.");
                    return Task.FromResult(response);
                }

                foreach (var group in result.Entries.GroupBy(e => e.Severity))
                {
                    response.SeverityCounts.Add(new StatisticsEntry()
                    {
                        Key = group.Key.ToString(),
                        Count = group.Count(),
                    });
                }

                foreach (var group in result.Entries.GroupBy(e => e.EventType))
                {
                    response.EventTypeCounts.Add(new StatisticsEntry()
                    {
                        Key = group.Key.ToString(),
                        Count = group.Count(),
                    });
                }

                foreach (var group in result.Entries.GroupBy(e => GetServiceName(e.PodName)))
                {
                    response.ServiceCounts.Add(new StatisticsEntry()
                    {
                        Key = group.Key,
                        Count = group.Count(),
                    });
                }

                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateOperationStatusFromException(ex);
                _logger.LogError(ex, "An error occurred while retrieving statistics.");
            }
            return Task.FromResult(response);
        }

        private static IEnumerable<LogEntry> ApplyCondition(IEnumerable<LogEntry> entries, QueryCondition condition)
        {
            if (condition.HasEventType)
            {
                var eventType = GrpcTypeConverter.ConvertFromGrpc(condition.EventType);
                entries = entries.Where(e => e.EventType == eventType);
            }

            if (condition.HasSeverity)
            {
                var severity = GrpcTypeConverter.ConvertFromGrpc(condition.Severity);
                entries = entries.Where(e => e.Severity == severity);
            }

            if (condition.HasService && !string.IsNullOrEmpty(condition.Service))
            {
                entries = entries.Where(e => GetServiceName(e.PodName) == condition.Service);
            }

            if (condition.HasRequestId && !string.IsNullOrEmpty(condition.RequestId))
            {
                entries = entries.Where(e => TryGetRequestId(e) == condition.RequestId);
            }

            if (condition.HasStartTime && !string.IsNullOrEmpty(condition.StartTime))
            {
                var start = DateTimeOffset.Parse(condition.StartTime);
                entries = entries.Where(e => e.Timestamp >= start);
            }

            if (condition.HasEndTime && !string.IsNullOrEmpty(condition.EndTime))
            {
                var end = DateTimeOffset.Parse(condition.EndTime);
                entries = entries.Where(e => e.Timestamp <= end);
            }

            return entries;
        }

        private static IEnumerable<LogEntry> ApplySort(IEnumerable<LogEntry> entries, string sortBy, bool ascending)
        {
            var key = string.IsNullOrEmpty(sortBy) ? "" : sortBy.Trim().ToLowerInvariant();
            return key switch
            {
                "lineno" => ascending ? entries.OrderBy(e => e.LineNo) : entries.OrderByDescending(e => e.LineNo),
                "timestamp" => ascending ? entries.OrderBy(e => e.Timestamp) : entries.OrderByDescending(e => e.Timestamp),
                "podname" => ascending ? entries.OrderBy(e => e.PodName) : entries.OrderByDescending(e => e.PodName),
                "severity" => ascending ? entries.OrderBy(e => e.Severity) : entries.OrderByDescending(e => e.Severity),
                "eventtype" => ascending ? entries.OrderBy(e => e.EventType) : entries.OrderByDescending(e => e.EventType),
                _ => entries,
            };
        }

        private static string GetServiceName(string podName)
        {
            var index = podName.LastIndexOf('-');
            return index < 0 ? podName : podName.Substring(0, index);
        }

        private static string? TryGetRequestId(LogEntry entry)
        {
            return entry switch
            {
                CallLogEntry call => call.RequestId,
                RequestLogEntry request => request.RequestId,
                _ => null,
            };
        }
    }
}
