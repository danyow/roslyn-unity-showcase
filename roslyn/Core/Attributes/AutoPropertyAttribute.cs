using System;

namespace Showcase.Core;

/// <summary>
/// 标注在字段上，由 AutoPropertyGenerator 自动生成对应的公共属性。
/// 字段命名遵循 _camelCase 约定，属性名为 PascalCase。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AutoPropertyAttribute : Attribute { }
