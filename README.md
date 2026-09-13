# ETS2LA 简体中文补全（ZhHansFix）

给 [ETS2LA](https://github.com/ETS2LA/ETS2LA)（欧卡2 / 美卡自动驾驶辅助）补全**简体中文界面**的第三方插件：程序本体与各插件的界面文案。不改动 ETS2LA 的任何文件。

## 覆盖范围

- 程序本体界面：主菜单、设置各页、更新页
- 插件管理 / 插件库：插件名称、说明、作者行、标签
- 各插件界面：车道辅助、自适应巡航控制（ACC）、内部可视化、路径规划，含各自设置页
- 状态信息与 ACC 约束窗口

内置词典 337 条。

## 安装

1. 到 [Releases](../../releases) 下载最新的 `ZhHansFix-vX.Y.Z.zip`
2. 解压后运行安装脚本（自动复制文件夹并登记到 ETS2LA 的插件清单）：

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

也可以手动安装：把 `mldjy.zhhansfix` 整个文件夹复制到 `%LOCALAPPDATA%\ETS2LA\current\Plugins\`，
再在 `%APPDATA%\ETS2LA\InstalledPluginManifest.json` 的 `InstalledPlugins` 里加一条：

```json
{ "Id": "mldjy.zhhansfix", "Version": "1.0.0",
  "DllPath": "Plugins\\mldjy.zhhansfix\\ZhHansFix.dll",
  "Dependencies": [], "Type": 0 }
```

> ETS2LA 只自动扫描 `Plugins\` 根目录下的 DLL，**子文件夹里的插件必须登记清单才会被加载**。

3. 重启 ETS2LA
4. 到 `插件管理` 里启用「简体中文补全」

> 语言需为简体中文：`设置 → 显示 → 语言` 选「简体中文」。

## 启用 / 停用

在「插件管理」里勾选或取消勾选即可。

## 卸载

运行 `uninstall.ps1`，或手动删除插件文件夹与清单条目。

## 扩展词典

词典是一个外置 JSON 文件，改完存盘立即生效，不用重启程序：

```
%APPDATA%\ETS2LA\zh-hans-fix.json
```

文件里放一组「原文 → 译文」的对照：**引号里左边写界面上出现的英文原文，右边写它对应的中文译文**。

```json
{
  "Enable Lane Changes": "启用变道",
  "Path Length": "路径长度",
  "Render Leading Vehicle": "渲染前车"
}
```

这三条是最普通的写法：英文整段完全相同才替换。

### 标签与数值拼成一整句的情况

有些文案是「标签 + 数值」拼起来的，例如 `Speed: 39.7 km/h`、`Sorted 0 vehicles.`。
这种要在**原文的开头或结尾故意留一个空格**，只替换标签、数值原样保留。

空格写在引号里看不出来，下面用一条**深黄细线**标出空格的位置（它就是一个空格，写进文件时照常敲空格）：

| 原文的写法 | 译文 | 匹配方式 |
|---|---|---|
| "Path Length" | "路径长度" | 首尾都不带空格 → 整段完全相同才替换 |
| "Speed:" | "速度：" | 原文**结尾**是冒号 → 只替换开头那一段 |
| "By![空格](docs/space-underline.png)" | "作者：" | 原文**结尾**是空格 → 只替换开头那一段 |
| "![空格](docs/space-underline.png)vehicles." | "![空格](docs/space-underline.png)辆车。" | 原文**开头**是空格 → 只替换结尾那一段 |

效果：

```
Speed: 39.7 km/h        ->  速度：39.7 km/h
By Tumppi066            ->  作者：Tumppi066
Sorted 0 vehicles.      ->  已排序 0 辆车。
```

对应的 JSON 写法（这里的空格是真的空格，可直接照抄）：

```json
{
  "Speed:": "速度：",
  "By ": "作者：",
  " vehicles.": " 辆车。"
}
```

## 自行编译

需要 .NET 10 SDK（ETS2LA 是 net10.0）。

```powershell
cd <仓库所在目录>
powershell -ExecutionPolicy Bypass -File scripts\fetch-harmony.ps1
dotnet build ZhHansFix.Core\ZhHansFix.Core.csproj -c Release
dotnet build ZhHansFix\ZhHansFix.csproj -c Release
```

产物为 `ZhHansFix.dll`、`ZhHansFix.Core.dll`，连同 `lib\0Harmony.dll` 一起放进 `Plugins\mldjy.zhhansfix\`。

## 已知限制

- 仅适用于 Windows 版 ETS2LA

## 免责声明

- 翻译覆盖**尽力而为**，不保证界面所有位置都已翻译完整（个别位置仍可能显示英文）。
- 不保证在 **ETS2LA 或游戏版本更新后仍然兼容**。
- 本项目**不保证后续持续更新与维护**。

## 许可

采用自定义许可（v1.0，见 [LICENSE](LICENSE)）：

- **允许**：免费使用、修改，以及免费分发原版或修改版
- **不允许**：在未做实质性独立开发之前，以任何形式直接或间接销售本插件；
  仅做非关键修改（例如换成其它语言的翻译词表、调整执行顺序等细微改动）
  不构成可销售的理由
- 运行补丁依赖 [Lib.Harmony](https://github.com/pardeike/Harmony)（MIT），其自身许可不受本协议影响
- 本项目为第三方社区插件，与 ETS2LA 官方**无隶属关系**；ETS2LA 商标与版权归其作者所有
