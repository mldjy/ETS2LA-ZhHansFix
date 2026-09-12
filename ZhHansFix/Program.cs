// ZhHansFix — ETS2LA 插件：补全简体中文翻译（程序页面与插件页面）。
//
// 程序自带简中目录有少量空缺，且插件（ACC / Lane Assist / Internal Visualization 等）
// 的界面文本从未进入翻译目录，因此恒定显示英文。程序所有界面文本都经过同一个入口
// ETS2LA.Translations.T._()，本插件接管该入口，用自带词典返回中文译文。
//
// 本插件只负责"装载与开关"，真正的补丁在 ZhHansFix.Core 里，由它显式加载进默认
// （不可回收）AssemblyLoadContext —— 这是 Harmony 能正常生成补丁包装方法的前提
// （ETS2LA 用可回收上下文加载插件）。
//
// 词典与缺失清单：
//   %APPDATA%\ETS2LA\zh-hans-fix.json    —— 译文词典，可随时编辑，运行中自动重载
//   %APPDATA%\ETS2LA\zh-hans-missing.txt —— 收集到的"仍是英文"的原文，用于持续补全

using System.Reflection;
using System.Runtime.Loader;
using ETS2LA.Logging;
using ETS2LA.Shared;

namespace ZhHansFix;

public class ZhHansFixPlugin : Plugin
{
    private static readonly object Gate = new();
    private static Type? _patcher;

    public override PluginInformation Info => new PluginInformation
    {
        Id = "mldjy.zhhansfix",
        Version = "1.0.0",
        Name = "简体中文补全",
        Description = "补全 ETS2LA 及其插件的简体中文翻译（词典可外置编辑）。",
        AuthorName = "迷路的鲸鱼",
        AuthorWebsite = "https://github.com/mldjy/ETS2LA-ZhHansFix",
    };

    public override void Init()
    {
        base.Init();
        // 插件被加载 ≠ 被启用：这里把补丁预先挂好（不生效），并汇报状态。
        // 真正的启用/停用只是翻转标志位，因此是瞬时的。
        Logger.Info("[ZhHansFix] " + InvokeCore("Prepare"));
        StartStatusReports();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        Logger.Info("[ZhHansFix] " + InvokeCore("Apply"));
    }

    public override void OnDisable()
    {
        base.OnDisable();
        Logger.Info("[ZhHansFix] " + InvokeCore("Remove"));
    }

    public override void Shutdown()
    {
        // 停用只需翻转标志位，程序退出时不必逐条回退补丁（那会让退出变慢）。
        Logger.Info("[ZhHansFix] " + InvokeCore("Remove"));
        base.Shutdown();
    }

    /// <summary>定时上报词典条目数与已收割的缺失条目数，便于确认补丁在跑。</summary>
    private static void StartStatusReports()
    {
        var t = new Thread(() =>
        {
            foreach (var delayMs in new[] { 8000, 20000, 45000, 90000 })
            {
                Thread.Sleep(delayMs);
                try { Logger.Info("[ZhHansFix] " + InvokeCore("Status")); } catch { }
            }
        })
        { IsBackground = true, Name = "ZhHansFix.Status" };
        t.Start();
    }

    /// <summary>调用 ZhHansFix.Core.Patcher 上的静态方法（该程序集在默认上下文中）。</summary>
    private static string InvokeCore(string methodName)
    {
        try
        {
            lock (Gate)
            {
                if (_patcher == null)
                {
                    var dir = ResolvePluginDirectory();
                    var core = LoadIntoDefaultContext(Path.Combine(dir, "ZhHansFix.Core.dll"), "ZhHansFix.Core");
                    _patcher = core.GetType("ZhHansFix.Core.Patcher", throwOnError: true)!;
                }
            }

            var method = _patcher!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null) return $"{methodName}: method not found";
            return method.Invoke(null, null) as string ?? $"{methodName}: no result";
        }
        catch (Exception ex)
        {
            return $"{methodName} failed: {ex.GetBaseException().Message}";
        }
    }

    private static string ResolvePluginDirectory()
    {
        var candidates = new List<string>();

        // 首选：实际安装的 Plugins 目录（ETS2LA 会把发现的手动插件影复制到 %TEMP% 再加载，
        // 因此不能用 Assembly.Location 定位）。
        try { candidates.Add(Path.Combine(AppContext.BaseDirectory, "Plugins")); } catch { }

        try
        {
            var here = Path.GetDirectoryName(typeof(ZhHansFixPlugin).Assembly.Location);
            if (!string.IsNullOrEmpty(here)) candidates.Add(here);
        }
        catch { }

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(c, "ZhHansFix.Core.dll")) &&
                    File.Exists(Path.Combine(c, "0Harmony.dll")))
                    return c;
            }
            catch { }
        }

        return candidates.FirstOrDefault() ?? AppContext.BaseDirectory;
    }

    /// <summary>把程序集（以及它依赖的 Harmony）加载进默认/不可回收上下文。</summary>
    private static Assembly LoadIntoDefaultContext(string path, string simpleName)
    {
        var alc = AssemblyLoadContext.Default;

        var existing = alc.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var harmonyPath = Path.Combine(Path.GetDirectoryName(path)!, "0Harmony.dll");
        if (File.Exists(harmonyPath))
        {
            var hasHarmony = alc.Assemblies.Any(a =>
                string.Equals(a.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase));
            if (!hasHarmony) alc.LoadFromAssemblyPath(harmonyPath);
        }

        return alc.LoadFromAssemblyPath(path);
    }
}
