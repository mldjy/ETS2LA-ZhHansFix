// ZhHansFix.Core — 简体中文补全的实际补丁实现。
//
// ETS2LA 的全部界面文本（程序自带页面、以及各插件的页面）都经过同一个入口：
//     ETS2LA.Translations.T._(string name, params object[] arguments)
//     ETS2LA.Translations.T._n(string singular, string plural, int count, params object[] arguments)
// 二者最终查 OrchardCore 的 PO 本地化目录（随程序分发的 .po/.mo）。
//
// 插件侧字符串从未进入该目录（插件 DLL 不含任何翻译资源），程序侧也有少量串未被提取，
// 因此这些文本恒定回落到英文原文。本程序集做两件事：
//   ① prefix  —— 若当前界面语言为简体中文，且我们的词典里有该原文，直接返回词典译文；
//   ② postfix —— 若本地化结果仍等于原文（即未翻译），把原文记进缺失清单文件，
//               用于持续扩充词典（这就是权威的缺口列表，无需靠猜）。
//
// 词典为外置 JSON（%APPDATA%\ETS2LA\zh-hans-fix.json），支持热加载，程序更新不会丢失。
// 本程序集不编译期引用 ETS2LA 的任何类型，全部走反射，避免被解析进可回收上下文。

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;

namespace ZhHansFix.Core;

public static class Patcher
{
    private const string HarmonyId = "mldjy.ets2la.zhhansfix";

    /// <summary>%APPDATA%\ETS2LA\zh-hans-fix.json — 可随时编辑，程序运行中会自动重载。</summary>
    public static string DictionaryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ETS2LA", "zh-hans-fix.json");

    /// <summary>%APPDATA%\ETS2LA\zh-hans-missing.txt — 收集到的未翻译原文，每行一条。</summary>
    public static string MissingLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ETS2LA", "zh-hans-missing.txt");

    /// <summary>
    /// %APPDATA%\ETS2LA\zh-hans-served.txt — 被词典实际命中过的原文。
    /// 用于区分「词典里的串程序从没查询过（硬编码，插件够不到）」与「只是还没重载」。
    /// </summary>
    public static string ServedLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ETS2LA", "zh-hans-served.txt");

    private static Harmony? _harmony;
    private static readonly Dictionary<string, string> Dict = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Served = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static DateTime _dictStamp = DateTime.MinValue;
    private static DateTime _lastMissFlush = DateTime.MinValue;

    // 统计，供插件写进日志
    internal static int OverridesServed;
    internal static int MissesSeen;
    private static string _status = "not applied";

    /// <summary>补丁常驻，启用/停用只切这个标志。默认关闭：插件被加载但未被启用时不生效。</summary>
    private static volatile bool _enabled = false;

    public static string Apply()
    {
        _enabled = true;
        if (_harmony != null) return "already patched";
        return PatchInternal();
    }

    /// <summary>
    /// 预先挂好补丁但保持不生效（开关默认关闭）。耗时约数秒，放在程序启动阶段吸收，
    /// 这样用户启用/停用时都只是翻转标志位，瞬时完成。
    /// </summary>
    public static string Prepare()
    {
        if (_harmony != null) return "already prepared";
        return PatchInternal();
    }

    private static string PatchInternal()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var t = AccessTools.TypeByName("ETS2LA.Translations.T");
            if (t == null) return "skipped: ETS2LA.Translations.T not found";

            LoadDictionary(force: true);

            _harmony = new Harmony(HarmonyId);
            var notes = new List<string>();

            var m1 = AccessTools.Method(t, "_", new[] { typeof(string), typeof(object[]) });
            if (m1 != null)
            {
                try
                {
                    _harmony.Patch(m1,
                        prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.TranslatePrefix)),
                        postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.TranslatePostfix)));
                    notes.Add("_");
                }
                catch (Exception ex) { notes.Add("_ failed: " + ex.GetBaseException().Message); }
            }
            else notes.Add("_ MISSING");

            var m2 = AccessTools.Method(t, "_n", new[] { typeof(string), typeof(string), typeof(int), typeof(object[]) });
            if (m2 != null)
            {
                try
                {
                    _harmony.Patch(m2,
                        prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.TranslatePluralPrefix)),
                        postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.TranslatePluralPostfix)));
                    notes.Add("_n");
                }
                catch (Exception ex) { notes.Add("_n failed: " + ex.GetBaseException().Message); }
            }
            else notes.Add("_n MISSING");

            // 第二层：ImGui 控件文案（插件设置页等直接画在 ImGui 调用参数里的字符串）
            var imguiPatched = ImGuiPatcher.Apply(_harmony);
            notes.Add($"imgui {imguiPatched}");

            // 第三层：插件元数据（插件管理/插件库卡片的名称与说明，按属性取值入口替换，
            // 不依赖具体渲染方式，元数据此前是否已被缓存都不影响）
            var metaPatched = MetadataPatcher.Apply(_harmony);
            notes.Add($"meta {metaPatched}");

            // 第四层：在线插件目录的数据类型（与上一层的类不同，目录卡片走这条）
            var netMetaPatched = NetworkMetadataPatcher.Apply(_harmony);
            notes.Add($"net {netMetaPatched}");

            // 第五层：Blazor 渲染树（设置页等 razor 页面里的字面量标题/说明/选项，
            // 既不过 T._() 也不经 ImGui，只有在渲染树入口才拦得到）
            var blazorPatched = BlazorPatcher.Apply(_harmony);
            notes.Add($"blazor {blazorPatched}");


            _status = string.Join("+", notes);
            sw.Stop();
            return $"{_status} patched in {sw.ElapsedMilliseconds}ms, dictionary {Dict.Count} entries";
        }
        catch (Exception ex)
        {
            try { _harmony?.UnpatchAll(HarmonyId); } catch { }
            _harmony = null;
            _status = "error";
            return "error: " + ex.GetBaseException().Message;
        }
    }

    public static string Remove()
    {
        // 停用只需翻转标志：所有翻译入口立刻停用（不重新打/撤补丁，因此是瞬时的）。
        // 补丁本身留在进程内不再生效；完全卸载发生在程序退出时。
        _enabled = false;
        _status = "disabled";
        return "disabled (instant)";
    }

    /// <summary>彻底回退所有补丁（仅在需要真正卸载时使用，耗时较长）。</summary>
    public static string RemoveHard()
    {
        try
        {
            _enabled = false;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            _status = "not applied";
            return "unpatched";
        }
        catch (Exception ex) { return "error: " + ex.GetBaseException().Message; }
    }

    public static string Status()
    {
        LoadDictionary(force: false);

        // ImGui 的程序集可能在插件加载时还没就绪，这里补挂一次（幂等）
        // ImGui / ETS2LA.Shared / ETS2LA.Networking 都属于后加载，仅在启用状态下补挂
        if (_enabled && _harmony != null && ImGuiPatcher.Applied == 0)
        {
            try
            {
                var n = ImGuiPatcher.Apply(_harmony);
                if (n > 0) _status += $"+imgui {n}";
            }
            catch { }
        }

        // ETS2LA.Shared 也属于后加载，同样补挂一次
        if (_enabled && _harmony != null && MetadataPatcher.Applied == 0)
        {
            try
            {
                var n = MetadataPatcher.Apply(_harmony);
                if (n > 0) _status += $"+meta {n}";
            }
            catch { }
        }

        // ETS2LA.Networking（在线目录的数据类）同样补挂一次
        if (_enabled && _harmony != null && NetworkMetadataPatcher.Applied == 0)
        {
            try
            {
                var n = NetworkMetadataPatcher.Apply(_harmony);
                if (n > 0) _status += $"+net {n}";
            }
            catch { }
        }

        lock (Gate)
            return $"patches: {(_enabled ? _status : (_harmony == null ? _status : "ready, inactive"))} | dictionary {Dict.Count} entries | "
                 + $"overridden {OverridesServed} lookups | untranslated seen {MissesSeen} "
                 + $"(log: {Missing.Count} unique) | culture {CultureInfo.CurrentUICulture.Name}";
    }

    // ---------- 词典 ----------

    internal static void LoadDictionary(bool force)
    {
        try
        {
            var stamp = File.Exists(DictionaryPath) ? File.GetLastWriteTimeUtc(DictionaryPath) : DateTime.MinValue;
            if (!force && stamp == _dictStamp) return;
            _dictStamp = stamp;

            lock (Gate)
            {
                Dict.Clear();

                // 内置默认词典（编译进程序集）
                try
                {
                    var asm = typeof(Patcher).Assembly;
                    using var s = asm.GetManifestResourceStream("ZhHansFix.Core.dict.zh-hans.json");
                    if (s != null) Merge(JsonSerializer.Deserialize<Dictionary<string, string>>(s));
                }
                catch { }

                // 外置词典（优先，支持热加载）
                if (File.Exists(DictionaryPath))
                {
                    try
                    {
                        var json = File.ReadAllText(DictionaryPath, Encoding.UTF8);
                        Merge(JsonSerializer.Deserialize<Dictionary<string, string>>(json));
                    }
                    catch { }
                }

                RebuildPrefixIndex();
            }
        }
        catch { }
    }

    private static void Merge(Dictionary<string, string>? pairs)
    {
        if (pairs == null) return;
        foreach (var kv in pairs)
        {
            if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
            if (kv.Key.StartsWith('_')) continue;   // 允许 JSON 里写 _comment 之类的说明键
            Dict[kv.Key] = kv.Value;
        }
    }

    /// <summary>以冒号/空格结尾的键（前缀替换用）与以空格开头的键（后缀替换用），都是不可变快照。</summary>
    private static volatile Dictionary<char, string[]> PrefixIndex = new();
    private static volatile Dictionary<char, string[]> SuffixIndex = new();

    private static void RebuildPrefixIndex()
    {
        var tmp = new Dictionary<char, List<string>>();
        var tails = new Dictionary<char, List<string>>();
        lock (Gate)
        {
            foreach (var k in Dict.Keys)
            {
                if (k.Length < 3) continue;
                var last = k[^1];
                var first = k[0];
                // 前缀：键尾是「非字母数字」（冒号、空格、括号、句点、逗号、等号…）
                // 覆盖 "Sorted "、"Speed:"、"…changing to green ("、"Most … in front (" 这类「固定标签 + 动态值」
                if (!char.IsLetterOrDigit(last))
                {
                    if (!tmp.TryGetValue(k[0], out var list))
                    {
                        list = new List<string>();
                        tmp[k[0]] = list;
                    }
                    list.Add(k);
                }
                // 后缀：键首是「非字母数字」，覆盖 " vehicles."、"), no need to slow down." 这类「动态值 + 固定尾巴」
                if (!char.IsLetterOrDigit(first) && k.Length >= 3)
                {
                    if (!tails.TryGetValue(last, out var tlist))
                    {
                        tlist = new List<string>();
                        tails[last] = tlist;
                    }
                    tlist.Add(k);
                }
            }
        }

        var frozen = new Dictionary<char, string[]>(tmp.Count);
        foreach (var kv in tmp) frozen[kv.Key] = kv.Value.ToArray();
        PrefixIndex = frozen;

        var frozenTails = new Dictionary<char, string[]>(tails.Count);
        foreach (var kv in tails) frozenTails[kv.Key] = kv.Value.ToArray();
        SuffixIndex = frozenTails;
    }

    internal static bool TryTranslate(string source, out string translation)
    {
        translation = string.Empty;
        if (!_enabled || string.IsNullOrEmpty(source)) return false;   // 停用后所有入口立即空转
        LoadDictionary(force: false);
        lock (Gate)
        {
            if (Dict.TryGetValue(source, out var t) && !string.IsNullOrEmpty(t))
            {
                translation = t;
                return true;
            }
        }
        // 文案把换行写成了多行、词典里存的是空格版本时也要能命中（插件常见写法）
        if (source.IndexOf('\n') >= 0)
        {
            var flat = source.Replace("\r\n", " ").Replace('\n', ' ');
            lock (Gate)
            {
                if (Dict.TryGetValue(flat, out var ft) && !string.IsNullOrEmpty(ft))
                {
                    translation = ft;
                    return true;
                }
            }
        }

        // 跨行文案：插件常把说明分成多行放进同一个字符串，整串查不到时按行翻译再拼回
        if (source.Contains('\n'))
        {
            var parts = source.Split('\n');
            var any = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var line = parts[i];
                var bare = line.TrimEnd('\r');
                var suffix = line.Substring(bare.Length);
                if (bare.Length == 0) continue;
                lock (Gate)
                {
                    if (Dict.TryGetValue(bare, out var lt) && !string.IsNullOrEmpty(lt))
                    {
                        parts[i] = lt + suffix;
                        any = true;
                    }
                }
            }
            if (any)
            {
                translation = string.Join("\n", parts);
                return true;
            }
        }

        translation = string.Empty;
        return false;
    }
    /// <summary>
    /// 前缀替换：插件常把标签与数值拼成一个字符串（"Speed: " + 39.7），精确匹配抓不到。
    /// 这里按"以冒号结尾的键"做前缀匹配，只替换标签部分，数值原样保留。
    /// </summary>
    internal static bool TryTranslatePrefixed(string source, out string result)
    {
        result = string.Empty;
        if (!_enabled || string.IsNullOrEmpty(source)) return false;
        LoadDictionary(force: false);
        var translated = Segmented(source, 0, out var changed);
        if (!changed) return false;
        result = translated;
        return true;
    }

    /// <summary>
    /// 分段替换：把「固定标签 + 动态值 + 固定尾巴」这类拼装文案逐段译出。
    /// 先试整串，再试最长前缀键，再试最长后缀键，命中后对剩余部分递归（深度上限 4）。
    /// </summary>
    private static string Segmented(string s, int depth, out bool changed)
    {
        changed = false;
        if (depth >= 4 || s.Length < 2) return s;

        // 1) 整串
        lock (Gate)
        {
            if (Dict.TryGetValue(s, out var whole) && !string.IsNullOrEmpty(whole))
            {
                changed = true;
                return whole;
            }
        }

        // 2) 最长前缀键
        string? headKey = null;
        if (PrefixIndex.TryGetValue(s[0], out var heads))
        {
            foreach (var k in heads)
            {
                if (k.Length >= s.Length) continue;
                if (!s.StartsWith(k, StringComparison.Ordinal)) continue;
                if (headKey == null || k.Length > headKey.Length) headKey = k;
            }
        }
        if (headKey != null)
        {
            string headTr;
            lock (Gate)
            {
                if (!Dict.TryGetValue(headKey, out headTr!) || string.IsNullOrEmpty(headTr)) headTr = null!;
            }
            if (headTr != null)
            {
                var rest = Segmented(s[headKey.Length..], depth + 1, out _);
                if (headTr.EndsWith('：') && rest.StartsWith(' ')) rest = rest[1..];
                changed = true;
                return headTr + rest;
            }
        }

        // 3) 最长后缀键
        string? tailKey = null;
        if (s.Length > 2 && SuffixIndex.TryGetValue(s[^1], out var tails))
        {
            foreach (var k in tails)
            {
                if (k.Length >= s.Length) continue;
                if (!s.EndsWith(k, StringComparison.Ordinal)) continue;
                if (tailKey == null || k.Length > tailKey.Length) tailKey = k;
            }
        }
        if (tailKey != null)
        {
            string tailTr;
            lock (Gate)
            {
                if (!Dict.TryGetValue(tailKey, out tailTr!) || string.IsNullOrEmpty(tailTr)) tailTr = null!;
            }
            if (tailTr != null)
            {
                var head = Segmented(s[..^tailKey.Length], depth + 1, out _);
                changed = true;
                return head + tailTr;
            }
        }

        // 4) 中段：前缀键出现在字符串中间（"955.0m away, which is too far to be relevant." 这类「动态值 + 固定文案」）
        string? midKey = null;
        var midIdx = -1;
        foreach (var c in s)
        {
            if (!PrefixIndex.TryGetValue(c, out var mids)) continue;
            foreach (var k in mids)
            {
                if (k.Length >= s.Length) continue;
                var idx = s.IndexOf(k, StringComparison.Ordinal);
                if (idx <= 0) continue;
                if (midKey == null || k.Length > midKey.Length) { midKey = k; midIdx = idx; }
            }
        }
        if (midKey != null)
        {
            string midTr;
            lock (Gate)
            {
                if (!Dict.TryGetValue(midKey, out midTr!) || string.IsNullOrEmpty(midTr)) midTr = null!;
            }
            if (midTr != null)
            {
                var left = s[..midIdx];
                var rest = Segmented(s[(midIdx + midKey.Length)..], depth + 1, out _);
                changed = true;
                return left + midTr + rest;
            }
        }

        return s;
    }

    // ---------- 缺失收割 ----------

    internal static void RecordMissing(string source)
    {
        if (!_enabled) return;
        try
        {
            bool added;
            lock (Gate)
            {
                MissesSeen++;
                added = Missing.Add(source);
                if (added && Missing.Count > 0 && (DateTime.UtcNow - _lastMissFlush).TotalSeconds > 5)
                {
                    _lastMissFlush = DateTime.UtcNow;
                    var dir = Path.GetDirectoryName(MissingLogPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var existing = new HashSet<string>(StringComparer.Ordinal);
                    if (File.Exists(MissingLogPath))
                        foreach (var l in File.ReadAllLines(MissingLogPath)) existing.Add(l);
                    var newOnes = Missing.Where(x => !existing.Contains(x)).ToList();
                    if (newOnes.Count > 0)
                        File.AppendAllLines(MissingLogPath, newOnes, new UTF8Encoding(false));
                }
            }
        }
        catch { }
    }

    /// <summary>记录词典命中的原文（去重后追加），用于判定哪些串程序从不查询翻译入口。</summary>
    internal static void RecordServed(string source)
    {
        if (!_enabled) return;
        try
        {
            lock (Gate)
            {
                if (!Served.Add(source)) return;
                var dir = Path.GetDirectoryName(ServedLogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllLines(ServedLogPath, new[] { source }, new UTF8Encoding(false));
            }
        }
        catch { }
    }

    /// <summary>当前界面语言是否简体中文。</summary>
    internal static bool IsSimplifiedChinese()
    {
        try
        {
            var n = CultureInfo.CurrentUICulture.Name;
            return n.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
                || n.Equals("zh_Hans", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("zh_CN", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static string Format(string source, object[]? args)
    {
        if (args == null || args.Length == 0) return source;
        try { return string.Format(source, args); } catch { return source; }
    }

    internal static bool HasCjk(string s)
    {
        foreach (var c in s)
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)) return true;
        return false;
    }
}

public static class Patches
{
    // ---- T._ ----

    public static bool TranslatePrefix(string name, object[] arguments, ref string __result)
    {
        try
        {
            if (!Patcher.IsSimplifiedChinese()) return true;
            if (string.IsNullOrEmpty(name)) return true;

            if (Patcher.TryTranslate(name, out var translation))
            {
                __result = Patcher.Format(translation, arguments);
                Patcher.OverridesServed++;
                Patcher.RecordServed(name);
                return false;   // 跳过原实现
            }
            else if (arguments is { Length: > 0 } &&
                     Patcher.TryTranslatePrefixed(Patcher.Format(name, arguments), out var prefixed))
            {
                __result = prefixed;
                Patcher.OverridesServed++;
                return false;
            }
        }
        catch { }
        return true;
    }

    // ---- T._n ----

    public static bool TranslatePluralPrefix(string singular, string plural, int count, object[] arguments,
                                             ref string __result)
    {
        try
        {
            if (!Patcher.IsSimplifiedChinese()) return true;
            var key = count == 1 ? singular : plural;
            if (string.IsNullOrEmpty(key)) return true;

            if (Patcher.TryTranslate(key, out var translation))
            {
                __result = Patcher.Format(translation, arguments);
                Patcher.OverridesServed++;
                return false;
            }
        }
        catch { }
        return true;
    }

    // ---- 未翻译检测（T._ 用：参数名为 name） ----

    public static void TranslatePostfix(string name, object[] arguments, ref string __result)
    {
        RecordIfUntranslated(name, arguments, __result);
    }

    // ---- 未翻译检测（T._n 用：参数名为 singular/plural） ----

    public static void TranslatePluralPostfix(string singular, string plural, int count, object[] arguments,
                                              ref string __result)
    {
        RecordIfUntranslated(count == 1 ? singular : plural, arguments, __result);
    }

    private static void RecordIfUntranslated(string key, object[] arguments, string result)
    {
        try
        {
            if (!Patcher.IsSimplifiedChinese()) return;
            if (string.IsNullOrEmpty(key)) return;

            // 本地化器找不到条目时会回落为原文（参数已代入）
            if (result == Patcher.Format(key, arguments) && !Patcher.HasCjk(result))
                Patcher.RecordMissing(key);
        }
        catch { }
    }
}
