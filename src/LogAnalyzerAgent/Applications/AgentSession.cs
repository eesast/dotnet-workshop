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
            static OperationStatusMessage Error(AgentErrorCode code, string message) => new()
            {
                Success = false,
                Code = code,
                Message = message,
            };

            var response = new ChangeDirectoryResponse();
            try
            {
                if (request is null || string.IsNullOrWhiteSpace(request.DirectoryPath))
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidArgument,
                        "Directory path cannot be empty.");
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidOperation,
                        "Cannot change directory while analysis is in progress.");
                    return Task.FromResult(response);
                }

                var directoryPath = request.DirectoryPath.Trim();
                if (!Directory.Exists(directoryPath))
                {
                    response.Status = Error(
                        AgentErrorCode.DirectoryNotFound,
                        $"Directory '{directoryPath}' does not exist.");
                    return Task.FromResult(response);
                }

                if (!_analyzer.ChangeDirectory(directoryPath))
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidOperation,
                        "Cannot change directory while analysis is in progress.");
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
            static OperationStatusMessage Error(AgentErrorCode code, string message) => new()
            {
                Success = false,
                Code = code,
                Message = message,
            };

            var response = new AnalyzeAllResponse();
            try
            {
                if (request is null || request.DegreeOfParallelism < 0)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be a non-negative integer.");
                    return Task.FromResult(response);
                }

                if (!_analyzer.HasDirectory)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidOperation,
                        "A log directory must be selected before analysis.");
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidOperation,
                        "Analysis is already in progress.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = Error(AgentErrorCode.InvalidArgument, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = Error(AgentErrorCode.InvalidOperation, ex.Message);
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
            static OperationStatusMessage Error(AgentErrorCode code, string message) => new()
            {
                Success = false,
                Code = code,
                Message = message,
            };

            var response = new AnalyzeFilesResponse();
            try
            {
                if (request is null || request.DegreeOfParallelism < 0)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be a non-negative integer.");
                    return Task.FromResult(response);
                }

                if (!_analyzer.HasDirectory)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidOperation,
                        "A log directory must be selected before analysis.");
                    return Task.FromResult(response);
                }

                var fileNames = request.FileNames
                    .Select(fileName => fileName?.Trim() ?? "")
                    .Where(fileName => fileName.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (fileNames.Count == 0 || fileNames.Count != request.FileNames.Count)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidArgument,
                        "At least one non-empty log file name is required.");
                    return Task.FromResult(response);
                }

                var availableFiles = _analyzer.GetLogFiles().ToHashSet(StringComparer.Ordinal);
                var missingFile = fileNames.FirstOrDefault(fileName => !availableFiles.Contains(fileName));
                if (missingFile is not null)
                {
                    response.Status = Error(
                        AgentErrorCode.FileNotFound,
                        $"File '{missingFile}' does not exist in the current directory.");
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = Error(
                        AgentErrorCode.InvalidOperation,
                        "Analysis is already in progress.");
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, fileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                response.Status = Error(AgentErrorCode.InvalidArgument, ex.Message);
            }
            catch (ArgumentException ex)
            {
                response.Status = Error(AgentErrorCode.FileNotFound, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                response.Status = Error(AgentErrorCode.InvalidOperation, ex.Message);
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
            static OperationStatusMessage Error(AgentErrorCode code, string message) => new()
            {
                Success = false,
                Code = code,
                Message = message,
            };

            try
            {
                if (request is null || string.IsNullOrWhiteSpace(request.FileName))
                {
                    return new[]
                    {
                        new GetAnalysisResultResponse
                        {
                            Status = Error(
                                AgentErrorCode.InvalidArgument,
                                "Log file name cannot be empty.")
                        }
                    };
                }

                if (!_analyzer.HasDirectory)
                {
                    return new[]
                    {
                        new GetAnalysisResultResponse
                        {
                            Status = Error(
                                AgentErrorCode.InvalidOperation,
                                "A log directory must be selected before retrieving analysis results.")
                        }
                    };
                }

                var fileName = request.FileName.Trim();
                if (!_analyzer.TryGetAnalysisResult(fileName, out var result) || result is null)
                {
                    return new[]
                    {
                        new GetAnalysisResultResponse
                        {
                            Status = Error(
                                AgentErrorCode.FileNotFound,
                                $"File '{fileName}' does not exist in the current directory.")
                        }
                    };
                }

                var header = new AnalysisResultHeaderMessage
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

                var responses = new List<GetAnalysisResultResponse>
                {
                    new()
                    {
                        Status = CreateNoErrorOperationStatus(),
                        Header = header,
                    }
                };
                if (result.State == AnalysisState.Succeeded)
                {
                    responses.AddRange(result.Entries.Select(entry => new GetAnalysisResultResponse
                    {
                        Status = CreateNoErrorOperationStatus(),
                        LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                    }));
                }

                return responses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
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
