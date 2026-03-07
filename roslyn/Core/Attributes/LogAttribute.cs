using System;

namespace Showcase.Core;

/// <summary>
/// 标注此特性的 partial class/struct 将由 LogSourceGenerator 生成内嵌的静态 Debug 类。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class LoggerAttribute : Attribute
{
    /// <summary>
    /// 为 true 时生成实例 DebugContext（适合 MonoBehaviour），否则生成静态 Debug 类。
    /// </summary>
    public bool ContextMode { get; }

    public LoggerAttribute(bool contextMode = false) => ContextMode = contextMode;
}
