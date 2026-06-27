# 科协暑培（.NET 部分）学习型工程

## 介绍

本项目是为 [清华大学电子工程系学生科学技术协会](https://eesast.com) 暑期培训（以下简称「暑培」）的 .NET 部分（或称队式部分）所编写的学习与训练型项目。本项目将带领学生通过制作一款简单的软件来学习 .NET 技术栈，以及其他在未来一年的 [队式程序设计大赛](https://eesast.com/#/contest/list) 开发过程中所用到的技术栈。

由于暑培具有时间短、覆盖面广，且每人负责一部分的特点，而每位主讲人都希望在自己的部分倾囊相授，以达到「速成」的效果，因此以往的倾向于选择知识密集型的教学方式，或多或少造成了「填鸭式」、「量子波动速读」的效果。一项技术（特别是编程领域）的知识点既多又散，即便主讲人们努力抓住主干脉络，也难免落入长篇累牍堆砌知识点的境地，不仅让听者产生厌烦，也不利于同学们掌握这门技术在实际开发当中的应用。

由此，[FranGuam](https://github.com/FranGuam) 创新性地为暑培的网站部分编写了 [web-workshop](https://github.dev/eesast/web-workshop) 用于暑培网站部分的学习，极大地提升了学生们的学习积极性。由此，队式的部分拟参考网站部分的成功经验，也开发了一套学习型工程用于队式部分的学习。

## 学习内容

本工程将实现一个建议的云服务日志分析系统。通过本学习工程，你将学习到以下内容：

1. **C\# 语言与面向对象程序设计。** .NET 是由 [微软公司](https://www.microsoft.com/) 开发的一套开源的软件开发框架与运行时，而 C\# 是适用 .NET 进行软件开发的首选语言。同时，C\# 语言和 .NET SDK 提供了面向对象程序设计的优秀支持。本项目将带领大家了解基本的 .NET 开发环境、熟悉 C\# 的基本语法，并学习一些面向对象程序设计的思想。
2. **多线程并发程序设计。** 进程是操作系统进行分时多任务的抽象以及资源分配的基本单位，而线程是进程当中用于分时多任务的抽象。很多时候，我们的程序需要并发地运行多个任务，而非顺序地运行，这时我们就需要多个线程进行协作。虽然现代应用程序直接使用线程已不多见，线程大都被封装于线程池、异步、并行计算等框架中，但了解线程的原理仍有价值。本项目将带领大家了解何为多线程，以及多线程编程遇到的同步和互斥等问题。
3. **异步与通信。** 在互联网时代，网络应用已屡见不鲜，应用程序之间通过网络进行通信更是常态。而异步则是这种进行网络通信时为进行线程复用以及处理回调地狱等问题所设计的编程模型。本项目将带大家学习由 Google 公司开发的 **远程过程调用（RPC）** 通信框架 [gRPC](https://grpc.io/)，并使用其异步接口来进行网络应用开发。
4. **Avalonia UI。** [Avalonia UI](https://avaloniaui.net/) 是一款开源、跨平台的图形界面（GUI）开发框架，其基于 .NET 框架，是微软官方 GUI 开发框架 [WPF](https://learn.microsoft.com/zh-cn/dotnet/desktop/wpf/overview/) 的跨平台实现。本项目将带您使用 Avalonia UI 设计一个简要的图形界面。
5. **Unity。** Unity 是一款流行的游戏开发引擎，其主要使用 C\# 语言进行开发，也是暑培队式部分的内容。但本项目尚未将 Unity 进行收录，原因在于将 Unity 收录其中稍显困难，尤其是 Unity 项目难以使用 GitHub 进行托管。至于原因为何，参见 [开发札记](./DEVLOG.md) 中关于 Unity 部分的叙述。后续是否可以将 Unity 也加入进来，留待后人的智慧。

## 开始学习

如何开始学习本工程？

本工程的全部文档位于 `docs/` 目录中。首先，同学应当阅读 `docs/00-prepare` 中的文档，进行环境准备与背景了解，以及作业的提交方式。随后，按照数字顺序依次阅读该章节下的指引（`guidance.md`）并完成任务（`tasks.md`）。

+ `00-prepare`
  + [Guidance](./docs/00-prepare/guidance.md)
  + [Tasks](./docs/00-prepare/tasks.md)
+ `01-basic`
  + [Guidance](./docs/01-basic/guidance.md)
  + [Tasks](./docs/01-basic/tasks.md)
+ `02-multithreading`
  + [Guidance](./docs/02-multithreading/guidance.md)
  + [Tasks](./docs/02-multithreading/tasks.md)
+ `03-async-grpc`
  + [Guidance](./docs/03-async-grpc/guidance.md)
  + [Tasks](./docs/03-async-grpc/tasks.md)
+ `04-avalonia`
  + [Guidance](./docs/04-avalonia/guidance.md)
  + [Tasks](./docs/04-avalonia/tasks.md)

## 后记

本项目受到 [FranGuam](https://github.com/FranGuam) 编写的 [web-workshop](https://github.com/eesast/web-workshop) 启发，在此表示感谢。

关于开发当中的经历和感悟 ，以及开发幕后的故事和花絮，欢迎阅读 [开发札记](./DEVLOG.md)。

## 开源许可

本项目的全部代码通过 [MIT 许可证](./LICENSE-code.txt)（SPDX-License-Identifier：MIT）进行开源；本项目的全部文档通过 [署名—相同方式共享 4.0 协议国际版](./LICENSE-document.txt)（SPDX-License-Identifier：CC-BY-SA-4.0）进行开源。