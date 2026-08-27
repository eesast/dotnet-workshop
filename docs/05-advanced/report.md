# T5 高级功能项目介绍

## 程序如何编译运行

本次的程序由提供日志分析服务的 Agent 和 Avalonia 桌面客户端两部分组成，需要分别启动。先在一个终端中启动 Agent：

```shell
dotnet run --project src/LogAnalyzerAgent/LogAnalyzerAgent.csproj -c Release --no-build --launch-profile Server
```

终端显示 `Now listening on: http://localhost:5000` 后，保留该进程，再打开另一个终端启动桌面客户端：

```shell
dotnet run --project src/LogAnalyzerClient/LogAnalyzerClient.Desktop/LogAnalyzerClient.Desktop.csproj -c Release --no-build
```

客户端启动后，按以下步骤完成初始化：

1. 点击菜单栏中的 `File -> Connect...`，输入 Agent 地址 `http://localhost:5000`。如果 Agent 运行在其他主机上，则填写该主机的实际地址和端口。
2. 在 `Directory Path` 中填写 **Agent 所在机器上的** 日志目录，建议使用绝对路径。例如，要使用仓库自带的样例日志，可填写仓库中 `src/dataset` 的绝对路径。
3. 点击 `Change Directory`。文件列表会自动刷新；也可以随时点击 `Refresh` 手动刷新。
4. 设置 `DoP`（并行度），选中一个或多个日志文件后点击 `Analyze Selected`，也可以点击 `Analyze All` 分析目录中的全部日志。后续的结果筛选和拓扑显示都需要相应文件已经分析成功。

## 我实现了哪些功能？每个功能如何使用？

### 1. 控件布局自适应

我为 Avalonia 客户端增加了宽、窄两套方案。窗口宽度大于 640 像素时，日志文件和分析结果左右排列。宽度不大于 640 像素时，目录输入框与操作按钮改为分行显示，日志文件和分析结果改为上下排列。

这避免了当窗口过窄时，文字相互挤压的问题，同时也让比较宽的窗口下也能保持美观。

### 2. 云服务调用拓扑推断与可视化

Agent 会从分析成功的日志中提取 `Call` 类型记录，根据产生日志的 Pod 名称推断源服务，并使用 `TargetService` 作为目标服务。客户端收到拓扑数据后会按层排列节点，并绘制服务节点、有向边和边上的调用次数。

我对调用图含有环的情况进行了测试，能够正常运行。

这也为下一个功能提供了基础，允许每一个服务统计其被调用次数。

### 3. 日志类型筛选与调用次数排序

我在 `Analysis Result` 页签中增加了日志类型筛选器，支持 `All`、`Call`、`Request` 和 `Internal` 四种选项。选择 `Call` 时，每条日志还会显示 `CallCount` 字段，其值表示当前文件中以同一个 `TargetService` 为目标的调用总数。此外，`Call count` 下拉框可以按该数值升序或降序排列；相同调用次数的记录再按原始行号排列，以保证结果稳定。

本次开发中，我在程序整体架构的设计上采用了AI的设计，主要是因为我仍不熟悉如何设计架构是最优的。

我的提示词是：根据guidance.md，设计T5.1.a.d的功能的具体架构，注意：你的架构应尽可能保持简单。

本次开发，我的主要心得是：程序的“服务端-客户端-中间层”架构。具体来说，通过这种方式来实现了代码的高可读性与可维护性。

本markdown的格式由AI调整。