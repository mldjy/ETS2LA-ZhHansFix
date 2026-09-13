// BlazorPatcher — 第五层补丁：拦截 Blazor 渲染树里的字面量文案。
//
// 背景：ETS2LA 的设置页是 Blazor 组件（Photino.Blazor 把 HTML 渲染进 WebView），
// 组件参数里的字面量既不过 ETS2LA.Translations.T._()，也不是 ImGui 调用参数，例如
//     <Dropdown Title="Update Channel"
//               Description="Select the update channel you want to listen to. ..." />
// 同一个页面里用 @_("Current Version: {0}", ...) 写的文案会被第一层译掉，
// 而上面这类字面量会漏掉——正是本层要补的。
//
// razor 编译后的组件把字面量交给 Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder：
//   文本节点  -> AddContent(int sequence, string textContent)
//   属性值    -> AddAttribute(int sequence, string name, string value)
//   标记内容  -> AddMarkupContent(int sequence, string markupContent)
// 在这里按同一本词典替换，即可覆盖所有 razor 页面里的固定文案（标题、说明、选项文字）。
//
// 只替换词典里精确命中的字符串，CSS 类名、事件处理器、内部 ID 等一律原样保留。
// 本文件不引用任何 AspNetCore 类型（补丁方法只用 ref string / ref object），因此无需编译期依赖。

using System.Reflection;
using HarmonyLib;

namespace ZhHansFix.Core;

internal static class BlazorPatcher
{
    internal static int Applied;

    public static int Apply(Harmony harmony)
    {
        var t = AccessTools.TypeByName("Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder");
        if (t == null) return 0;

        int patched = 0;
        patched += Patch(harmony, t, "AddAttribute", new[] { typeof(int), typeof(string), typeof(string) },
                         nameof(BlazorPatches.PrefixAttributeValue));
        patched += Patch(harmony, t, "AddAttribute", new[] { typeof(int), typeof(string), typeof(object) },
                         nameof(BlazorPatches.PrefixAttributeObject));
        patched += Patch(harmony, t, "AddContent", new[] { typeof(int), typeof(string) },
                         nameof(BlazorPatches.PrefixContentText));
        patched += Patch(harmony, t, "AddContent", new[] { typeof(int), typeof(object) },
                         nameof(BlazorPatches.PrefixContentObject));
        patched += Patch(harmony, t, "AddMarkupContent", new[] { typeof(int), typeof(string) },
                         nameof(BlazorPatches.PrefixMarkupContent));

        Applied = patched;
        return patched;
    }

    private static int Patch(Harmony harmony, Type t, string name, Type[] signature, string patchName)
    {
        try
        {
            var m = AccessTools.Method(t, name, signature);
            if (m == null) return 0;
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(BlazorPatches), patchName));
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>词典命中就替换；未命中且看起来像界面文案时记入缺失清单。</summary>
    internal static void Translate(ref string value, bool harvest)
    {
        try
        {
            if (!Patcher.IsSimplifiedChinese()) return;
            if (string.IsNullOrEmpty(value)) return;

            var raw = value;
            if (Patcher.TryTranslate(raw, out var hit))
            {
                value = hit;
                Patcher.OverridesServed++;
                Patcher.RecordServed(raw);
                return;
            }
            if (Patcher.TryTranslatePrefixed(raw, out var prefixed))
            {
                value = prefixed;
                Patcher.OverridesServed++;
                return;
            }
            if (harvest && LooksLikeUiText(raw)) Patcher.RecordMissing(raw);
        }
        catch { }
    }

    /// <summary>过滤掉 CSS 类名、URL、代码片段等，只留下可能是界面文案的字符串。</summary>
    private static bool LooksLikeUiText(string s)
    {
        if (s.Length < 4 || s.Length > 400) return false;
        if (s.Contains('%')) return false;                       // 格式串另有人管
        if (s.Contains('\n') || s.Contains('\r')) return false;
        if (s.StartsWith('#') || s.StartsWith('.')) return false;
        if (Patcher.HasCjk(s)) return false;                     // 已是中文
        if (s.Contains("://") || s.Contains('{') || s.Contains('}')) return false;
        if (s.Contains("flex ") || s.Contains("text-") || s.Contains("w-full") ||
            s.Contains("gap-") || s.Contains("m-") || s.Contains("p-")) return false;   // Tailwind 类名
        if (s.Count(c => c == ' ') < 1) return false;            // 单个词多半是 id 或类名
        return true;
    }
}

public static class BlazorPatches
{
    // AddAttribute(int sequence, string name, string value)
    public static void PrefixAttributeValue(ref string value) => BlazorPatcher.Translate(ref value, true);

    // AddAttribute(int sequence, string name, object value)
    public static void PrefixAttributeObject(ref object value)
    {
        if (value is string s)
        {
            BlazorPatcher.Translate(ref s, true);
            value = s;
        }
    }

    // AddContent(int sequence, string textContent)
    public static void PrefixContentText(ref string textContent) => BlazorPatcher.Translate(ref textContent, true);

    // AddContent(int sequence, object content)
    public static void PrefixContentObject(ref object textContent)
    {
        if (textContent is string s)
        {
            BlazorPatcher.Translate(ref s, true);
            textContent = s;
        }
    }

    // AddMarkupContent(int sequence, string markupContent)
    public static void PrefixMarkupContent(ref string markupContent) => BlazorPatcher.Translate(ref markupContent, false);
}
