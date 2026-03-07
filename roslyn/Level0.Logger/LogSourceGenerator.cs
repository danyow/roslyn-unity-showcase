using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGenerator.Infrastructure;

namespace Showcase.Generator.Level0;

/// <summary>
/// Level 0 — Logger 生成器
///
/// 引入 API：
///   - IIncrementalGenerator           生成器完整骨架
///   - CreateSyntaxProvider            语法过滤 + 转换
///   - SemanticModel.GetSymbolInfo     语义验证特性全名（防止名字冲突）
///   - ContainingType.ToDisplayString  获取特性的完全限定名
///   - AnalyzerConfigOptionsProvider   读取 MSBuild 属性（双模式切换）
///
/// 生成内容：
///   [Logger] partial class Foo → 生成内嵌 private static class Debug { Log/LogWarning/LogError/LogException }
///   [Logger(true)] partial class Bar → 生成内嵌 private class DebugContext（适合 MonoBehaviour）
///
/// 双模式：
///   Debug  → AddSource（内存注入，适合开发）
///   Release → WriteFile（物理文件，适合 Unity）
/// </summary>
[Generator]
public class LogSourceGenerator : IIncrementalGenerator
{
    // 完全限定名，防止与用户代码中同名特性冲突
    private const string AttributeFullName = "Showcase.Core.LoggerAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 读取 MSBuild 配置：GenerateMode（AddSource / WriteFile）
        var configProvider = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.GenerateMode", out var mode);
                return GeneratorConfig.ParseMode(mode);
            }
        );

        // 第一步：用 CreateSyntaxProvider 过滤语法节点
        //   predicate: 初筛——只看 class/struct 声明（性能优先）
        //   transform: 细查——验证语义，确认是否带有目标特性
        var providers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => GetTypeForSourceGen(ctx)
            )
            .Where(static t => t.found)
            .Select(static (t, _) => t);

        // 第二步：合并编译对象和配置，在 RegisterSourceOutput 中生成代码
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(providers.Collect()).Combine(configProvider),
            static (ctx, t) => GenerateCode(ctx, t.Left.Left, t.Left.Right, t.Right)
        );
    }

    /// <summary>
    /// 语义过滤：通过 SemanticModel 验证特性全名，避免同名特性产生误匹配。
    /// </summary>
    private static (TypeDeclarationSyntax syntax, bool found, bool contextMode) GetTypeForSourceGen(
        GeneratorSyntaxContext context)
    {
        var syntax = (TypeDeclarationSyntax)context.Node;

        foreach (var attrList in syntax.AttributeLists)
        foreach (var attr in attrList.Attributes)
        {
            // GetSymbolInfo 借助语义模型解析特性构造函数的符号
            if (context.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                continue;

            // ContainingType.ToDisplayString() 返回特性类的完全限定名
            if (attrSymbol.ContainingType.ToDisplayString() != AttributeFullName)
                continue;

            // 检查第一个参数：[Logger(true)] 启用 contextMode
            var contextMode = attr.ArgumentList?.Arguments.Count > 0
                && attr.ArgumentList.Arguments[0].ToString().Contains("true");

            return (syntax, true, contextMode);
        }

        return (syntax, false, false);
    }

    private static void GenerateCode(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<(TypeDeclarationSyntax syntax, bool found, bool contextMode)> items,
        GenerateMode mode)
    {
        foreach (var (syntax, _, contextMode) in items)
        {
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol classSymbol)
                continue;

            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : classSymbol.ContainingNamespace.ToDisplayString();
            var targetName = syntax.Identifier.Text;
            var typeName = syntax is ClassDeclarationSyntax ? "class" : "struct";

            var source = contextMode
                ? BuildContextModeSource(namespaceName, typeName, targetName)
                : BuildStaticModeSource(namespaceName, typeName, targetName);

            var fileName = $"{targetName}.Logger.g.cs";

            if (mode == GenerateMode.WriteFile)
            {
                FileWriter.WriteToPhysicalFile(context, syntax.SyntaxTree.FilePath, fileName, source, "LOG");
            }
            else
            {
                context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    /// <summary>
    /// 静态模式：生成 private static class Debug（适合非 MonoBehaviour 类）
    /// </summary>
    private static string BuildStaticModeSource(string ns, string typeName, string targetName)
    {
        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns}\n{{";
        var nsClose = string.IsNullOrEmpty(ns) ? "" : "}";
        var indent = string.IsNullOrEmpty(ns) ? "" : "    ";

        return $$"""
// <auto-generated/>
// Generator: LogSourceGenerator (Level 0)
{{nsOpen}}
{{indent}}public partial {{typeName}} {{targetName}}
{{indent}}{
{{indent}}    private static class Debug
{{indent}}    {
{{indent}}        private const string Tag = "{{targetName}}";

{{indent}}        [System.Diagnostics.Conditional("UNITY_EDITOR")]
{{indent}}        public static void Log(object msg) =>
{{indent}}            UnityEngine.Debug.Log($"[{Tag}] {msg}");

{{indent}}        public static void LogWarning(object msg) =>
{{indent}}            UnityEngine.Debug.LogWarning($"[{Tag}] {msg}");

{{indent}}        public static void LogError(object msg) =>
{{indent}}            UnityEngine.Debug.LogError($"[{Tag}] {msg}");

{{indent}}        public static void LogException(System.Exception ex) =>
{{indent}}            UnityEngine.Debug.LogException(ex);
{{indent}}    }
{{indent}}}
{{nsClose}}
""";
    }

    /// <summary>
    /// Context 模式：生成 private class DebugContext（适合 MonoBehaviour，可绑定 GameObject）
    /// </summary>
    private static string BuildContextModeSource(string ns, string typeName, string targetName)
    {
        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns}\n{{";
        var nsClose = string.IsNullOrEmpty(ns) ? "" : "}";
        var indent = string.IsNullOrEmpty(ns) ? "" : "    ";

        return $$"""
// <auto-generated/>
// Generator: LogSourceGenerator (Level 0)
{{nsOpen}}
{{indent}}public partial {{typeName}} {{targetName}}
{{indent}}{
{{indent}}    private sealed class DebugContext
{{indent}}    {
{{indent}}        private readonly string _tag;
{{indent}}        public DebugContext(string tag) => _tag = tag;

{{indent}}        public void Log(object msg) =>
{{indent}}            UnityEngine.Debug.Log($"[{_tag}] {msg}");

{{indent}}        public void LogWarning(object msg) =>
{{indent}}            UnityEngine.Debug.LogWarning($"[{_tag}] {msg}");

{{indent}}        public void LogError(object msg) =>
{{indent}}            UnityEngine.Debug.LogError($"[{_tag}] {msg}");

{{indent}}        public void LogException(System.Exception ex) =>
{{indent}}            UnityEngine.Debug.LogException(ex);
{{indent}}    }

{{indent}}    private DebugContext? _debug;
{{indent}}    // 懒初始化，首次访问时传入类名作为日志 Tag
{{indent}}    private DebugContext Debug => _debug ??= new DebugContext("{{targetName}}");
{{indent}}}
{{nsClose}}
""";
    }
}
