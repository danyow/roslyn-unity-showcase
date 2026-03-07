using HarmonyLib;
using UnityEngine;

namespace Showcase.HarmonyDemo.Chapter1
{
    /// <summary>
    /// Chapter 1 — Prefix/Postfix 基础拦截
    /// 拦截 MonoBehaviour.Update，在调用前后注入日志
    /// 参考: dnyw/game/Assets/Scripts/9/Injection/DebugReplacePatch.cs
    /// </summary>
    public class Chapter1Target : MonoBehaviour
    {
        private void Update()
        {
            // 这个 Update 会被 Chapter1Patch 拦截
            // Prefix 在此之前执行，Postfix 在此之后执行
        }
    }

    [HarmonyPatch(typeof(Chapter1Target), "Update")]
    public static class Chapter1Patch
    {
        /// <summary>
        /// Prefix: 在原方法之前执行
        /// return true  → 继续执行原方法
        /// return false → 跳过原方法（用于完全替换）
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix()
        {
            Debug.Log("[Chapter1] Prefix: Update 即将执行");
            return true; // 继续执行原 Update
        }

        /// <summary>
        /// Postfix: 在原方法之后执行（无论 Prefix 返回什么都会执行）
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            Debug.Log("[Chapter1] Postfix: Update 已执行完毕");
        }
    }

    public class Chapter1Demo : MonoBehaviour
    {
        private HarmonyLib.Harmony _harmony;

        private void Awake()
        {
            _harmony = new HarmonyLib.Harmony("showcase.chapter1");
            _harmony.PatchAll(); // 自动扫描当前程序集中的 [HarmonyPatch]
            Debug.Log("✓ Chapter 1 通关：Prefix/Postfix 基础拦截已激活");
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }
}
