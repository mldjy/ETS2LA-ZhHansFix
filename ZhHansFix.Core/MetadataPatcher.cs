using System;
using System.Reflection;
using HarmonyLib;

namespace ZhHansFix.Core
{
    /// <summary>
    /// 插件元数据（ETS2LA.Shared.PluginInformation）的 Name / Description 取值入口。
    /// 插件管理、插件库的卡片无论用什么方式渲染，最终都要读这两个属性，
    /// 因此在这里替换可以覆盖所有渲染路径（包括元数据被提前缓存的场景）。
    /// </summary>
    internal static class MetadataPatcher
    {
        internal static int Applied;

        public static int Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ETS2LA.Shared.PluginInformation");
            if (type == null) return 0;

            int n = 0;
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

                var postfix = typeof(MetadataPatcher).GetMethod(nameof(TranslateResult),
                    BindingFlags.Public | BindingFlags.Static);
                if (postfix == null) return 0;

                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch { return 0; }
        }

        /// <summary>属性取值后替换为中文；命中则同时记入命中日志。</summary>
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
