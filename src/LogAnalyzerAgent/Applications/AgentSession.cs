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
            var response=new ChangeDirectoryResponse();
            try
            {
                bool exists = _analyzer.ChangeDirectory(request.DirectoryPath);
                if (!exists)
                {
                    response.Status=new OperationStatusMessage
                    {
                        Success=false,
                        Code=AgentErrorCode.DirectoryNotFound,
                        Message="Directory not exists."
                    };
                }
                else
                {
                    response.Status=CreateNoErrorOperationStatus();
                    response.CurrentDirectory=_analyzer.CurrentDirectory??"";
                    response.FileNames.AddRange(_analyzer.GetLogFiles());
                }
            }
            catch(ArgumentException ex)
            {
                response.Status = new OperationStatusMessage
                {
                    Success = false,
                    Code = AgentErrorCode.InvalidArgument,
                     Message = "Directory illegal: " + ex.Message
                };
            }
            catch(Exception ex)
            {
                response.Status=CreateInternalErrorOperationStatus(ex);
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
            catch(InvalidOperationException ex)
            {
                response.Status=new OperationStatusMessage
                {
                    Success=false,
                    Code=AgentErrorCode.InvalidOperation,
                    Message="Analysis is already running: " + ex.Message
                };
            }
            catch(ArgumentException ex)
            {
                response.Status=new OperationStatusMessage
                {
                    Success=false,
                    Code=AgentErrorCode.InvalidArgument,
                    Message = "Invalid degree of parallelism: " + ex.Message
                };
            }
            catch(Exception ex)
            {
                response.Status=CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "An error occurred while analyzing all files.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, CancellationToken cancellationToken)
        {
            var response=new AnalyzeFilesResponse();
            try
            {
                _analyzer.AnalyzeFiles(request.DegreeOfParallelism,request.FileNames.ToArray());
                response.Status=CreateNoErrorOperationStatus();
            }
            catch(InvalidOperationException ex)
            {
                response.Status=new OperationStatusMessage
                {
                    Success=false,
                    Code=AgentErrorCode.InvalidOperation,
                    Message="Analysis is already running: " + ex.Message
                };
            }
            catch (ArgumentException ex)
            {
            response.Status = new OperationStatusMessage
            {
                Success = false,
                Code = AgentErrorCode.InvalidArgument,
                Message = "Invalid files or degree of parallelism: " + ex.Message
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
            var responseList = new List<GetAnalysisResultResponse>();
            try
            {
                if(!_analyzer.TryGetAnalysisResult(request.FileName,out var result) || result == null)
                {
                    responseList.Add(new GetAnalysisResultResponse
                    {
                        Status =new OperationStatusMessage
                        {
                            Success=false,
                            Code=AgentErrorCode.FileNotFound,
                            Message="file not Found."
                        }
                    });
                    return responseList;
                }
                var headerResponse =new GetAnalysisResultResponse
                {
                    Status=CreateNoErrorOperationStatus(),
                    Header =new AnalysisResultHeaderMessage
                    {
                        FileName= request.FileName,
                        State=GrpcTypeConverter.ConvertToGrpc(result.State)
                    }
                };
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    headerResponse.Header.ErrorMessage=result.ErrorMessage;
                }
                responseList.Add(headerResponse);
                if (result.State == AnalysisState.Succeeded)
                {
                    foreach(var entry in result.Entries)
                    {
                        var LogEntryResponse=new GetAnalysisResultResponse
                        {
                            Status=CreateNoErrorOperationStatus(),
                            LogEntry =GrpcTypeConverter.ConvertToGrpc(entry),
                        };
                        responseList.Add(LogEntryResponse);
                    }
                }

            }
            catch (Exception ex)
            {
                var errorResponse= new GetAnalysisResultResponse();
                errorResponse.Status=CreateInternalErrorOperationStatus(ex);
                responseList.Add(errorResponse);
                _logger.LogError(ex, "An error occurred while getting analysis result.");
            }
            return responseList;
        }
    }
}
