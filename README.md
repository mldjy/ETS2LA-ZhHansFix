# ETS2LA 简体中文补全（ZhHansFix）

给 [ETS2LA](https://github.com/ETS2LA/ETS2LA)（欧卡2 / 美卡自动驾驶辅助）补全**简体中文界面**的第三方插件。

ETS2LA 自带的简中翻译只覆盖了程序本体的一部分，**插件设置页、在线插件库的名称与说明、状态信息窗口、更新页**等大量文案没有翻译。本插件在运行时接管这些文案的取值入口来补全，**不改动 ETS2LA 的任何文件**。

---

## 效果

- 程序本体界面：主菜单、设置各页、更新页、插件管理、插件库
- 插件界面：车道辅助 / 自适应巡航控制（ACC）/ 内部可视化 / 路径规划 / 各插件设置页
- 插件库：插件名称、说明、作者行、标签
- 状态信息与 ACC 约束窗口：标签、说明句、`True/False` 等取值

内置词典 337 条，全部实测生效。

---

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

> ETS2LA 只自动扫描 `Plugins\` 根目录下的 DLL，**子文件夹里的插件必须登记清单才会被加载**

3. 重启 ETS2LA
4. 到 `插件管理` 里**启用「简体中文补全」**（默认不启用，未启用时不会有任何改动）

启用后日志中应出现：

```
_+_n+imgui …+meta …+net … patched in …ms, dictionary 337 entries
```

界面即变为简体中文。

> 语言需为简体中文：`设置 → 显示 → 语言` 选「简体中文」。

## 启用 / 停用

在 `插件管理` 里勾选或取消勾选即可，**瞬时生效**。

- 补丁在程序启动阶段就已预挂好（此时**不生效**），启用只是打开开关
- 停用只是关闭开关，界面立刻恢复英文；再次启用同样瞬时
- 未启用时，本插件不会改动任何界面文本

## 卸载

删除插件目录下的这三个文件后重启即可：

```
ZhHansFix.dll
ZhHansFix.Core.dll
0Harmony.dll      （若其它插件也用到，请不要删除）
```

程序目录里由本插件同步的 `Localization\zh_Hans.po`（程序目录及各插件目录）可一并删除；它们属于官方支持的本地化文件，程序更新时也会被覆盖。

---

## 工作原理

ETS2LA 的界面文案有四类来源，必须分别接管，缺一层就会出现「部分仍是英文」：

| 层 | 接管的入口 | 覆盖范围 |
|---|---|---|
| ① | `ETS2LA.Translations.T._()` / `T._n()` | 程序与插件的通用文案入口 |
| ② | `Hexa.NET.ImGui.ImGui` 全部静态方法的文案参数 + `ImDrawList.AddText` | 插件自画的设置页、叠加层窗口 |
| ③ | `ETS2LA.Shared.PluginInformation.Name / Description` | 已安装插件的卡片 |
| ④ | `ETS2LA.Networking.Plugins.NetworkPlugin.Name / Description` | 在线插件库的卡片（与 ③ 不是同一个类） |

③④ 直接替换**属性取值入口**，因此与渲染方式无关，也不受「元数据在启动时已被缓存」的影响。

查表时还有三条通用规则：

- **换行归一化**：不少说明句在插件里本身带换行，先把换行换成空格再查一遍
- **前缀 / 后缀替换**：处理 `"Speed: " + 数值`、`"Sorted " + n + " vehicles."` 这类拼接写法
- **按行回退**：整串查不到时，逐行翻译再拼回

## 扩展词典

词典是**外置 JSON、热加载**的，改完存盘**立即生效**，不用重启程序：

```
%APPDATA%\ETS2LA\zh-hans-fix.json
```

格式：

```json
{
  "Enable Lane Changes": "启用变道",
  "Path Length": "路径长度"
}
```

- 以冒号或空格结尾的键会被当作**前缀**匹配；以空格开头的键会被当作**后缀**匹配
- 插件会把「界面上真实渲染过、但译不出来」的原文记到 `%APPDATA%\ETS2LA\zh-hans-missing.txt`，
  把「已译生效」的原文记到 `%APPDATA%\ETS2LA\zh-hans-served.txt`，便于按需补词

---

## 目录结构

```
ZhHansFix\                  插件本体（被 ETS2LA 加载）
ZhHansFix.Core\             补丁实现（必须加载进默认/不可回收上下文，原因见下）
dict\zh-hans.json           内置词典（编译进 ZhHansFix.Core.dll）
scripts\fetch-harmony.ps1   从 NuGet 拉取 Lib.Harmony 到 lib\0Harmony.dll
tools\collect.py            收割结果查看工具
```

## 自行编译

需要 .NET 10 SDK（ETS2LA 是 net10.0）。

```powershell
cd <仓库所在目录>
powershell -ExecutionPolicy Bypass -File scripts\fetch-harmony.ps1
dotnet build ZhHansFix.Core\ZhHansFix.Core.csproj -c Release
dotnet build ZhHansFix\ZhHansFix.csproj -c Release
```

产物：`ZhHansFix.dll`、`ZhHansFix.Core.dll`（连同 `lib\0Harmony.dll` 一起放进 `Plugins\`）。

> **为什么要拆成两个程序集**：ETS2LA 用**可回收 (collectible) AssemblyLoadContext** 加载插件，Harmony 生成的补丁包装方法无法引用可回收程序集里的补丁方法（会报 `Operation is not supported`）。
> 所以补丁代码必须放在一个加载进**默认（不可回收）上下文**的独立程序集里，由插件在运行时按路径显式装载。

---

## 已知限制

- **启动阶段会预挂补丁，约 8 秒**（要挂 6000 多个 ImGui 方法）。这段时间与程序解析地图数据同时进行，菜单、按钮均可用，不影响使用；此后启用/停用都是瞬时的。
- 若某段文字被插件直接写死在绘制调用里、既不经过任何翻译入口也不经过 ImGui 文案参数，任何插件都触达不到（本插件已覆盖 6000+ 个入口，实测剩余为 0）。
- 程序目录里的 `.po` 属于官方支持的本地化文件，但**程序更新会覆盖**；`%APPDATA%` 那份运行时词典不受影响，两者互为补充。
- 仅适用于 Windows 版 ETS2LA。

## 免责声明

- 翻译覆盖**尽力而为**，不保证界面所有位置都已翻译完整（个别位置仍可能显示英文）。
- 不保证在 **ETS2LA 或游戏版本更新后仍然兼容** —— 上游内部实现变动可能导致补丁跳过或部分失效。
- 本项目**不保证后续持续更新与维护**。

## 许可

- 本插件：MIT —— 见 [LICENSE](LICENSE)
- 运行补丁依赖 [Lib.Harmony](https://github.com/pardeike/Harmony)（MIT）
- 本项目为第三方社区插件，与 ETS2LA 官方**无隶属关系**；ETS2LA 商标与版权归其作者所有

详见 `THIRD-PARTY-NOTICES.md`。
