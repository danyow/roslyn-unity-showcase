using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Showcase.HarmonyDemo.Chapter2
{
    /// <summary>
    /// Chapter 2 — __state 状态传递
    /// Prefix 记录开始时间，Postfix 计算耗时
    /// 关键：__state 类型必须在 Prefix 和 Postfix 签名中完全一致
    /// </summary>
    public class Chapter2Target : MonoBehaviour
    {
        public void HeavyWork()
        {
            // 模拟耗时操作
            var sum = 0L;
            for (var i = 0; i < 100000; i++) sum += i;
        }
    }

    [HarmonyPatch(typeof(Chapter2Target), nameof(Chapter2Target.HeavyWork))]
    public static class Chapter2Patch
    {
        // __state 类型：Stopwatch（Prefix 和 Postfix 必须使用同一类型）
        [HarmonyPrefix]
        public static void Prefix(ref Stopwatch __state)
        {
            __state = Stopwatch.StartNew();
            Debug.Log("[Chapter2] 开始计时...");
        }

        [HarmonyPostfix]
        public static void Postfix(ref Stopwatch __state)
        {
            __state.Stop();
            Debug.Log($"[Chapter2] HeavyWork 耗时: {__state.ElapsedMilliseconds}ms");
        }
    }

    public class Chapter2Demo : MonoBehaviour
    {
        private void Start()
        {
            var harmony = new HarmonyLib.Harmony("showcase.chapter2");
            harmony.PatchAll(typeof(Chapter2Patch).Assembly);

            var target = gameObject.AddComponent<Chapter2Target>();
            target.HeavyWork(); // 触发 Prefix + Postfix

            Debug.Log("✓ Chapter 2 通关：__state 状态传递正常");
        }
    }
}
