using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Visitors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LogAnalyzerAgent.Applications
{
    public class AgentSession
    {
        private readonly LogFileAnalyzer _analyzer;
        private readonly ILogger<AgentSession> _logger;

        public AgentSession(LogFileAnalyzer analyzer, ILoggerFactory loggerFactory)
        {
            _analyzer = analyzer;
            _logger = loggerFactory.CreateLogger<AgentSession>();
        }

        private static OperationStatusMessage CreateInternalErrorOperationStatus(Exception ex)
        {
            return new OperationStatusMessage
            {
                Success = false,
                Code = AgentErrorCode.InternalError,
                Message = $"An internal error occurred: {ex.Message}",
            };
        }

        private static OperationStatusMessage CreateNoErrorOperationStatus()
        {
            return new OperationStatusMessage
            {
                Success = true,
                Code = AgentErrorCode.NoAgentError,
                Message = string.Empty,
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
                response.CurrentDirectory = _analyzer.CurrentDirectory ?? string.Empty;
                response.IsAnalyzing = _analyzer.IsAnalyzing;
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Error occurred while retrieving agent status.");
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
                _logger.LogError(ex, "Error occurred while retrieving log files.");
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
                        Message = $"Directory '{request.DirectoryPath}' does not exist or is currently analyzing."
                    };
                    return Task.FromResult(response);
                }

                response.CurrentDirectory = _analyzer.CurrentDirectory ?? request.DirectoryPath;
                response.FileNames.AddRange(_analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Error occurred while changing directory.");
            }
            return Task.FromResult(response);
        }

        public async Task<AnalyzeAllResponse> AnalyzeAllAsync(AnalyzeAllRequest request, CancellationToken cancellationToken)
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
                    return response;
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "Agent is currently analyzing logs."
                    };
                    return response;
                }

                // 在后台线程运行密集型 CPU 任务，避免阻塞 gRPC 主处理线程
                await Task.Run(() => _analyzer.AnalyzeAll(request.DegreeOfParallelism), cancellationToken);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Error occurred while analyzing all files.");
            }
            return response;
        }

        public async Task<AnalyzeFilesResponse> AnalyzeFilesAsync(AnalyzeFilesRequest request, CancellationToken cancellationToken)
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
                    return response;
                }

                if (_analyzer.IsAnalyzing)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = "Agent is currently analyzing logs."
                    };
                    return response;
                }

                if (request.FileNames == null || request.FileNames.Count == 0)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "No files specified for analysis."
                    };
                    return response;
                }

                // 异步后台线程执行
                await Task.Run(() => _analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames), cancellationToken);
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
                _logger.LogError(ex, "Error occurred while analyzing specified files.");
            }
            return response;
        }

        // 优化为延迟生成器 (Yield)，提高大日志文件下传输响应速度
        public async IAsyncEnumerable<GetAnalysisResultResponse> GetAnalysisResultStreamAsync(
            GetAnalysisResultRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                yield return new GetAnalysisResultResponse
                {
                    Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "File name cannot be empty."
                    }
                };
                yield break;
            }

            if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
            {
                yield return new GetAnalysisResultResponse
                {
                    Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.FileNotFound,
                        Message = $"File '{request.FileName}' was not found or has no analysis result."
                    }
                };
                yield break;
            }

            // 1. 先推送 Header
            var headerMessage = new AnalysisResultHeaderMessage
            {
                FileName = request.FileName,
                FullName = result.FullName ?? string.Empty,
                State = GrpcTypeConverter.ConvertToGrpc(result.State),
                ErrorMessage = result.ErrorMessage ?? string.Empty,
                WorkerId = result.WorkerId
            };

            yield return new GetAnalysisResultResponse
            {
                Header = headerMessage,
                Status = CreateNoErrorOperationStatus()
            };

            // 2. 逐条推送 Log Entries
            if (result.State.ToString() == "Succeeded" && result.Entries != null)
            {
                foreach (var entry in result.Entries)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;

                    yield return new GetAnalysisResultResponse
                    {
                        LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                        Status = CreateNoErrorOperationStatus()
                    };
                }
            }

            await Task.CompletedTask;
        }

        // 查询 + 排序（T5.1.a.c）：对已分析文件的日志按条件过滤、排序并流式返回。
        public async IAsyncEnumerable<GetAnalysisResultResponse> QueryAnalysisResultStreamAsync(
            QueryAnalysisResultRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                yield return ErrorResponse(AgentErrorCode.InvalidArgument, "File name cannot be empty.");
                yield break;
            }

            if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
            {
                yield return ErrorResponse(AgentErrorCode.FileNotFound,
                    $"File '{request.FileName}' was not found or has no analysis result.");
                yield break;
            }

            // 1. 先推送 Header（与 GetAnalysisResult 保持一致的协议形状）
            var headerMessage = new AnalysisResultHeaderMessage
            {
                FileName = request.FileName,
                FullName = result.FullName ?? string.Empty,
                State = GrpcTypeConverter.ConvertToGrpc(result.State),
                ErrorMessage = result.ErrorMessage ?? string.Empty,
                WorkerId = result.WorkerId
            };

            yield return new GetAnalysisResultResponse
            {
                Header = headerMessage,
                Status = CreateNoErrorOperationStatus()
            };

            if (result.State.ToString() != "Succeeded" || result.Entries is null)
            {
                yield break;
            }

            // 2. 过滤 + 排序后逐条推送
            var filtered = LogAnalysisQuery.FilterAndSort(result.Entries, request.Filter, request.Sort);
            foreach (var entry in filtered)
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                yield return new GetAnalysisResultResponse
                {
                    LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                    Status = CreateNoErrorOperationStatus()
                };
            }

            await Task.CompletedTask;
        }

        // 云服务拓扑推断（T5.1.a.d）：基于 Call 日志构建有向调用图。
        public Task<GetTopologyResponse> GetTopology(GetTopologyRequest request, CancellationToken cancellationToken)
        {
            var response = new GetTopologyResponse();
            try
            {
                if (string.IsNullOrWhiteSpace(request.FileName))
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidArgument,
                        Message = "File name cannot be empty."
                    };
                    return Task.FromResult(response);
                }

                if (!_analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.FileNotFound,
                        Message = $"File '{request.FileName}' was not found or has no analysis result."
                    };
                    return Task.FromResult(response);
                }

                if (result.State.ToString() != "Succeeded" || result.Entries is null)
                {
                    response.Status = new OperationStatusMessage
                    {
                        Success = false,
                        Code = AgentErrorCode.InvalidOperation,
                        Message = $"File '{request.FileName}' has not been analyzed successfully."
                    };
                    return Task.FromResult(response);
                }

                var topology = TopologyBuilder.Build(result.Entries);
                foreach (var node in topology.Nodes)
                {
                    response.Nodes.Add(new TopologyNode { ServiceName = node });
                }
                foreach (var edge in topology.Edges)
                {
                    var edgeMessage = new TopologyEdge
                    {
                        SourceService = edge.SourceService,
                        TargetService = edge.TargetService,
                        CallCount = edge.CallCount
                    };
                    edgeMessage.RequestIds.AddRange(edge.RequestIds);
                    response.Edges.Add(edgeMessage);
                }

                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex);
                _logger.LogError(ex, "Error occurred while building service topology.");
            }
            return Task.FromResult(response);
        }

        private static GetAnalysisResultResponse ErrorResponse(AgentErrorCode code, string message)
        {
            return new GetAnalysisResultResponse
            {
                Status = new OperationStatusMessage
                {
                    Success = false,
                    Code = code,
                    Message = message
                }
            };
        }
    }
}