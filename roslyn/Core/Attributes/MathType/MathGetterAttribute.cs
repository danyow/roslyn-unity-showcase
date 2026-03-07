using System;

namespace Showcase.Core.MathType;

/// <summary>
/// 标注在 partial static class 上，自动生成向量分量提取扩展方法。
/// 如 GetXY()、GetXZ() 等命名轴组合方法。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MathGetterAttribute : Attribute
{
    /// <summary>目标类型（如 Vector3, float4）</summary>
    public Type Target { get; }

    /// <summary>轴数量</summary>
    public int Axis { get; }

    public MathGetterAttribute(Type target, int axis = 0)
    {
        Target = target;
        Axis = axis;
    }
}
