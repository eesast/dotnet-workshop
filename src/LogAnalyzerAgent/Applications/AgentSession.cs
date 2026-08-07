using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerAgent.Auth;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;
using LogParser.Models;
using LogParser.Parquet;
using LogParser.Visitors;
using System.Text.RegularExpressions;

namespace LogAnalyzerAgent.Applications
{
    /// <summary>
    /// Agent 端的业务逻辑入口（T5.1.a.b 起，所有方法均以 <see cref="TokenInfo"/> 标识调用者）。
    ///
    /// 每个 token 代表一个独立用户，由 <see cref="SessionManager"/> 映射到其专属的
    /// <see cref="LogFileAnalyzer"/>，从而实现不同用户间目录与分析结果的完全隔离。
    /// </summary>
    public class AgentSession
    {
        private readonly SessionManager _sessions;
        private readonly TokenStore _tokens;
        private readonly ILogger _logger;

        public AgentSession(SessionManager sessions, TokenStore tokens, ILoggerFactory loggerFactory)
        {
            _sessions = sessions;
            _tokens = tokens;
            _logger = loggerFactory.CreateLogger<AgentSession>();
        }

        private static OperationStatusMessage CreateInternalErrorOperationStatus(Exception ex, string operation)
        {
            return new OperationStatusMessage()
            {
                Success = false,
                Code = AgentErrorCode.InternalError,
                Message = $"An error occurred while {operation}: {ex.Message}",
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

        public Task<Empty> Ping(Empty empty, TokenInfo caller, CancellationToken cancellationToken)
        {
            // Ping 仅用于校验连接与 token 是否合法；真正的校验已在 AgentService.Authorize 完成。
            _ = caller;
            return Task.FromResult(new Empty());
        }

        public Task<GetAgentStatusResponse> GetAgentStatus(Empty empty, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new GetAgentStatusResponse();
            try
            {
                response.HasDirectory = analyzer.HasDirectory;
                response.CurrentDirectory = analyzer.CurrentDirectory ?? "";
                response.IsAnalyzing = analyzer.IsAnalyzing;
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "retrieving agent status");
                _logger.LogError(ex, "An error occurred while retrieving agent status.");
            }
            return Task.FromResult(response);
        }

        public Task<GetLogFilesResponse> GetLogFiles(Empty empty, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new GetLogFilesResponse();
            try
            {
                response.FileNames.AddRange(analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "retrieving log files");
                _logger.LogError(ex, "An error occurred while retrieving log files.");
            }
            return Task.FromResult(response);
        }

        public Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new ChangeDirectoryResponse();
            try
            {
                if (analyzer.IsAnalyzing)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "Cannot change directory while analysis is in progress.");
                    return Task.FromResult(response);
                }

                bool changed;
                try
                {
                    changed = analyzer.ChangeDirectory(request.DirectoryPath);
                }
                catch (ArgumentException)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        $"Invalid directory path: '{request.DirectoryPath}'.");
                    return Task.FromResult(response);
                }

                if (!changed)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.DirectoryNotFound,
                        $"Directory '{request.DirectoryPath}' does not exist.");
                    return Task.FromResult(response);
                }

                response.CurrentDirectory = analyzer.CurrentDirectory ?? "";
                response.FileNames.AddRange(analyzer.GetLogFiles());
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "changing directory");
                _logger.LogError(ex, "An error occurred while changing directory.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new AnalyzeAllResponse();
            try
            {
                if (!analyzer.HasDirectory)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "No log directory has been set. Please change directory first.");
                    return Task.FromResult(response);
                }

                if (request.DegreeOfParallelism < 0)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "Degree of parallelism must be non-negative.");
                    return Task.FromResult(response);
                }

                analyzer.AnalyzeAll(request.DegreeOfParallelism);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "analyzing all log files");
                _logger.LogError(ex, "An error occurred while analyzing all log files.");
            }
            return Task.FromResult(response);
        }

        public Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new AnalyzeFilesResponse();
            try
            {
                if (!analyzer.HasDirectory)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        "No log directory has been set. Please change directory first.");
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
                        "No file names provided.");
                    return Task.FromResult(response);
                }

                analyzer.AnalyzeFiles(request.DegreeOfParallelism, request.FileNames);
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (InvalidOperationException ex)
            {
                response.Status = CreateErrorOperationStatus(AgentErrorCode.InvalidOperation, ex.Message);
            }
            catch (ArgumentException ex)
            {
                // LogFileAnalyzer throws ArgumentException when a requested file is not in the directory.
                response.Status = CreateErrorOperationStatus(AgentErrorCode.FileNotFound, ex.Message);
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "analyzing log files");
                _logger.LogError(ex, "An error occurred while analyzing log files.");
            }
            return Task.FromResult(response);
        }

        public IReadOnlyList<GetAnalysisResultResponse> GetAnalysisResult(GetAnalysisResultRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                if (!analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound,
                            $"File '{request.FileName}' does not exist in the current directory."),
                    });
                    return responses;
                }

                // First, return the header describing the analysis state of the file.
                responses.Add(BuildHeaderResponse(result));

                // Only stream log entries when the analysis succeeded.
                if (result.State == AnalysisState.Succeeded)
                {
                    foreach (var entry in result.Entries)
                    {
                        responses.Add(BuildEntryResponse(entry));
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse()
                {
                    Status = CreateInternalErrorOperationStatus(ex, "retrieving analysis result"),
                });
                _logger.LogError(ex, "An error occurred while retrieving analysis result.");
            }
            return responses;
        }

        /// <summary>
        /// 按条件查询某个日志文件的分析结果。逻辑与 <see cref="GetAnalysisResult"/> 一致，
        /// 但在流式返回逐条日志前，先用 <paramref name="request"/> 中给出的过滤条件筛选。
        /// 任一维度未填写即表示不按该维度过滤。
        /// </summary>
        public IReadOnlyList<GetAnalysisResultResponse> QueryAnalysisResult(QueryAnalysisResultRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                if (string.IsNullOrEmpty(request.FileName))
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.InvalidArgument,
                            "file_name must not be empty."),
                    });
                    return responses;
                }

                if (!analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound,
                            $"File '{request.FileName}' does not exist in the current directory."),
                    });
                    return responses;
                }

                // 头部与 GetAnalysisResult 完全一致，客户端可据此判断文件是否已分析 / 是否失败。
                responses.Add(BuildHeaderResponse(result));

                // 仅在分析成功时才进行过滤并返回日志条目。
                if (result.State == AnalysisState.Succeeded)
                {
                    var predicate = BuildQueryPredicate(request);
                    foreach (var entry in result.Entries)
                    {
                        if (predicate(entry))
                        {
                            responses.Add(BuildEntryResponse(entry));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse()
                {
                    Status = CreateInternalErrorOperationStatus(ex, "querying analysis result"),
                });
                _logger.LogError(ex, "An error occurred while querying analysis result.");
            }
            return responses;
        }

        /// <summary>
        /// 根据某个日志文件中的 Call 类型日志，推断云服务的调用拓扑。
        /// 结点 = 服务（由 pod 名去除末尾的 -&lt;索引&gt; 得到，如 gateway-0 → gateway）；
        /// 有向边 = 调用关系，由「发出 Call 日志的服务」指向 target-service，并统计该边对应的 Call 日志条数。
        /// </summary>
        public GetCallTopologyResponse GetCallTopology(GetCallTopologyRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new GetCallTopologyResponse
            {
                FileName = request.FileName,
            };
            try
            {
                if (string.IsNullOrEmpty(request.FileName))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "file_name must not be empty.");
                    return response;
                }

                if (!analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.FileNotFound,
                        $"File '{request.FileName}' does not exist in the current directory.");
                    return response;
                }

                if (result.State != AnalysisState.Succeeded)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        $"File '{request.FileName}' has not been successfully analyzed (state: {result.State}). Please analyze it first.");
                    return response;
                }

                // 用 SortedSet 保证结点顺序确定（前端布局可复现）。
                var nodes = new SortedSet<string>();
                var edgeCounts = new Dictionary<(string Source, string Target), int>();

                foreach (var entry in result.Entries)
                {
                    if (entry is CallLogEntry call)
                    {
                        string source = ExtractService(call.PodName);
                        string target = ExtractService(call.TargetService);
                        if (source.Length == 0 && target.Length == 0)
                        {
                            continue;
                        }
                        nodes.Add(source);
                        nodes.Add(target);

                        var key = (source, target);
                        edgeCounts[key] = edgeCounts.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }

                foreach (var service in nodes)
                {
                    response.Nodes.Add(new TopologyNodeMessage { Service = service });
                }
                foreach (var kv in edgeCounts)
                {
                    response.Edges.Add(new TopologyEdgeMessage
                    {
                        SourceService = kv.Key.Source,
                        TargetService = kv.Key.Target,
                        CallCount = kv.Value,
                    });
                }
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "building call topology");
                _logger.LogError(ex, "An error occurred while building call topology.");
            }
            return response;
        }

        /// <summary>
        /// 流式返回某条有向边（source_service → target_service）对应的所有 Call 日志。
        /// 复用与 GetAnalysisResult 相同的 header + 逐条日志格式，便于客户端复用结果展示逻辑。
        /// </summary>
        public IReadOnlyList<GetAnalysisResultResponse> GetEdgeCallLogs(GetEdgeCallLogsRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                if (string.IsNullOrEmpty(request.FileName) ||
                    string.IsNullOrEmpty(request.SourceService) ||
                    string.IsNullOrEmpty(request.TargetService))
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.InvalidArgument,
                            "file_name, source_service and target_service must not be empty."),
                    });
                    return responses;
                }

                if (!analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound,
                            $"File '{request.FileName}' does not exist in the current directory."),
                    });
                    return responses;
                }

                // 与 GetAnalysisResult 一致：先返回 header，客户端据此判断文件状态。
                responses.Add(BuildHeaderResponse(result));

                if (result.State == AnalysisState.Succeeded)
                {
                    foreach (var entry in result.Entries)
                    {
                        if (entry is CallLogEntry call &&
                            ExtractService(call.PodName) == request.SourceService &&
                            ExtractService(call.TargetService) == request.TargetService)
                        {
                            responses.Add(BuildEntryResponse(entry));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse()
                {
                    Status = CreateInternalErrorOperationStatus(ex, "retrieving edge call logs"),
                });
                _logger.LogError(ex, "An error occurred while retrieving edge call logs.");
            }
            return responses;
        }

        /// <summary>
        /// 按 Request ID 追踪某次请求的完整调用链（T5.2）：流式返回该 request-id 对应的所有
        /// Call / Request 日志，并按时间升序排列，供客户端绘制瀑布图。Internal 日志无 RequestId，不参与。
        /// 响应复用与 GetAnalysisResult 相同的 header + 逐条日志格式。
        /// </summary>
        public IReadOnlyList<GetAnalysisResultResponse> GetTrace(GetTraceRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var responses = new List<GetAnalysisResultResponse>();
            try
            {
                if (string.IsNullOrEmpty(request.FileName) || string.IsNullOrEmpty(request.RequestId))
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.InvalidArgument,
                            "file_name and request_id must not be empty."),
                    });
                    return responses;
                }

                if (!analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    responses.Add(new GetAnalysisResultResponse()
                    {
                        Status = CreateErrorOperationStatus(
                            AgentErrorCode.FileNotFound,
                            $"File '{request.FileName}' does not exist in the current directory."),
                    });
                    return responses;
                }

                // 与 GetAnalysisResult 一致：先返回 header，客户端据此判断文件状态。
                responses.Add(BuildHeaderResponse(result));

                if (result.State == AnalysisState.Succeeded)
                {
                    // 收集该 request-id 的全部日志，再按时间升序输出，便于客户端直接绘制瀑布图。
                    var trace = new List<LogEntry>();
                    foreach (var entry in result.Entries)
                    {
                        string? rid = entry switch
                        {
                            CallLogEntry c => c.RequestId,
                            RequestLogEntry r => r.RequestId,
                            _ => null,
                        };
                        if (rid == request.RequestId)
                        {
                            trace.Add(entry);
                        }
                    }
                    trace.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                    foreach (var entry in trace)
                    {
                        responses.Add(BuildEntryResponse(entry));
                    }
                }
            }
            catch (Exception ex)
            {
                responses.Add(new GetAnalysisResultResponse()
                {
                    Status = CreateInternalErrorOperationStatus(ex, "retrieving trace"),
                });
                _logger.LogError(ex, "An error occurred while retrieving trace.");
            }
            return responses;
        }

        /// <summary>
        /// 从 pod 名 / 服务名中提取服务名：去除末尾的 -&lt;数字&gt; 索引后缀。
        /// 例如 gateway-0 → gateway，my-svc-12 → my-svc，authservice → authservice（无后缀则原样返回）。
        /// </summary>
        private static string ExtractService(string podOrServiceName)
        {
            if (string.IsNullOrEmpty(podOrServiceName))
            {
                return "";
            }
            return PodIndexSuffixRegex.Replace(podOrServiceName, "");
        }

        private static readonly Regex PodIndexSuffixRegex = new(@"-\d+$", RegexOptions.Compiled);

        /// <summary>
        /// 根据查询请求构造一个日志条目过滤谓词。所有维度均为「未填写则不过滤」。
        /// </summary>
        private static Func<LogEntry, bool> BuildQueryPredicate(QueryAnalysisResultRequest request)
        {
            // 把 gRPC 枚举转换为领域枚举，装入集合便于 O(1) 包含判断。
            var eventTypes = request.EventTypes.Select(GrpcTypeConverter.ConvertFromGrpc).ToHashSet();
            var severities = request.Severities.Select(GrpcTypeConverter.ConvertFromGrpc).ToHashSet();

            string requestIdPattern = request.RequestIdPattern ?? "";
            string servicePattern = request.ServicePattern ?? "";

            // message 类型字段默认可空：客户端未设置时为 null，即表示不限定该侧时间边界。
            DateTimeOffset? startTime = request.StartTime is not null
                ? request.StartTime.ToDateTimeOffset()
                : null;
            DateTimeOffset? endTime = request.EndTime is not null
                ? request.EndTime.ToDateTimeOffset()
                : null;

            return entry =>
            {
                if (eventTypes.Count > 0 && !eventTypes.Contains(entry.EventType))
                {
                    return false;
                }
                if (severities.Count > 0 && !severities.Contains(entry.Severity))
                {
                    return false;
                }
                if (servicePattern.Length > 0 &&
                    !entry.PodName.Contains(servicePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (requestIdPattern.Length > 0)
                {
                    // 仅 Call / Request 日志拥有 Request ID；Internal 日志在按 Request ID 过滤时一律排除。
                    string? requestId = entry switch
                    {
                        CallLogEntry c => c.RequestId,
                        RequestLogEntry r => r.RequestId,
                        _ => null,
                    };
                    if (requestId is null ||
                        !requestId.Contains(requestIdPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                if (startTime is not null && entry.Timestamp < startTime.Value)
                {
                    return false;
                }
                if (endTime is not null && entry.Timestamp > endTime.Value)
                {
                    return false;
                }
                return true;
            };
        }

        private static GetAnalysisResultResponse BuildHeaderResponse(AnalysisResult result)
        {
            return new GetAnalysisResultResponse()
            {
                Header = new AnalysisResultHeaderMessage()
                {
                    FileName = result.FileName,
                    FullName = result.FullName,
                    State = GrpcTypeConverter.ConvertToGrpc(result.State),
                    ErrorMessage = result.ErrorMessage ?? "",
                    WorkerId = result.WorkerId,
                },
                Status = CreateNoErrorOperationStatus(),
            };
        }

        private static GetAnalysisResultResponse BuildEntryResponse(LogEntry entry)
        {
            return new GetAnalysisResultResponse()
            {
                LogEntry = GrpcTypeConverter.ConvertToGrpc(entry),
                Status = CreateNoErrorOperationStatus(),
            };
        }

        /// <summary>
        /// 将某个已分析日志文件的结果导出为 Parquet 文件（T5.1.a.a）。
        /// 输出路径可为绝对路径，或相对于当前日志目录的相对路径 / 文件名。
        /// </summary>
        public async Task<ExportAnalysisResultResponse> ExportAnalysisResultAsync(ExportAnalysisResultRequest request, TokenInfo caller, CancellationToken cancellationToken)
        {
            var analyzer = _sessions.GetOrCreate(caller.Token);
            var response = new ExportAnalysisResultResponse();
            try
            {
                if (string.IsNullOrEmpty(request.FileName))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "file_name must not be empty.");
                    return response;
                }

                if (string.IsNullOrWhiteSpace(request.OutputPath))
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        "output_path must not be empty.");
                    return response;
                }

                if (!analyzer.TryGetAnalysisResult(request.FileName, out var result) || result is null)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.FileNotFound,
                        $"File '{request.FileName}' does not exist in the current directory.");
                    return response;
                }

                if (result.State != AnalysisState.Succeeded)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidOperation,
                        $"File '{request.FileName}' has not been successfully analyzed (state: {result.State}). Please analyze it first.");
                    return response;
                }

                // 解析输出路径：相对路径以当前日志目录为基准。
                string outputDirectory = analyzer.CurrentDirectory ?? "";
                string outputPath = Path.IsPathRooted(request.OutputPath)
                    ? request.OutputPath
                    : Path.GetFullPath(Path.Combine(
                        string.IsNullOrEmpty(outputDirectory) ? Directory.GetCurrentDirectory() : outputDirectory,
                        request.OutputPath));

                if (!outputPath.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += ".parquet";
                }

                if (File.Exists(outputPath) && !request.Overwrite)
                {
                    response.Status = CreateErrorOperationStatus(
                        AgentErrorCode.InvalidArgument,
                        $"Output file already exists: '{outputPath}'. Set overwrite=true to replace it.");
                    return response;
                }

                string? dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                int count = await ParquetLogWriter.WriteAsync(outputPath, result.Entries, cancellationToken);
                response.WrittenPath = outputPath;
                response.EntryCount = count;
                response.Status = CreateNoErrorOperationStatus();
            }
            catch (Exception ex)
            {
                response.Status = CreateInternalErrorOperationStatus(ex, "exporting analysis result to parquet");
                _logger.LogError(ex, "An error occurred while exporting analysis result to parquet.");
            }
            return response;
        }

        // —— Token 管理（T5.1.a.b）。调用者已被 AgentService 校验为管理员，这里只做业务逻辑。 ——

        public CreateTokenResponse CreateToken(CreateTokenRequest request, TokenInfo caller)
        {
            var role = ConvertRole(request.Role);
            var info = _tokens.CreateToken(role, request.Note ?? "");
            _logger.LogInformation("Admin {Caller} created a new {Role} token.", Mask(caller.Token), role);
            return new CreateTokenResponse
            {
                Status = CreateNoErrorOperationStatus(),
                Token = ToMessage(info),
            };
        }

        public OperationStatusMessage DeleteToken(DeleteTokenRequest request, TokenInfo caller)
        {
            var (ok, error) = _tokens.TryDelete(request.Token ?? "");
            if (!ok)
            {
                return CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, error ?? "Failed to delete token.");
            }
            _logger.LogInformation("Admin {Caller} deleted token {Token}.", Mask(caller.Token), Mask(request.Token ?? ""));
            return CreateNoErrorOperationStatus();
        }

        public ListTokensResponse ListTokens(Empty empty, TokenInfo caller)
        {
            var all = _tokens.List();
            var response = new ListTokensResponse
            {
                Status = CreateNoErrorOperationStatus(),
                CallerToken = caller.Token,
            };
            foreach (var info in all)
            {
                response.Tokens.Add(ToMessage(info));
            }
            return response;
        }

        public OperationStatusMessage SetTokenRole(SetTokenRoleRequest request, TokenInfo caller)
        {
            var role = ConvertRole(request.Role);
            var (ok, error) = _tokens.TrySetRole(request.Token ?? "", role);
            if (!ok)
            {
                return CreateErrorOperationStatus(AgentErrorCode.InvalidArgument, error ?? "Failed to set token role.");
            }
            _logger.LogInformation("Admin {Caller} set token {Token} role to {Role}.",
                Mask(caller.Token), Mask(request.Token ?? ""), role);
            return CreateNoErrorOperationStatus();
        }

        private static TokenRole ConvertRole(TokenRoleEnum role) => role switch
        {
            TokenRoleEnum.TokenAdmin => TokenRole.Admin,
            _ => TokenRole.Normal,
        };

        private static TokenRoleEnum ConvertRole(TokenRole role) => role switch
        {
            TokenRole.Admin => TokenRoleEnum.TokenAdmin,
            _ => TokenRoleEnum.TokenNormal,
        };

        private static TokenInfoMessage ToMessage(TokenInfo info) => new()
        {
            Token = info.Token,
            Role = ConvertRole(info.Role),
            Note = info.Note,
        };

        /// <summary>
        /// 在日志中对 token 做脱敏：只保留前 6 个字符，避免完整 token 落入日志后被泄露。
        /// 启动时输出的 bootstrap admin token 是例外（由 Program.cs 显式输出，方便使用者取用）。
        /// </summary>
        private static string Mask(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "<empty>";
            }
            return token.Length <= 6 ? token + "…" : token[..6] + "…";
        }
    }
}
