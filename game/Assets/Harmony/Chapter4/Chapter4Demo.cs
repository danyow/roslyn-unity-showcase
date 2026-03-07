using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Showcase.HarmonyDemo.Chapter4
{
    /// <summary>
    /// 自定义 Attribute：用于标记需要自动打补丁的方法
    /// 参考: dnyw/game/Assets/Scripts/9/Injection/StackTracePatch.cs 的反射扫描模式
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AutoPatchAttribute : Attribute
    {
        public string Description { get; }
        public AutoPatchAttribute(string description) => Description = description;
    }

    // 动态扫描的目标类（可以在任何地方定义 [AutoPatch] 方法）
    public class Chapter4Target
    {
        [AutoPatch("玩家攻击方法")]
        public void Attack() => Debug.Log("[Chapter4Target] 执行攻击");

        [AutoPatch("玩家防御方法")]
        public void Defend() => Debug.Log("[Chapter4Target] 执行防御");

        public void NormalMethod() => Debug.Log("[Chapter4Target] 普通方法（不会被自动补丁）");
    }

    /// <summary>
    /// Chapter 4 — 动态扫描 + 自定义 Attribute
    /// 启动时扫描程序集，对所有 [AutoPatch] 方法批量打补丁
    /// 参考 StackTracePatch.ApplyPatches() 的反射扫描模式
    /// </summary>
    public class Chapter4Demo : MonoBehaviour
    {
        private HarmonyLib.Harmony _harmony;

        private void Start()
        {
            _harmony = new HarmonyLib.Harmony("showcase.chapter4");
            ApplyPatches();

            var target = new Chapter4Target();
            target.Attack();   // 会被自动拦截
            target.Defend();   // 会被自动拦截
            target.NormalMethod(); // 不受影响

            Debug.Log("✓ Chapter 4 通关：动态扫描 + 自定义 Attribute 批量打补丁正常");
        }

        private void ApplyPatches()
        {
            // 扫描 Assembly-CSharp 中所有带 [AutoPatch] 的方法
            // 参考 StackTracePatch 的反射扫描模式
            var allMethods = AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp")
                .SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<AutoPatchAttribute>() != null);

            var prefix = new HarmonyMethod(typeof(AutoPatchHelper)
                .GetMethod(nameof(AutoPatchHelper.Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic));

            var postfix = new HarmonyMethod(typeof(AutoPatchHelper)
                .GetMethod(nameof(AutoPatchHelper.Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic));

            foreach (var method in allMethods)
            {
                var attr = method.GetCustomAttribute<AutoPatchAttribute>()!;
                Debug.Log($"[Chapter4] 自动打补丁: {method.DeclaringType?.Name}.{method.Name} ({attr.Description})");
                _harmony.Patch(method, prefix: prefix, postfix: postfix);
            }
        }

        private void OnDestroy() => _harmony.UnpatchSelf();
    }

    internal static class AutoPatchHelper
    {
        internal static bool Prefix(MethodBase __originalMethod)
        {
            var attr = __originalMethod.GetCustomAttribute<AutoPatchAttribute>();
            Debug.Log($"[AutoPatch] → 进入 {__originalMethod.Name} ({attr?.Description})");
            return true;
        }

        internal static void Postfix(MethodBase __originalMethod)
        {
            Debug.Log($"[AutoPatch] ← 退出 {__originalMethod.Name}");
        }
    }
}
