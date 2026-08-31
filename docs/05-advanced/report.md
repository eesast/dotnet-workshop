## 实现内容

本章完成了 T5.1.a.c、T5.1.b.a，并在 T5.2 中对查询功能做了扩展。

| 任务 | 实现 |
| --- | --- |
| T5.1.a.c | 按日志类型、时间范围、产生日志的服务、严重级别和 Request ID 组合查询；按行号、时间、服务、严重级别、类型和 Request ID 排序 |
| T5.1.b.a | 以固定列表格显示公共字段和各日志类型的 Message 字段；Info、Warning、Error 分别使用蓝、橙、红色标签 |
| T5.2 | 增加跨字段关键词搜索、服务端分页、匹配数与严重级别统计；增加系统文件夹选择器；点击日志文件后自动显示分析结果；修复左侧分析按钮文字裁切；将服务筛选改为候选列表，将时间筛选改为日期和时间选择器；切换日志文件时自动重置筛选条件 |

## 编译与运行

### 环境

- .NET 10 SDK
- Windows、Linux 或 macOS
- 用于分析的 `.log` 文件目录

以下命令均在仓库的 `src` 目录执行。

```shell
dotnet restore dotnet-workshop.slnx
dotnet build dotnet-workshop.slnx
```

先启动 Agent：

```shell
dotnet run --project LogAnalyzerAgent/LogAnalyzerAgent.csproj
```

默认监听地址为 `http://localhost:5000`。保持 Agent 运行，在另一个终端启动桌面客户端：

```shell
dotnet run --project LogAnalyzerClient/LogAnalyzerClient.Desktop/LogAnalyzerClient.Desktop.csproj
```

## 使用方法

### 连接与分析

1. 在菜单中选择 `File > Connect...`，输入 `http://localhost:5000`。
2. 客户端与 Agent 在同一台机器上时，点击 `Browse...` 在文件夹选择器中选择日志目录，选中后会自动切换目录。连接远程 Agent 时，需要在 `Log directory` 中输入 Agent 所在机器上的绝对路径，再点击 `Change`。
3. 在左侧选择文件。可以点击 `Analyze selected` 分析选中文件，或点击 `Analyze all` 分析当前目录的全部日志。
4. 点击单个文件后，右侧会自动加载它的分析结果。未分析或分析失败时，结果区会显示对应原因。原有的右键 `View results` 操作仍然保留。

`Parallelism` 接受非负整数。设为 `0` 时由 Agent 根据处理器数量决定并行度。

### 条件查询

结果区的筛选条件可以单独使用，也可以组合使用：

- `Event type`：Call、Request 或 Internal。
- `Severity`：Info、Warning 或 Error。
- `Service`：从当前文件中已发现的服务下拉列表中选择。Agent 会将 `gateway-0`、`gateway-1` 归并为 `gateway`。
- `Request ID`：不区分大小写的精确匹配。Internal 日志没有 Request ID，因此不会匹配此条件。
- `From` 和 `To`：分别使用日期选择器和 24 小时制时间选择器。只选起始日期时从当日 00:00 开始；只选结束日期时包含该日全天。边界时间包含在查询范围内。
- `Search all fields`：不区分大小写的包含搜索，覆盖 Pod、类型、严重级别、Request ID、路径、目标服务、异常名和异常消息等字段。

设置条件后点击 `Apply`。`Reset` 会清空所有查询条件并重新加载当前文件。

从左侧切换到另一个日志文件时，日志类型、严重级别、服务、Request ID、关键词和起止时间会自动重置，新文件首次显示的是未筛选结果。排序方式和每页行数保留不变。

### 排序与分页

点击表头中的 `Line`、`Timestamp`、`Service / pod`、`Severity`、`Type` 或 `Request ID` 可以排序。第一次点击按升序排列，再次点击同一列切换为降序。当前排序方式显示在表格底部。

每页可显示 25、50、100 或 200 条记录。翻页时 Agent 只返回当前页，但结果摘要中的匹配总数和 Info、Warning、Error 数量始终针对全部匹配记录。

## 实现要点

新增的 `QueryAnalysisResult` gRPC 使用服务端流返回一个结果头和当前页的日志。结果头包含分析状态、匹配总数、页码、页大小和三种严重级别的数量。原有 `GetAnalysisResult` RPC 保留不变，因此原有 CLI 不需要跟随修改。

Agent 的处理顺序为：组合过滤、统计、排序、分页。这个顺序保证了统计数据不会被当前页截断。页大小上限为 200，避免单次查询传输过多记录。

客户端使用统一的表格行模型承载 Call、Request 和 Internal 日志。不属于当前日志类型的列保持为空，使同一列的语义保持稳定。表格水平滚动，不会为了塞入窄窗口而截掉 Message 字段。

## 验证

执行了以下命令：

```shell
dotnet build LogAnalyzerAgent/LogAnalyzerAgent.csproj --no-restore
dotnet build LogAnalyzerClient/LogAnalyzerClient.Desktop/LogAnalyzerClient.Desktop.csproj --no-restore
dotnet test test-03-async-grpc/test-03-async-grpc.csproj --no-restore
```

Agent 和桌面客户端均以 0 错误通过编译。`test-03-async-grpc` 共运行 7 项测试，全部通过。新增的 4 项测试覆盖了：

- 类型、时间、服务、严重级别和 Request ID 的组合查询。
- 子类专属字段的关键词搜索、降序排序和分页。
- 非法页码、页大小和颠倒时间范围的拒绝处理。
- 分析文件中服务名称的提取、去重与 Pod 实例后缀归并。

## 已知限制

- 查询以单个已分析文件为单位，尚不支持跨文件聚合。
- 分析是显式触发的；日志文件在分析后发生变化时，需要重新分析。
- 文件夹选择器只能浏览客户端所在机器。当 Agent 在另一台机器上时，客户端无法通过本地系统选择器浏览 Agent 的文件系统，需要手动输入远程路径。
- 表格同时展示三种日志的全部字段，在较窄窗口中需要水平滚动。

## 开发记录

这次修改中最需要先确定的是查询边界。如果只在客户端过滤，客户端仍然需要接收整个日志，分页也只能减少渲染量，不能减少传输量。因此筛选、排序、统计和分页都放在 Agent，客户端只管理查询状态和展示。

另一个具体问题是日志中记录的是 Pod 名，但用户按服务查询时更常输入 `gateway` 而不是 `gateway-1`。当前规则同时接受完整 Pod 名和“服务名 + 连字符 + 实例后缀”，解决了数据字段与用户查询习惯不一致的问题。
