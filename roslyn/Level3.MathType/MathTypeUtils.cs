using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Showcase.Generator.Level3;

/// <summary>
/// MathType 生成器共用工具类
/// </summary>
internal static class MathTypeUtils
{
    /// <summary>从类型名中提取数字（如 Vector3 → 3, float4 → 4）</summary>
    public static readonly Regex NumberRegex = new("[0-9]+", RegexOptions.Compiled);

    /// <summary>轴索引 → 轴名映射</summary>
    public static readonly Dictionary<int, (string low, string up)> Axis = new()
    {
        { 0, ("x", "X") },
        { 1, ("y", "Y") },
        { 2, ("z", "Z") },
        { 3, ("w", "W") },
    };

    /// <summary>
    /// 生成所有 C(max, count) 组合（位掩码法）
    /// </summary>
    public static List<List<int>> GenerateCombinations(int max, int count)
    {
        var combinations = new List<List<int>>();

        for (var mask = 0; mask < 1 << max; mask++)
        {
            if (CountSetBits(mask) != count) continue;

            var combination = new List<int>();
            for (var i = 0; i < max; i++)
            {
                if ((mask & 1 << i) != 0)
                    combination.Add(i);
            }
            combinations.Add(combination);
        }

        return combinations;
    }

    private static int CountSetBits(int n)
    {
        var count = 0;
        while (n > 0)
        {
            count += n & 1;
            n >>= 1;
        }
        return count;
    }

    public static bool NamespaceValid(string ns)
    {
        return !string.IsNullOrEmpty(ns) && ns != "<global namespace>";
    }

    public static string GetTypeNamespace(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
            return namedType.ContainingNamespace.ToDisplayString();
        return string.Empty;
    }

    /// <summary>
    /// 带自动缩进的 AppendLine
    /// </summary>
    public static void AppendIndent(this StringBuilder sb, string line)
    {
        sb.AppendLine(line);
    }
}
