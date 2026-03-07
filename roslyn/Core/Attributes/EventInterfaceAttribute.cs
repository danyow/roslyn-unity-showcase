using System;

namespace Showcase.Core;

/// <summary>
/// 标注在接口上，由 EventInterfaceGenerator 生成：
/// 1. 每个方法对应的 int 常量 ID（FNV-1a 哈希，取负数域）
/// 2. 事件分发实现类
/// 3. Helper 辅助类
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class EventInterfaceAttribute : Attribute { }
