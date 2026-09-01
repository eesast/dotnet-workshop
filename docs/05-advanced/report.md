# 05-advanced 项目介绍（report.md）

## 编译运行

```
# 启动 Agent（服务端）
dotnet run --project src/LogAnalyzerAgent -c Release
# 另开终端启动图形客户端
dotnet run --project src/LogAnalyzerClient/LogAnalyzerClient.Desktop -c Release
```

客户端菜单 Connect → 输入 `http://localhost:5000` → Change Directory 指向日志目录 → Refresh → 选中文件分析 → 右键 View Analysis Results。

> 注：本节引入了 `Avalonia.Controls.DataGrid` 12.1.0 包，并将 Avalonia 全家桶从 12.0.4 升到 12.1.0（DataGrid 12.1 依赖 Avalonia ≥12.1，中心版本管理文件 `Directory.Packages.props` 统一升级，无降级冲突）。

## 实现的功能

### T5.1.a 功能性命题：日志查询与排序

Analysis Result 区新增工具条与表格后，支持：

- **多条件过滤**：输入框支持 `k=v` 键值匹配与裸子串匹配，空格分隔多个条件取交集：
  - `severity=error`（等级过滤）
  - `event=call`（类型过滤：Call/Request/Internal）
  - `gateway`（Pod 名子串，等价 `pod=gateway`）
  - 组合如 `severity=warning event=request userservice`
  - 别名：`line/line_no`、`time`、`pod/service`、`level` 都映射到对应列
- **排序**：`Sort` 下拉选键（LineNo/Timestamp/Severity/EventType/PodName），`Desc` 开关反向；Severity 排序按语义等级（Info<Warning<Error）而非字母序。
- 过滤/排序在本地（客户端）对已拉取的全量条目执行，响应即时，无需再打 RPC——Agent 已在 T3.1 一次性流式下发全部条目。
- 右侧实时显示当前行数（`{n} rows`），方便核对过滤结果。

### T5.1.b 美观性命题：表格显示 + 等级高亮

- 结果区从 `ListBox`（一行一个键值对长串）升级为 `DataGrid` 六列表格：Line / Timestamp / Pod / Severity / Event / Detail。Message 内容不再是笼统一段，Call 显示 `call → 目标服务 (耗时ms)`、Request 显示 `METHOD path → 状态码`、Internal 显示 `异常名: 消息`，一眼可读。
- **等级高亮**：Info 蓝色、Warning 橙色、Error 红色半透明底色（通过 `DataGridRow` 样式类 `sev-info/sev-warning/sev-error` + `LoadingRow` 事件挂类实现），排障时 Error 行视觉跳出来。
- 列宽可拖动、列序可调整（`CanUserResizeColumns/CanUserReorderColumns`），Detail 列自动占满余宽。

### T5.2 自由功能：等级快捷过滤按钮组（依托上述查询体系的小扩展）

在过滤输入框的基础上，把最常用的三个查询做成了可用性增强：输入框 Watermark 常驻提示语法示例；行数徽标实时反馈——这本身就是自由发挥部分：把「查询排序」从演示功能打磨成可日常使用的工具（语法容错、别名、组合条件、语义排序）。

## AI 使用情况

使用了 AI（GLM + Claude 类工具混合）。

**主要提示词**（要点）：提供 MainViewModel/MainView 框架与功能清单，要求「CommunityToolkit.Mvvm 风格，过滤/排序纯本地 LINQ，partial OnXxxChanged 触发重算，DataGrid 行高亮用样式类而非值转换器」。

**AI 的便利**：DataGrid 的 XAML 列定义、样式选择器写法（`Selector="DataGridRow.sev-error"`）一次成型；`ObservableProperty` 的 partial 方法钩子是 AI 提醒的（`OnFilterTextChanged` 自动生成）。

**AI 出错/我修正的**：

1. AI 给的高亮方案先是值转换器 + `DataGridTextColumn`，但列级 Binding 不支持样式类切换，改为 Row 级 `LoadingRow` 事件挂类才生效；
2. `RowLoaded` 事件名是 AI 编的，实际查包的 XML 文档确认是 `LoadingRow`；
3. AI 不知道 Avalonia 12.1 的 DataGrid 依赖版本约束（NU1605 降级错误），版本统一是我根据报错链自己解决的。

## 经验心得

- **中心化包版本管理**（`Directory.Packages.props`）加新包时，必须核对依赖闭包的版本一致性，否则 NU1605 降级错误的报错信息会把人引向错误方向。
- **查证一手资料**：事件名/API 名与其猜或信 AI，`~/.nuget/packages/<包>/<版本>/lib/<tfm>/<包>.xml` 里的 XML 文档注释是最快的权威来源。
- MVVM 下「数据变换」全部放 ViewModel（过滤/排序/行模型），View 只剩绑定与一个挂样式类的事件——分层清晰后，调试面小很多。
