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
                Message = $"An internal error occurred: {ex.Message}",
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

        private static OperationStatusMessage CreateErrorOperationStatus(
            AgentErrorCode code,
            string message)
        {
            return new OperationStatusMessage
            {
                Success = false,
                Code = code,
                Message = message,
            };
        }

        private bool TryGetSucceededAnalysisResult(
            string fileName,
            out AnalysisResult? result,
            out OperationStatusMessage status)
        {
            if (!_analyzer.TryGetAnalysisResult(fileName, out result) || result is null)
            {
                status = CreateErrorOperationStatus(
                    AgentErrorCode.FileNotFound,
                    $"File '{fileName}' was not found.");
                return false;
            }

            if (result.State != AnalysisState.Succeeded)
            {
                var stateMessage = result.State switch
                {
                    AnalysisState.NotAnalyzed => "has not been analyzed yet",
                    AnalysisState.Failed => $"failed to analyze: {result.ErrorMessage ?? "Unknown error."}",
                    _ => $"has unsupported analysis state '{result.State}'",
                };
                status = CreateErrorOperationStatus(
                    AgentErrorCode.InvalidOperation,
                    $"File '{fileName}' {stateMessage}.");
                return false;
            }

            status = CreateNoErrorOperationStatus();
            return true;
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
                _analyzer.ChangeDirectory(request.DirectoryPath);
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
                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) ||
                    result is null)
                {
                    responses.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.FileNotFound,
                            Message = $"File '{request.FileName}' was not found."
                        }
                    });
                }
                else
                {
                    var header = new AnalysisResultHeaderMessage
                    {
                        FileName = result.FileName,
                        FullName = result.FullName,
                        State = GrpcTypeConverter.ConvertToGrpc(result.State),
                        WorkerId = result.WorkerId
                    };

                    if (result.ErrorMessage is not null)
                    {
                        header.ErrorMessage = result.ErrorMessage;
                    }

                    responses.Add(new GetAnalysisResultResponse
                    {
                        Status = CreateNoErrorOperationStatus(),
                        Header = header
                    });

                    if (result.State == AnalysisState.Succeeded)
                    {
                        foreach (var entry in result.Entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            responses.Add(new GetAnalysisResultResponse
                            {
                                Status = CreateNoErrorOperationStatus(),
                                LogEntry = GrpcTypeConverter.ConvertToGrpc(entry)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex)
                });
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
            }
            return responses;
        }

        public Task<GetServiceTopologyResponse> GetServiceTopology(
            GetServiceTopologyRequest request,
            CancellationToken cancellationToken)
        {
            var response = new GetServiceTopologyResponse();
            try
            {
                if (!TryGetSucceededAnalysisResult(request.FileName, out var result, out var status))
                {
                    response.Status = status;
                    return Task.FromResult(response);
                }

                var topology = ServiceTopologyBuilder.Build(result!.Entries);
                foreach (var node in topology.Nodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    response.Nodes.Add(new ServiceNodeMessage
                    {
                        Name = node.Name,
                    });
                }

                foreach (var edge in topology.Edges)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    response.Edges.Add(new ServiceEdgeMessage
                    {
                        SourceService = edge.SourceService,
                        TargetService = edge.TargetService,
                        CallCount = edge.CallCount,
                    });
                }

                response.Status = CreateNoErrorOperationStatus();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while retrieving service topology.");
            }

            return Task.FromResult(response);
        }

        public Task<GetTopologyEdgeLogsResponse> GetTopologyEdgeLogs(
            GetTopologyEdgeLogsRequest request,
            CancellationToken cancellationToken)
        {
            var response = new GetTopologyEdgeLogsResponse();
            try
            {
                if (string.IsNullOrWhiteSpace(request.SourceService) ||
                    string.IsNullOrWhiteSpace(request.TargetService))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Source service and target service must not be empty.");
                    return Task.FromResult(response);
                }

                if (!TryGetSucceededAnalysisResult(request.FileName, out var result, out var status))
                {
                    response.Status = status;
                    return Task.FromResult(response);
                }

                var topology = ServiceTopologyBuilder.Build(result!.Entries);
                var edge = topology.Edges.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourceService, request.SourceService, StringComparison.Ordinal) &&
                    string.Equals(candidate.TargetService, request.TargetService, StringComparison.Ordinal));

                if (edge is null)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        $"Edge '{request.SourceService}' -> '{request.TargetService}' does not exist in file '{request.FileName}'.");
                    return Task.FromResult(response);
                }

                foreach (var entry in edge.Calls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    response.Entries.Add(GrpcTypeConverter.ConvertToGrpc(entry).CallLogEntry);
                }
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while retrieving topology edge logs.");
            }

            return Task.FromResult(response);
        }
    }
}
