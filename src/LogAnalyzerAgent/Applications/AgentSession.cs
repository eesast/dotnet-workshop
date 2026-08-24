using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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
                if (string.IsNullOrWhiteSpace(request.DirectoryPath))
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "Directory path cannot be empty."
                    };
                    return Task.FromResult(response);
                }

                if (!_analyzer.ChangeDirectory(request.DirectoryPath))
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.DirectoryNotFound,
                        Message = $"Directory '{request.DirectoryPath}' does not exist."
                    };
                    return Task.FromResult(response);
                }

                response.CurrentDirectory = _analyzer.CurrentDirectory ?? request.DirectoryPath;
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (ArgumentException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false,
                    Code = AgentErrorCode.InvalidArgument,
                    Message = ex.Message
                };
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
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "Directory not set."
                    };
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "Agent is currently analyzing logs."
                    };
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
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
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "Directory not set."
                    };
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "Agent is currently analyzing logs."
                    };
                    return Task.FromResult(response);
                }

                if (request.FileNames == null || request.FileNames.Count == 0)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "No files specified for analysis."
                    };
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (FileNotFoundException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false,
                    Code = AgentErrorCode.FileNotFound,
                    Message = ex.Message
                };
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing specified files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
            var resultsList = new List<GetAnalysisResultResponse>();
            try
            {
                if (string.IsNullOrWhiteSpace(request.FileName))
                {
                    resultsList.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.InvalidArgument,
                            Message = "File name cannot be empty."
                        }
                    });
                    return resultsList;
                }

                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    resultsList.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.FileNotFound,
                            Message = $"File '{request.FileName}' was not found or has no analysis result."
                        }
                    });
                    return resultsList;
                }

                var headerMessage = new AnalysisResultHeaderMessage
                {
                    FileName = request.FileName,
                    FullName = result.FullName ?? "", 
                    State = GrpcTypeConverter.ConvertToGrpc(result.State),
                    ErrorMessage = result.ErrorMessage ?? "",
                    WorkerId = result.WorkerId 
                };

                resultsList.Add(new GetAnalysisResultResponse
                {
                    Header = headerMessage,
                    Status = CreateNoErrorOperationStatus()
                });

                if (result.State == AnalysisState.Succeeded && result.Entries != null)
                {
                    foreach (var entry in result.Entries)
                    {
                        resultsList.Add(new GetAnalysisResultResponse
                        {
                            LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                            Status = CreateNoErrorOperationStatus()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                resultsList.Clear();
                resultsList.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex)
                });
                _logger.LogError(ex, "An error occurred while getting analysis result.");
            }

            return resultsList;
        }
    }
}
