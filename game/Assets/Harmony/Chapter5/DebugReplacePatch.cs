using HarmonyLib;
using UnityEngine;

namespace Showcase.HarmonyDemo.Chapter5
{
    /// <summary>
    /// 为 Debug.Log / LogWarning / LogError 添加带颜色的前缀。
    /// 使用手动 Patch 而非 [HarmonyPatch] Attribute，精确匹配每个重载。
    /// 参考: dnyw/game/Assets/Scripts/9/Injection/DebugReplacePatch.cs
    /// </summary>
    public static class DebugReplacePatch
    {
        // -------- Debug.Log → 绿色 D --------
        private static class LogPatch
        {
            public static bool Prefix(ref object message)
            {
                message = $"{Debugger.GreenLog("D")} {message}";
                return true;
            }

            public static bool PrefixContext(ref object message, Object context)
            {
                message = $"{Debugger.GreenLog("D")} {message}";
                return true;
            }
        }

        // -------- Debug.LogWarning → 黄色 W --------
        private static class WarningPatch
        {
            public static bool Prefix(ref object message)
            {
                message = $"{Debugger.YellowLog("W")} {message}";
                return true;
            }

            public static bool PrefixContext(ref object message, Object context)
            {
                message = $"{Debugger.YellowLog("W")} {message}";
                return true;
            }
        }

        // -------- Debug.LogError → 红色 E --------
        private static class ErrorPatch
        {
            public static bool Prefix(ref object message)
            {
                message = $"{Debugger.RedLog("E")} {message}";
                return true;
            }

            public static bool PrefixContext(ref object message, Object context)
            {
                message = $"{Debugger.RedLog("E")} {message}";
                return true;
            }
        }

        /// <summary>
        /// 手动为 Debug.Log/LogWarning/LogError 的两个重载（共 6 个方法）注册 Prefix。
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            // Debug.Log(object)
            harmony.Patch(
                AccessTools.Method(typeof(Debug), nameof(Debug.Log),
                    new[] { typeof(object) }),
                new HarmonyMethod(AccessTools.Method(
                    typeof(LogPatch), nameof(LogPatch.Prefix)))
            );
            // Debug.Log(object, Object)
            harmony.Patch(
                AccessTools.Method(typeof(Debug), nameof(Debug.Log),
                    new[] { typeof(object), typeof(Object) }),
                new HarmonyMethod(AccessTools.Method(
                    typeof(LogPatch), nameof(LogPatch.PrefixContext)))
            );

            // Debug.LogWarning(object)
            harmony.Patch(
                AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning),
                    new[] { typeof(object) }),
                new HarmonyMethod(AccessTools.Method(
                    typeof(WarningPatch), nameof(WarningPatch.Prefix)))
            );
            // Debug.LogWarning(object, Object)
            harmony.Patch(
                AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning),
                    new[] { typeof(object), typeof(Object) }),
                new HarmonyMethod(AccessTools.Method(
                    typeof(WarningPatch), nameof(WarningPatch.PrefixContext)))
            );

            // Debug.LogError(object)
            harmony.Patch(
                AccessTools.Method(typeof(Debug), nameof(Debug.LogError),
                    new[] { typeof(object) }),
                new HarmonyMethod(AccessTools.Method(
                    typeof(ErrorPatch), nameof(ErrorPatch.Prefix)))
            );
            // Debug.LogError(object, Object)
            harmony.Patch(
                AccessTools.Method(typeof(Debug), nameof(Debug.LogError),
                    new[] { typeof(object), typeof(Object) }),
                new HarmonyMethod(AccessTools.Method(
                    typeof(ErrorPatch), nameof(ErrorPatch.PrefixContext)))
            );
        }
    }
}
