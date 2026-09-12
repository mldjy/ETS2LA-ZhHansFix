using System;
using System.Reflection;
using HarmonyLib;

namespace ZhHansFix.Core
{
    /// <summary>
    /// 插件库（在线目录）用的数据类型是 ETS2LA.Networking.Plugins.NetworkPlugin，
    /// 与已安装插件用的 PluginInformation 不是同一个类。这里对它的 Name / Description
    /// 取值入口做同样的替换，使目录卡片的名称与说明也走中文。
    /// </summary>
    internal static class NetworkMetadataPatcher
    {
        internal static int Applied;

        public static int Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ETS2LA.Networking.Plugins.NetworkPlugin");
            if (type == null) return 0;

            var n = 0;
            n += Patch(harmony, type, "get_Name");
            n += Patch(harmony, type, "get_Description");
            Applied = n;
            return n;
        }

        private static int Patch(Harmony harmony, Type type, string getterName)
        {
            try
            {
                var getter = type.GetMethod(getterName, BindingFlags.Public | BindingFlags.Instance);
                if (getter == null || getter.ReturnType != typeof(string)) return 0;

                var postfix = typeof(NetworkMetadataPatcher).GetMethod(nameof(TranslateResult),
                    BindingFlags.Public | BindingFlags.Static);
                if (postfix == null) return 0;

                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch { return 0; }
        }

        public static void TranslateResult(ref string __result)
        {
            try
            {
                var source = __result;
                if (string.IsNullOrEmpty(source) || source.Length < 2) return;
                if (!Patcher.IsSimplifiedChinese()) return;
                if (!Patcher.TryTranslate(source, out var translation)) return;
                if (string.IsNullOrEmpty(translation)) return;

                __result = translation;
                Patcher.OverridesServed++;
                Patcher.RecordServed(source);
            }
            catch { }
        }
    }
}
