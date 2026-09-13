// ImGuiPatcher — 第二层补丁：拦截 ImGui 控件调用的文案参数。
//
// 背景：ETS2LA 的插件设置页/调试窗口是插件自己用 ImGui 画的，文案直接写在调用参数里，例如
//     ImGui.SliderFloat("Positive Pedal Multiplier", ref value, 0, 1);
//     ImGui.Checkbox("Render All Semaphores", ref flag);
// 这些字符串从不经过 ETS2LA.Translations.T._()，因此第一层补丁（挂钩本地化入口）够不到它们。
//
// 本层挂钩 Hexa.NET.ImGui.ImGui 上所有带文案参数的静态方法（参数名为 label/text/fmt/name/str_id
// 的 string 参数），用同一本词典把文案替换成中文；词典里没有的则记进缺失清单，供后续补译。
//
// 说明：ImGui 用标签字符串派生控件 ID，因此替换文案会使该控件/窗口的 ID 变化，
// 表现为折叠状态、窗口位置等一次性重置。对滑条、勾选框这类无持久状态的控件没有影响。
//
// 本文件不引用任何 ImGui 类型（补丁方法只用 ref string），因此无需编译期依赖 Hexa.NET。

using System.Reflection;
using HarmonyLib;

namespace ZhHansFix.Core;

internal static class ImGuiPatcher
{
    /// <summary>会被替换的 string 参数名（ImGui 的文案参数；id 类参数只在词典精确命中时才改）。</summary>
    private static readonly string[] CandidateParams =
    {
        "label", "text", "fmt", "name", "str_id", "label_id", "text_end",
        "desc", "description", "tooltip", "title", "message", "content", "caption", "help", "id", "format"
    };

    /// <summary>为某个参数名找到对应的补丁方法（按名字绑定，Harmony 只注入同名参数）。</summary>
    private static readonly Dictionary<string, string> PrefixByParam = new(StringComparer.Ordinal)
    {
        ["label"]    = nameof(ImGuiPatches.PrefixLabel),
        ["text"]     = nameof(ImGuiPatches.PrefixText),
        ["fmt"]      = nameof(ImGuiPatches.PrefixFmt),
        ["name"]     = nameof(ImGuiPatches.PrefixName),
        ["str_id"]   = nameof(ImGuiPatches.PrefixStrId),
        ["label_id"] = nameof(ImGuiPatches.PrefixLabelId),
        ["text_end"] = nameof(ImGuiPatches.PrefixTextEnd),
        // 其余候选参数名统一走同一个前缀（方法内按参数名绑定，这里用通用前缀）
        ["desc"]        = nameof(ImGuiPatches.PrefixDesc),
        ["description"] = nameof(ImGuiPatches.PrefixDescription),
        ["tooltip"]     = nameof(ImGuiPatches.PrefixTooltip),
        ["title"]       = nameof(ImGuiPatches.PrefixTitle),
        ["message"]     = nameof(ImGuiPatches.PrefixMessage),
        ["content"]     = nameof(ImGuiPatches.PrefixContent),
        ["caption"]     = nameof(ImGuiPatches.PrefixCaption),
        ["help"]        = nameof(ImGuiPatches.PrefixHelp),
        ["id"]          = nameof(ImGuiPatches.PrefixId),
        ["format"]      = nameof(ImGuiPatches.PrefixFormat),
    };

    internal static int Applied;

    private static Harmony? _harmonyRef;
    private static readonly HashSet<Assembly> PatchedCopies = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 给所有 Hexa.NET.ImGui 副本挂补丁。
    ///
    /// ETS2LA 的插件加载器（ETS2LA.Backend/PluginHandler/PluginLoadContext）只把
    /// System./Microsoft./ETS2LA. 前缀的程序集共享到主上下文，Hexa.NET.ImGui 不在其中，
    /// 于是每个插件都会加载自己的一份副本。只给主上下文那份挂补丁的话，
    /// 插件自绘窗口里的文案（例如 ACC 约束的 ImGui.TextColored）就漏掉了。
    /// 因此这里遍历所有已加载副本，并监听之后加载进来的副本。
    /// </summary>
    public static int Apply(Harmony harmony)
    {
        _harmonyRef = harmony;

        int patched = 0;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                patched += PatchAssembly(asm);
        }
        catch { }

        try
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }
        catch { }

        Applied = patched;
        return patched;
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs e)
    {
        try
        {
            if (e.LoadedAssembly.GetName().Name == "Hexa.NET.ImGui")
                PatchAssembly(e.LoadedAssembly);
        }
        catch { }
    }

    /// <summary>对某一份 Hexa.NET.ImGui 程序集挂补丁；同一份只挂一次。</summary>
    internal static int PatchAssembly(Assembly asm)
    {
        if (asm == null || _harmonyRef == null) return 0;
        try
        {
            if (asm.GetName().Name != "Hexa.NET.ImGui") return 0;
        }
        catch { return 0; }

        lock (PatchedCopies)
        {
            if (!PatchedCopies.Add(asm)) return 0;
        }

        int patched = 0;
        try
        {
            var imgui = asm.GetType("Hexa.NET.ImGui.ImGui");
            if (imgui != null) patched += PatchTextParams(imgui);
        }
        catch { }
        foreach (var typeName in new[] { "Hexa.NET.ImGui.ImDrawListPtr", "Hexa.NET.ImGui.ImDrawList" })
        {
            try
            {
                var t = asm.GetType(typeName);
                if (t != null) patched += PatchDrawList(t);
            }
            catch { }
        }
        return patched;
    }

    /// <summary>ImGui 上带文案参数的静态方法（每个方法挂一个前缀）。</summary>
    private static int PatchTextParams(Type imgui)
    {
        int patched = 0;
        MethodInfo[] methods;
        try
        {
            methods = imgui.GetMethods(BindingFlags.Public | BindingFlags.Static);
        }
        catch (Exception)
        {
            return 0;
        }

        foreach (var m in methods)
        {
            try
            {
                if (m.IsGenericMethodDefinition || m.ContainsGenericParameters) continue;
                if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_")) continue;

                ParameterInfo[] ps;
                try { ps = m.GetParameters(); } catch { continue; }

                foreach (var p in ps)
                {
                    if (p.ParameterType != typeof(string)) continue;
                    if (p.Name == null || !PrefixByParam.TryGetValue(p.Name, out var prefixName)) continue;

                    var prefix = typeof(ImGuiPatches).GetMethod(prefixName, BindingFlags.Public | BindingFlags.Static);
                    if (prefix == null) continue;

                    _harmonyRef!.Patch(m, prefix: new HarmonyMethod(prefix));
                    patched++;
                    break;   // 每个方法挂一个前缀即可
                }
            }
            catch { /* 单个方法失败不影响其它 */ }
        }
        return patched;
    }

    /// <summary>ImDrawList / ImDrawListPtr 上的文本绘制（实例方法，插件常用它画段落文字）。</summary>
    private static int PatchDrawList(Type t)
    {
        int patched = 0;
        MethodInfo[] methods;
        try { methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance); } catch { return 0; }

        foreach (var m in methods)
        {
            try
            {
                var ps = m.GetParameters();
                string? beginName = null, endName = null;
                foreach (var p in ps)
                {
                    if (p.ParameterType != typeof(string)) continue;
                    if (p.Name == "textBegin" || p.Name == "text") beginName = p.Name;
                    if (p.Name == "textEnd" || p.Name == "text_end") endName = p.Name;
                }
                if (beginName == null) continue;   // 只处理能拿到正文的参数

                // textBegin 有配对 textEnd 的重载要额外校验（见 ImGuiPatches），因此分成两个前缀
                var prefixName = endName != null
                    ? nameof(ImGuiPatches.PrefixDrawTextBeginEnd)
                    : nameof(ImGuiPatches.PrefixDrawTextBegin);
                var prefix = typeof(ImGuiPatches).GetMethod(prefixName, BindingFlags.Public | BindingFlags.Static);
                if (prefix == null) continue;
                _harmonyRef!.Patch(m, prefix: new HarmonyMethod(prefix));
                patched++;
            }
            catch { }
        }
        return patched;
    }

    // 供补丁方法使用

    internal static void Translate(ref string value, bool harvest)
    {
        try
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!Patcher.IsSimplifiedChinese()) return;

            // ImGui 标签可用 "显示文字##内部ID" 形式，译文只替换显示部分
            var raw = value;
            var idPart = string.Empty;
            var idx = raw.IndexOf("##", StringComparison.Ordinal);
            if (idx >= 0)
            {
                idPart = raw[idx..];
                raw = raw[..idx];
            }

            if (raw.Length == 0) return;

            if (Patcher.TryTranslate(raw, out var translated))
            {
                value = translated + idPart;
                Patcher.OverridesServed++;
                Patcher.RecordServed(raw);
            }
            else if (Patcher.TryTranslatePrefixed(raw, out var prefixed))
            {
                // 标签与数值拼在一个字符串里（"Speed: 39.7"）：只替换标签部分
                value = prefixed + idPart;
                Patcher.OverridesServed++;
            }
            else if (harvest)
            {
                // 只收割"看起来像界面文案"的：含空格或长度>3，且不是格式串/ID 串
                if (LooksLikeUiText(raw)) Patcher.RecordMissing(raw);
            }
        }
        catch { }
    }

    private static bool LooksLikeUiText(string s)
    {
        if (s.Length < 4 || s.Length > 200) return false;
        if (s.Contains('%')) return false;                              // printf 格式串
        if (s.StartsWith('#') || s.StartsWith(' ')) return false;       // 内部 ID / 日志缩进
        if (Patcher.HasCjk(s)) return false;                            // 已是中文（译文或原文就是中文）
        if (s.Contains('\\') || s.Contains(":/") || s.Contains("\\\\")) return false;   // 文件路径
        if (s.Contains("[INF]") || s.Contains("[DBG]") || s.Contains("[WRN]") ||
            s.Contains("[ERR]") || s.Contains("[OKK]")) return false;   // 日志级别标签
        if (s.Contains("://") || s.Contains("api.ets2la")) return false;
        if (char.IsDigit(s[0])) return false;                           // 数字开头（统计行等）
        if (s.Contains(" : ")) return false;
        if (s.Count(c => c == ' ') > 12) return false;                  // 长句（多为日志/调试输出）
        if (s.Contains("km/h") || s.Contains('°') || s.Contains('∆')) return false;  // 调试叠加层的数值行
        if (s.Count(char.IsDigit) >= 4) return false;                   // 动态数值
        if (!s.Contains(' ') && !s.Any(char.IsUpper)) return false;      // 单个小写词多半是 id
        return true;
    }
}

public static class ImGuiPatches
{
    public static void PrefixLabel(ref string label) => ImGuiPatcher.Translate(ref label, true);
    public static void PrefixText(ref string text) => ImGuiPatcher.Translate(ref text, true);
    public static void PrefixFmt(ref string fmt) => ImGuiPatcher.Translate(ref fmt, true);
    public static void PrefixName(ref string name) => ImGuiPatcher.Translate(ref name, true);
    public static void PrefixStrId(ref string str_id) => ImGuiPatcher.Translate(ref str_id, false);
    public static void PrefixLabelId(ref string label_id) => ImGuiPatcher.Translate(ref label_id, false);
    public static void PrefixTextEnd(ref string text_end) => ImGuiPatcher.Translate(ref text_end, false);

    // 其余候选参数名：Harmony 按参数名绑定，因此每个名字都要有一个同名参数的前缀
    public static void PrefixDesc(ref string desc) => ImGuiPatcher.Translate(ref desc, true);
    public static void PrefixDescription(ref string description) => ImGuiPatcher.Translate(ref description, true);
    public static void PrefixTooltip(ref string tooltip) => ImGuiPatcher.Translate(ref tooltip, true);
    public static void PrefixTitle(ref string title) => ImGuiPatcher.Translate(ref title, true);
    public static void PrefixMessage(ref string message) => ImGuiPatcher.Translate(ref message, true);
    public static void PrefixContent(ref string content) => ImGuiPatcher.Translate(ref content, true);
    public static void PrefixCaption(ref string caption) => ImGuiPatcher.Translate(ref caption, true);
    public static void PrefixHelp(ref string help) => ImGuiPatcher.Translate(ref help, true);
    public static void PrefixId(ref string id) => ImGuiPatcher.Translate(ref id, false);
    public static void PrefixFormat(ref string format) => ImGuiPatcher.Translate(ref format, true);

    /// <summary>ImDrawList 上的文本绘制（插件常直接用绘制列表画段落文字）。</summary>
    public static void PrefixDrawText(ref string text) => ImGuiPatcher.Translate(ref text, true);
    public static void PrefixDrawTextEnd(ref string text_end) => ImGuiPatcher.Translate(ref text_end, false);

    // ImDrawList.AddText 的正参叫 textBegin（不是 text）；带 textEnd 的重载只在 textEnd 为空时才替换，
    // 否则会破坏调用方给出的子串范围。
    public static void PrefixDrawTextBegin(ref string textBegin)
        => ImGuiPatcher.Translate(ref textBegin, true);

    public static void PrefixDrawTextBeginEnd(ref string textBegin, string textEnd)
    {
        if (!string.IsNullOrEmpty(textEnd)) return;   // 显式子串范围，不动
        ImGuiPatcher.Translate(ref textBegin, true);
    }
}
