using Showcase.Core;
using UnityEngine;

namespace Showcase.Level0
{
    // [Logger] 特性由 LogSourceGenerator 处理
    // 生成器会在编译时注入内嵌的 private static class Debug
    [Logger]
    public partial class Level0Demo : MonoBehaviour
    {
        private void Start()
        {
            // 这个 Debug 是生成的内嵌类，带有 "Level0Demo" 前缀标签
            // 实际调用路径：Debug.Log → UnityEngine.Debug.Log($"[Level0Demo] {msg}")
            Debug.Log("Level 0 Logger 演示启动！");
            Debug.LogWarning("这是一条警告");
            Debug.LogError("这是一条错误");

            UnityEngine.Debug.Log("✓ Level 0 通关：LogSourceGenerator 运行正常");
        }
    }
}
