using System;

namespace Showcase.Core;

/// <summary>
/// 标注在类上，由 AutoServiceGenerator 将其注册到全局 Service 单例类。
/// 生成的属性名默认为类名，可通过 FieldName 自定义。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoServiceAttribute : Attribute
{
    /// <summary>
    /// 自定义在 Service 类中暴露的属性名，不指定则使用类名。
    /// </summary>
    public string? FieldName { get; set; }
}
