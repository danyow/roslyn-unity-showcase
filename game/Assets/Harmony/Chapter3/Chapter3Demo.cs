using HarmonyLib;
using UnityEngine;

namespace Showcase.HarmonyDemo.Chapter3
{
    /// <summary>
    /// Chapter 3 — __result 修改返回值
    /// 通过 Postfix 中的 ref __result 修改方法返回值
    /// 演示"值注入"：不改源码，让属性返回测试数据
    /// </summary>
    public class Chapter3Target
    {
        // 假设这是一个无法修改的第三方类的属性
        public int GetPlayerLevel() => 1; // 原始实现始终返回 1
    }

    [HarmonyPatch(typeof(Chapter3Target), nameof(Chapter3Target.GetPlayerLevel))]
    public static class Chapter3Patch
    {
        // ref __result 允许修改返回值
        // Postfix 在原方法执行后运行，__result 已包含原始返回值
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            // 将 Level 1 注入为 Level 99（测试用）
            Debug.Log($"[Chapter3] 原始 Level: {__result}");
            __result = 99;
            Debug.Log($"[Chapter3] 注入后 Level: {__result}");
        }
    }

    public class Chapter3Demo : MonoBehaviour
    {
        private void Start()
        {
            var harmony = new HarmonyLib.Harmony("showcase.chapter3");
            harmony.PatchAll(typeof(Chapter3Patch).Assembly);

            var target = new Chapter3Target();
            var level = target.GetPlayerLevel(); // 返回 99（被 Postfix 修改）
            Debug.Log($"GetPlayerLevel() = {level}");
            Debug.Log("✓ Chapter 3 通关：__result 返回值修改正常");
        }
    }
}
