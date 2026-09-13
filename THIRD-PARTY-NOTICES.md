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
- 本插件**不包含** ETS2LA 的任何代码或资源，仅在运行时对其公开类型的方法进行补丁，
  并以外部 JSON 词典提供译文。ETS2LA 的名称、商标与代码版权归其作者所有。

## 本插件

- 许可：自定义许可 v1.1（见 [LICENSE](LICENSE)）
- 允许免费使用、修改与免费分发；**未做实质性独立开发之前不得以任何形式销售**（详见 LICENSE）
