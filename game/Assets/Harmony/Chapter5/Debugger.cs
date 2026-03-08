using UnityEngine;

namespace Showcase.HarmonyDemo.Chapter5
{
    /// <summary>
    /// 颜色工具类：为日志文本添加 Unity Rich Text 颜色标签。
    /// 仅在编辑器下生效，Runtime 环境返回原始文本。
    /// 参考: dnyw/game/Assets/Scripts/9/Utils/Debugger.cs
    /// </summary>
    public static class Debugger
    {
        private static readonly string kGreenColor  = ColorUtility.ToHtmlStringRGBA(Color.green);
        private static readonly string kCyanColor   = ColorUtility.ToHtmlStringRGBA(Color.cyan);
        private static readonly string kYellowColor = ColorUtility.ToHtmlStringRGBA(Color.yellow);
        private static readonly string kRedColor    = ColorUtility.ToHtmlStringRGBA(Color.red);

        private static string ColorLog(string str, string color)
        {
#if UNITY_EDITOR
            return $"<color=#{color}>{str}</color>";
#else
            return str;
#endif
        }

        public static string GreenLog(string str)  => ColorLog(str, kGreenColor);
        public static string CyanLog(string str)   => ColorLog(str, kCyanColor);
        public static string YellowLog(string str) => ColorLog(str, kYellowColor);
        public static string RedLog(string str)    => ColorLog(str, kRedColor);
    }
}
