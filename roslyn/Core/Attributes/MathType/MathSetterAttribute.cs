using System;

namespace Showcase.Core.MathType;

/// <summary>
/// 标注在 partial static class 上，自动生成向量 Set/Add/Sub/Mul/Div/Mod 扩展方法。
/// 支持按轴索引和命名轴（X/Y/Z/W）两种调用方式。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MathSetterAttribute : Attribute
{
    /// <summary>目标类型（如 Vector3, float4）</summary>
    public Type Target { get; }

    /// <summary>内部分量类型（如 float, int）</summary>
    public Type Inner { get; }

    /// <summary>轴数量（默认从类型名中的数字推断）</summary>
    public int Axis { get; }

    /// <summary>是否生成降维重载（如 int3 → 接受 int2 参数）</summary>
    public bool Reduction { get; }

    public MathSetterAttribute(Type target, Type inner, int axis = 0, bool reduction = true)
    {
        Target = target;
        Inner = inner;
        Axis = axis;
        Reduction = reduction;
    }
}
