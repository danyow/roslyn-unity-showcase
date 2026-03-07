using System;

namespace Showcase.Core;

/// <summary>
/// 标注在 partial class 上，由 AutoImplementGenerator 自动生成所有接口中未实现的属性。
/// 扫描类实现的所有接口，为缺失的属性生成 { get; set; }。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoImplementAttribute : Attribute
{
}
