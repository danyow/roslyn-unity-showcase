using UnityEngine;

namespace Showcase.HarmonyDemo.Chapter5
{
    /// <summary>
    /// Chapter 5 — 实战：为 Debug.Log 添加带颜色的前缀
    /// 手动 Patch Unity 内置的 Debug.Log/LogWarning/LogError，
    /// 通过 Prefix + ref 参数篡改日志内容，注入颜色前缀标签。
    /// 通过宏 HARMONY_DEBUG_COLOR 控制是否启用，可在菜单 Showcase/Harmony Debug Color 切换。
    /// 参考: dnyw/game/Assets/Scripts/9/Injection/DebugReplacePatch.cs
    /// </summary>
    public class Chapter5Demo : MonoBehaviour
    {
        private HarmonyLib.Harmony _harmony;

        private void Awake()
        {
            _harmony = new HarmonyLib.Harmony("showcase.chapter5");

#if HARMONY_DEBUG_COLOR
            // 手动 Patch，精确匹配 Debug.Log/LogWarning/LogError 的每个重载
            DebugReplacePatch.ApplyPatches(_harmony);

            // 验证效果：以下三行日志会自动带上颜色前缀
            Debug.Log("普通日志 — 绿色 D 前缀");
            Debug.LogWarning("警告日志 — 黄色 W 前缀");
            Debug.LogError("错误日志 — 红色 E 前缀");

            Debug.Log("Chapter 5 通关：Debug.Log 颜色前缀已激活");
#else
            Debug.Log("Chapter 5：HARMONY_DEBUG_COLOR 未启用，请通过菜单 Showcase/Harmony Debug Color 开启");
#endif
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }
}
