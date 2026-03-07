using System;

namespace Showcase.Core;

/// <summary>
/// 标注在抽象基类上，由 EventExecuteGenerator 自动生成：
///   1. 基类的抽象 Execute 方法声明
///   2. 子类的 Execute switch 分发实现
///   3. Config 类的方法桩
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoEventExecuteAttribute : Attribute
{
    /// <summary>JSON 中标识事件类型的字段名（默认 "$type"）</summary>
    public string? TypeField { get; set; }

    /// <summary>JSON 中标识方法名的字段名（默认 "method"）</summary>
    public string? MethodField { get; set; }
}
