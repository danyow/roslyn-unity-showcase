using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGenerator.Infrastructure;

namespace Showcase.Generator.Level3;

/// <summary>
/// Level 3a — MathSetter 生成器
///
/// 引入 API：
///   - TypeOfExpressionSyntax        typeof(T) 参数解析
///   - SemanticModel.GetTypeInfo     从 typeof 获取类型符号
///   - Attribute 多参数提取           target, inner, axis, reduction
///
/// 生成内容：
///   [MathSetter(typeof(Vector3), typeof(float), axis: 3)]
///   static partial class Vector3Ext
///     → SetX/SetXY/SetXYZ, AddX/AddXY, ..., RefSetX/RefSetXY, ... 等扩展方法
///
/// 关键教学：
///   - 位掩码组合生成：C(n, k) 枚举所有轴组合
///   - 方法重载矩阵：6 种运算 × n 种轴组合 × ref/非ref = 大量方法
///   - 降维重载：int3.SetXY(int2 value) → 不同向量维度间的互操作
/// </summary>
[Generator]
public class MathSetterGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Showcase.Core.MathType.MathSetterAttribute";

    private static readonly Dictionary<string, Func<int, string>> ModifyMethods = new()
    {
        { "Set", i => $"target[axis{i}] = value{i};" },
        { "Add", i => $"target[axis{i}] += value{i};" },
        { "Sub", i => $"target[axis{i}] -= value{i};" },
        { "Mul", i => $"target[axis{i}] *= value{i};" },
        { "Div", i => $"target[axis{i}] /= value{i};" },
        { "Mod", i => $"target[axis{i}] %= value{i};" },
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configProvider = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.GenerateMode", out var mode);
                return GeneratorConfig.ParseMode(mode);
            }
        );

        var results = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (t, _) => t is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                transform: static (t, _) => GetClassDeclaration(t)
            )
            .Where(static t => t.Found);

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(results.Collect()).Combine(configProvider),
            static (ctx, t) => GenerateCode(ctx, t.Left.Left, t.Left.Right, t.Right)
        );
    }

    private static SetterInfo GetClassDeclaration(GeneratorSyntaxContext context)
    {
        var syntax = (ClassDeclarationSyntax)context.Node;

        foreach (var attrList in syntax.AttributeLists)
        foreach (var attr in attrList.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                continue;
            if (attrSymbol.ContainingType.ToDisplayString() != AttributeFullName)
                continue;

            var args = attr.ArgumentList?.Arguments;
            if (args == null || args.Value.Count < 2) continue;

            // target: typeof(Vector3)
            var targetType = args.Value[0].Expression is TypeOfExpressionSyntax t0
                ? context.SemanticModel.GetTypeInfo(t0.Type).Type
                : null;

            // inner: typeof(float)
            var innerType = args.Value[1].Expression is TypeOfExpressionSyntax t1
                ? context.SemanticModel.GetTypeInfo(t1.Type).Type
                : null;

            if (targetType == null || innerType == null) continue;

            var targetName = targetType.ToDisplayString();
            var innerName = innerType.ToDisplayString();

            // axis: 从参数或类名数字推断
            var axis = 0;
            if (args.Value.Count > 2 && args.Value[2].Expression is LiteralExpressionSyntax axisLit)
                axis = int.Parse(axisLit.Token.ValueText);
            if (axis == 0)
            {
                var match = MathTypeUtils.NumberRegex.Match(syntax.Identifier.Text);
                if (match.Success) axis = int.Parse(match.Value);
            }
            if (axis == 0) axis = 2;

            // reduction
            var reduction = true;
            if (args.Value.Count > 3 && args.Value[3].Expression is LiteralExpressionSyntax redLit)
                reduction = bool.Parse(redLit.Token.ValueText);

            var namespaces = new List<string>
            {
                MathTypeUtils.GetTypeNamespace(targetType),
                MathTypeUtils.GetTypeNamespace(innerType),
            };

            return new SetterInfo
            {
                Syntax = syntax,
                Found = true,
                Axis = axis,
                Target = targetName,
                Inner = innerName,
                Namespaces = namespaces,
                Reduction = reduction,
            };
        }

        return default;
    }

    private static void GenerateCode(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<SetterInfo> items,
        GenerateMode mode)
    {
        foreach (var info in items)
        {
            var semanticModel = compilation.GetSemanticModel(info.Syntax.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(info.Syntax) is not INamedTypeSymbol symbol)
                continue;

            var namespaceName = symbol.ContainingNamespace.ToDisplayString();
            var staticClass = info.Syntax.Identifier.Text;
            var target = info.Target;
            var inner = info.Inner;
            var axis = info.Axis;
            var reduction = info.Reduction;
            var namespaces = info.Namespaces.Distinct();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generator: MathSetterGenerator (Level 3)");

            foreach (var ns in namespaces)
                sb.AppendIndent($"using {ns};");

            if (MathTypeUtils.NamespaceValid(namespaceName))
            {
                sb.AppendIndent($"namespace {namespaceName}");
                sb.AppendIndent("{");
            }

            sb.AppendIndent($"public static partial class {staticClass}");
            sb.AppendIndent("{");

            for (var i = 0; i < 2; i++) // 0: normal, 1: ref
            {
                foreach (var kv in ModifyMethods)
                {
                    for (var j = 1; j <= axis; j++)
                    {
                        MethodBase(sb, j, target, inner, kv.Key, kv.Value, i == 1);
                        MethodAxis(sb, axis, j, target, inner, kv.Key, reduction, i == 1);
                    }
                }
            }

            sb.AppendIndent("}");

            if (MathTypeUtils.NamespaceValid(namespaceName))
                sb.AppendIndent("}");

            var source = sb.ToString();
            var fileName = $"{staticClass}.Setter.g.cs";

            if (mode == GenerateMode.WriteFile)
            {
                FileWriter.WriteToPhysicalFile(context, info.Syntax.SyntaxTree.FilePath, fileName, source, "MATH");
            }
            else
            {
                context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static void MethodBase(
        StringBuilder sb, int count, string targetName, string innerType,
        string methodName, Func<int, string> modifyMethod, bool isRef)
    {
        var args = "";
        for (var i = 1; i < count + 1; i++)
            args += $", int axis{i}, {innerType} value{i}";

        var refMethod = isRef ? $"Ref{methodName}" : methodName;
        var typeRef = isRef ? $"ref {targetName}" : targetName;
        sb.AppendIndent($"    public static {targetName} {refMethod}(this {typeRef} target{args})");
        sb.AppendIndent("    {");
        for (var i = 1; i < count + 1; i++)
            sb.AppendIndent($"        {modifyMethod(i)}");
        sb.AppendIndent("        return target;");
        sb.AppendIndent("    }");
    }

    private static void MethodAxis(
        StringBuilder sb, int max, int count, string targetName, string innerType,
        string methodName, bool reduction, bool isRef)
    {
        var combinations = MathTypeUtils.GenerateCombinations(max, count);
        foreach (var combination in combinations)
        {
            var args = "";
            var execute = "";
            var up = "";
            var reductionExecute = "";
            foreach (var i in combination)
            {
                up += MathTypeUtils.Axis[i].up;
                args += $", {innerType} {MathTypeUtils.Axis[i].low}";
                execute += $", {i}, {MathTypeUtils.Axis[i].low}";
                reductionExecute += $", {i}, value[{i}]";
            }
            var refMethod = isRef ? $"Ref{methodName}" : methodName;
            var refPrefix = isRef ? "ref " : "";
            sb.AppendIndent(
                $"    public static {targetName} {refMethod}{up}(this {refPrefix}{targetName} target{args}) => {refMethod}({refPrefix}target{execute});");
            if (reduction && count >= 2)
            {
                var reduceTargetName = MathTypeUtils.NumberRegex.Replace(targetName, count.ToString());
                sb.AppendIndent(
                    $"    public static {targetName} {refMethod}{up}(this {refPrefix}{targetName} target, {reduceTargetName} value) => {refMethod}({refPrefix}target{reductionExecute});");
            }
        }
    }

    private struct SetterInfo
    {
        public ClassDeclarationSyntax Syntax;
        public bool Found;
        public int Axis;
        public string Target;
        public string Inner;
        public List<string> Namespaces;
        public bool Reduction;
    }
}
