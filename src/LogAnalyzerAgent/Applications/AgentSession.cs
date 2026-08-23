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
                if (string.IsNullOrWhiteSpace(request.DirectoryPath))
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "目录路径不能为空"
                    };
                    return Task.FromResult(response);
                }

                var success = _analyzer.ChangeDirectory(request.DirectoryPath);
                if (!success)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.DirectoryNotFound,
                        Message = $"目录不存在: {request.DirectoryPath}"
                    };
                    return Task.FromResult(response);
                }

                // 成功：返回当前目录和文件列表
                response.CurrentDirectory = _analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "ChangeDirectory 失败: {DirectoryPath}", request.DirectoryPath);
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
                        Message = "尚未设置日志目录"
                    };
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "正在分析中，请稍后"
                    };
                    return Task.FromResult(response);
                }

                _analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "AnalyzeAll 失败");
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
                        Message = "尚未设置日志目录"
                    };
                    return Task.FromResult(response);
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "正在分析中，请稍后"
                    };
                    return Task.FromResult(response);
                }

                var fileNames = request.FileNames.ToList();
                if (fileNames.Count == 0)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "未指定任何文件"
                    };
                    return Task.FromResult(response);
                }

                var availableFiles = _analyzer.GetLogFiles();
                var validFileNames = new List<string>();
                
                foreach (var fileName in fileNames)
                {
                    // 先精确匹配
                    if (availableFiles.Contains(fileName))
                    {
                        validFileNames.Add(fileName);
                    }
                    else
                    {
                        // 尝试不区分大小写匹配
                        var matched = availableFiles.FirstOrDefault(f => 
                            string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));
                        if (matched != null)
                        {
                            validFileNames.Add(matched); // 使用实际存在的文件名
                        }
                        else
                        {
                            response.Status = new OperationStatusMessage
                            {
                                Success = false,
                                Code = AgentErrorCode.FileNotFound,
                                Message = $"文件不存在: {fileName}"
                            };
                            return Task.FromResult(response);
                        }
                    }
                }

                _analyzer.AnalyzeFiles(request.DegreeOfParallelism, validFileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "AnalyzeFiles 失败");
            }
            return Task.FromResult(response);
        }
        
        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, CancellationToken cancellationToken)
        {
                    var results = new List<GetAnalysisResultResponse>();
            try
            {
                if (string.IsNullOrWhiteSpace(request.FileName))
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.InvalidArgument,
                            Message = "文件名不能为空"
                        }
                    });
                    return results;
                }

                if (!_analyzer.HasDirectory)
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.InvalidOperation,
                            Message = "尚未设置日志目录"
                        }
                    });
                    return results;
                }

                // 获取实际存在的文件列表，匹配文件名
                var files = _analyzer.GetLogFiles();
                string? actualFileName = null;

                if (files.Contains(request.FileName))
                {
                    actualFileName = request.FileName;
                }
                else
                {
                    actualFileName = files.FirstOrDefault(f => 
                        string.Equals(f, request.FileName, StringComparison.OrdinalIgnoreCase));
                }

                if (actualFileName == null)
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.FileNotFound,
                            Message = $"文件不存在: {request.FileName}"
                        }
                    });
                    return results;
                }

                if (!_analyzer.TryGetAnalysisResult(actualFileName, out var result))
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.InternalError,
                            Message = $"获取分析结果失败: {request.FileName}"
                        }
                    });
                    return results;
                }

                if (result == null)
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Status = new OperationStatusMessage
                        {
                            Success = false,
                            Code = AgentErrorCode.InternalError,
                            Message = $"分析结果为空: {request.FileName}"
                        }
                    });
                    return results;
                }

                var header = new AnalysisResultHeaderMessage
                {
                    FileName = result.FileName,
                    State = GrpcTypeConverter.ConvertToGrpc(result.State),
                    WorkerId = result.WorkerId
                };

                if (result.State == AnalysisState.NotAnalyzed)
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Header = header,
                        Status = CreateNoErrorOperationStatus()  
                    });
                }
                else if (result.State == AnalysisState.Failed)
                {
                    
                    results.Add(new GetAnalysisResultResponse
                    {
                        Header = header,
                        Status = CreateNoErrorOperationStatus()  
                        // 注意：错误信息已经在 Header 的 State 中体现为 Failed
                    });
                }
                else // Succeeded
                {
                    results.Add(new GetAnalysisResultResponse
                    {
                        Header = header,
                        Status = CreateNoErrorOperationStatus()  
                    });

                    foreach (var entry in result.Entries)
                    {
                        var entryMessage = GrpcTypeConverter.ConvertToGrpc(entry);
                        results.Add(new GetAnalysisResultResponse
                        {
                            LogEntry = entryMessage,
                            Status = CreateNoErrorOperationStatus()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                results.Clear();
                results.Add(new GetAnalysisResultResponse
                {
                    Status = CreateInternalErrorOperationStatus(ex)
                });
                _logger.LogError(ex, "GetAnalysisResult 失败: {FileName}", request.FileName);
            }
            return results;
        }
    }
}
