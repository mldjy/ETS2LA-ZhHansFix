# 第三方组件声明

本项目为第三方社区插件，与 ETS2LA 官方无隶属关系。

## Lib.Harmony

- 项目：https://github.com/pardeike/Harmony
- 许可：MIT License
- 版权：Copyright (c) 2017 Andreas Pardeike
- 用途：在运行时为本插件挂载/撤销方法补丁。本仓库不包含其二进制文件，请通过
  `scripts/fetch-harmony.ps1` 从 NuGet 自行获取，或使用 Release 安装包中随附的
  `0Harmony.dll`。

## ETS2LA

- 项目：https://github.com/ETS2LA/ETS2LA
- 本插件为**独立创作**的第三方插件：独立编写，未使用 ETS2LA 的任何代码、资源或二进制文件。
- 本插件**不修改磁盘上的任何 ETS2LA 程序文件**：它通过 ETS2LA 自带的插件机制加载，
  所有改动仅在进程内存中生效（运行时挂载界面语言层，译文由外部 JSON 词典提供）。
- ETS2LA 的名称、商标与代码版权归其作者所有。
