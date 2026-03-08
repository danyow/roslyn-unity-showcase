using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Showcase.Editor
{
    /// <summary>
    /// 菜单开关：切换 HARMONY_DEBUG_COLOR 宏定义。
    /// 启用后 Chapter5 的 Debug.Log 颜色前缀补丁生效。
    /// </summary>
    public static class HarmonyDebugColorToggle
    {
        private const string Define = "HARMONY_DEBUG_COLOR";
        private const string MenuPath = "Showcase/Harmony Debug Color";

        [MenuItem(MenuPath, priority = 200)]
        private static void Toggle()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = GetDefines(group);

            if (defines.Contains(Define))
                defines.Remove(Define);
            else
                defines.Add(Define);

            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = GetDefines(group);
            Menu.SetChecked(MenuPath, defines.Contains(Define));
            return true;
        }

        private static List<string> GetDefines(BuildTargetGroup group)
        {
            PlayerSettings.GetScriptingDefineSymbolsForGroup(group, out var symbols);
            return symbols.ToList();
        }
    }
}
