# GitHub Copilot Custom Instructions

## Pull Request Review Guidelines

你只需要 Review 标题或 Description 中带有 “作业提交” 字样的 Pull Request，其他的不需要 Review。对于这些 Request，有以下要求：

1. 目标分支名和源分支名必须是 README.md 里指明的其中一个章节，例如对章节 `01-basic` 的提交，分支名为 `homework/01-basic`
2. PR 的 Description 里必须关联了正确的 Issue（即引用了相应 Issue 的编号，类似于 #32 这样）。例如 `01-basic` 的提交需要关联 `01-basic` 作业提交通道 这个 Issue（即 #32），`02-multithreading` 的提交需要关联 `02-multithreading` 作业提交通道 这个 Issue（#33）

一个示例的提交 PR 参见 #37 号 PR：https://github.com/eesast/dotnet-workshop/pull/37

如果 PR 不符合规范，请你用友好的语气指出，并告诉他解决方案。
