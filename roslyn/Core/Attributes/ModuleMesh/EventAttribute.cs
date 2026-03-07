using System;

namespace Showcase.Core.ModuleMesh;

/// <summary>
/// Level 7 — 标注在方法上，表示此方法是一个事件处理器。
/// SchemaGenerator 提取信息，ModuleCodeGenerator 生成注册胶水代码。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EventAttribute : Attribute
{
    public string Name { get; }
    public Type DataType { get; }

    public EventAttribute(string name, Type dataType)
    {
        Name = name;
        DataType = dataType;
    }
}
