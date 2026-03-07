using System;

namespace Showcase.Core.ModuleMesh;

/// <summary>
/// Level 7 — 标注在方法上，表示此方法是一个可被外部调用的工具。
/// SchemaGenerator 提取 InputType/OutputType，ModuleCodeGenerator 生成调用胶水代码。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolAttribute : Attribute
{
    public string Name { get; }
    public Type InputType { get; }
    public Type OutputType { get; }

    public ToolAttribute(string name, Type inputType, Type outputType)
    {
        Name = name;
        InputType = inputType;
        OutputType = outputType;
    }
}
