# 异步与 gRPC 实验报告

## 1. 功能介绍

本项目实现了一个基于 gRPC 的远程日志分析系统，包含：
- **LogAnalyzerAgent**：常驻运行的服务端，对外提供 gRPC 服务（Ping、GetAgentStatus、ChangeDirectory、GetLogFiles、AnalyzeAll、AnalyzeFiles、GetAnalysisResult 流式返回）
- **RemoteCli**：远程控制台客户端，全部使用 `async/await` 异步调用 gRPC 接口
- **GrpcTypeConverter / GrpcLogEntryVisitor**：内部 C# 类型与 Protobuf 消息类型的双向转换，访问者模式 + 单例模式

## 2. 运行截图

### 完整功能


Dell@DESKTOP-0GJFVEM MINGW64 ~
$ cd /d/dotnet-workshop && dotnet run --project src/RemoteCli
Connecting to agent at http://localhost:5000...

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 5
Please input directory containing log files:
D:\dotnet-workshop\src\dataset

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 1
Log files:
 - basic-fail.log
 - basic-multiple.log
 - basic.log

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 1
Log files:
 - basic-fail.log
 - basic-multiple.log
 - basic.log

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 1
Log files:
 - basic-fail.log
 - basic-multiple.log
 - basic.log

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 1
Log files:
 - basic-fail.log
 - basic-multiple.log
 - basic.log

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> D:\dotnet-workshop\src\dataset
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 1
Log files:
 - basic-fail.log
 - basic-multiple.log
 - basic.log

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 2
Please input degree of parallelism (0 for processor count):
4
Please input file names separated by comma (e.g. log1.log,log2.log):
basic.log
Analysis finished.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 4
Please input file name:
basic.log
File: basic.log
  State: Succeeded
  LineNo: 0
  Timestamp: 2026-06-05T16:00:29.0450000+00:00
  PodName: userservice-0
  Severity: Info
  EventType: Call
  RequestId: 3a013a08-6853-49fc-8f06-50daeb5c1e51
  TargetService: authservice
  DurationMs: 18

  LineNo: 1
  Timestamp: 2026-06-05T16:00:31.0860000+00:00
  PodName: userservice-1
  Severity: Info
  EventType: Request
  RequestId: 1177c344-115e-4f85-b8ec-c9164d132b79
  Method: GET
  Path: /api/user/john
  StatusCode: 404

  LineNo: 2
  Timestamp: 2026-06-05T16:05:45.3220000+00:00
  PodName: gateway-0
  Severity: Error
  EventType: Internal
  ExceptionName: System.InvalidOperationException
  ExceptionMessage: Failed to load gateway routing configuration.


Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 3
Please input degree of parallelism (0 for processor count):
 4
All files analysis finished.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 4
Please input file name:
basic-fail.log
File: basic-fail.log
  State: Failed
  Analysis failed: JSON deserialization for type 'LogParser.Parser.LineParser+RequestMessage' was missing required properties including: 'method'.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 


### 鲁棒性测试

>>> abc
Invalid input, please try again.

Please choose:
1. Show log files.
2. Analyze specified log files.
3. Analyze all log files.
4. Get log file analysis result.
5. Change directory.
6. Exit.
>>> 5
Please input directory containing log files:
C:\nope
Error: DirectoryNotFound: Directory not found: C:\nope, please try again:
Please input directory containing log files:



## 3. 问答题

### Q3.1 网络应用开发与非网络应用的区别

网络应用开发需要注意用户端的请求，可以关注到，和以往的并行开发应用不同，这一部分需要使用到异步开发来解放CPU的性能，充分发挥所有功能的

### Q3.2 AI 使用情况


