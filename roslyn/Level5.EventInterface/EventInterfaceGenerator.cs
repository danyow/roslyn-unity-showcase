using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGenerator.Infrastructure;

namespace Showcase.Generator.Level5;

/// <summary>
/// Level 5 — EventInterface 生成器（接口 → int ID）
///
/// 引入 API：
///   - InterfaceDeclarationSyntax    扫描接口而非类
///   - AnalyzerConfigOptionsProvider 读取 MSBuild 属性（双模式切换）
///
/// 生成内容：
///   [EventInterface]
///   interface IGameEvents { void OnPlayerDied(string playerName); }
///     →
///   public static class GameEvents          // int 常量类（FNV-1a 哈希，负数域）
///   public static class GameEventsDispatcher // 事件分发实现类
///   public static class GameEventsHelper    // 辅助类
///
/// 关键教学：
///   - FNV-1a 哈希算法：稳定的 ID 生成，不会随编译次序变化
///   - 负数域：int 取反作为事件 ID，避免与普通正数 ID 冲突
///   - ID 碰撞检测：两个方法哈希相同时报告 Diagnostic
/// </summary>
[Generator]
public class EventInterfaceGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Showcase.Core.EventInterfaceAttribute";

    // Diagnostic 描述符：ID 碰撞警告
    private static readonly DiagnosticDescriptor IdCollisionDiag = new(
        id: "EIG001",
        title: "事件 ID 碰撞",
        messageFormat: "接口 '{0}' 中方法 '{1}' 和 '{2}' 的 FNV-1a 哈希发生碰撞（ID={3}），请重命名其中一个",
        category: "EventInterfaceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configProvider = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.GenerateMode", out var mode);
                return GeneratorConfig.ParseMode(mode);
            }
        );

        // 关键点：predicate 扫描 InterfaceDeclarationSyntax
        var providers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, _) => GetInterfaceForSourceGen(ctx)
            )
            .Where(static t => t.found)
            .Select(static (t, _) => t);

        context.RegisterSourceOutput(
            providers.Collect().Combine(configProvider),
            static (ctx, t) => GenerateCode(ctx, t.Left, t.Right)
        );
    }

    private static (bool found, INamedTypeSymbol? symbol, string? filePath) GetInterfaceForSourceGen(
        GeneratorSyntaxContext context)
    {
        var syntax = (InterfaceDeclarationSyntax)context.Node;

        foreach (var attrList in syntax.AttributeLists)
        foreach (var attr in attrList.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                continue;

            if (attrSymbol.ContainingType.ToDisplayString() != AttributeFullName)
                continue;

            var symbol = context.SemanticModel.GetDeclaredSymbol(syntax) as INamedTypeSymbol;
            return (true, symbol, syntax.SyntaxTree.FilePath);
        }

        return (false, null, null);
    }

    private static void GenerateCode(
        SourceProductionContext context,
        ImmutableArray<(bool found, INamedTypeSymbol? symbol, string? filePath)> items,
        GenerateMode mode)
    {
        foreach (var (_, interfaceSymbol, filePath) in items)
        {
            if (interfaceSymbol == null) continue;

            var methods = interfaceSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .ToList();

            // 计算每个方法的 FNV-1a 哈希 ID
            var idMap = new System.Collections.Generic.Dictionary<int, string>();
            var entries = new System.Collections.Generic.List<(string methodName, int id)>();
            var hasCollision = false;

            foreach (var method in methods)
            {
                // 使用接口全名 + 方法名作为 ID 的输入，保证稳定性
                var key = $"{interfaceSymbol.ToDisplayString()}.{method.Name}";
                var id = -(int)(Fnv1aHash(key) & 0x7FFFFFFF); // 取负数域

                if (idMap.TryGetValue(id, out var existing))
                {
                    // 报告碰撞诊断
                    context.ReportDiagnostic(Diagnostic.Create(
                        IdCollisionDiag,
                        Location.None,
                        interfaceSymbol.Name, existing, method.Name, id));
                    hasCollision = true;
                }
                else
                {
                    idMap[id] = method.Name;
                    entries.Add((method.Name, id));
                }
            }

            if (hasCollision) continue;

            var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : interfaceSymbol.ContainingNamespace.ToDisplayString();

            // 去掉接口名前缀 "I"
            var baseName = interfaceSymbol.Name.StartsWith("I") && interfaceSymbol.Name.Length > 1
                ? interfaceSymbol.Name.Substring(1)
                : interfaceSymbol.Name;

            var source = BuildSource(ns, interfaceSymbol.ToDisplayString(), baseName, methods, entries);
            var fileName = $"{baseName}.EventInterface.g.cs";

            if (mode == GenerateMode.WriteFile && filePath != null)
            {
                FileWriter.WriteToPhysicalFile(context, filePath, fileName, source, "EVT");
            }
            else
            {
                context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static string BuildSource(
        string ns,
        string interfaceFullName,
        string baseName,
        System.Collections.Generic.List<IMethodSymbol> methods,
        System.Collections.Generic.List<(string methodName, int id)> entries)
    {
        var nsOpen = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns}\n{{";
        var nsClose = string.IsNullOrEmpty(ns) ? "" : "}";
        var indent = string.IsNullOrEmpty(ns) ? "" : "    ";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generator: EventInterfaceGenerator (Level 5)");
        sb.AppendLine(nsOpen);

        // 1. ID 常量类
        sb.AppendLine($"{indent}/// <summary>事件 ID 常量（FNV-1a 哈希，负数域）</summary>");
        sb.AppendLine($"{indent}public static class {baseName}");
        sb.AppendLine($"{indent}{{");
        foreach (var (methodName, id) in entries)
            sb.AppendLine($"{indent}    public const int {methodName} = {id};");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();

        // 2. 分发器类
        sb.AppendLine($"{indent}/// <summary>事件分发器：将 int ID 映射到接口方法调用</summary>");
        sb.AppendLine($"{indent}public static class {baseName}Dispatcher");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    private static {interfaceFullName}? _handler;");
        sb.AppendLine($"{indent}    public static void Register({interfaceFullName} handler) => _handler = handler;");
        sb.AppendLine();
        sb.AppendLine($"{indent}    public static void Dispatch(int eventId, object? arg = null)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        if (_handler == null) return;");
        sb.AppendLine($"{indent}        switch (eventId)");
        sb.AppendLine($"{indent}        {{");
        foreach (var method in methods)
        {
            var param = method.Parameters.Length > 0
                ? $"({method.Parameters[0].Type.ToDisplayString()})arg!"
                : "";
            var callArgs = method.Parameters.Length > 0 ? param : "";
            sb.AppendLine($"{indent}            case {baseName}.{method.Name}:");
            sb.AppendLine($"{indent}                _handler.{method.Name}({callArgs}); break;");
        }
        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();

        // 3. Helper 类
        sb.AppendLine($"{indent}/// <summary>事件 Helper：便捷触发方法</summary>");
        sb.AppendLine($"{indent}public static class {baseName}Helper");
        sb.AppendLine($"{indent}{{");
        foreach (var method in methods)
        {
            var paramList = string.Join(", ", method.Parameters.Select(p =>
                $"{p.Type.ToDisplayString()} {p.Name}"));
            var argList = method.Parameters.Length > 0 ? method.Parameters[0].Name : "null";
            sb.AppendLine($"{indent}    public static void {method.Name}({paramList}) =>");
            sb.AppendLine($"{indent}        {baseName}Dispatcher.Dispatch({baseName}.{method.Name}, {argList});");
        }
        sb.AppendLine($"{indent}}}");
        sb.AppendLine(nsClose);

        return sb.ToString();
    }

    /// <summary>
    /// FNV-1a 32-bit 哈希算法（稳定，适合生成确定性 ID）
    /// </summary>
    private static uint Fnv1aHash(string input)
    {
        const uint fnvPrime = 16777619;
        const uint fnvOffset = 2166136261;

        var hash = fnvOffset;
        foreach (var c in input)
        {
            hash ^= c;
            hash *= fnvPrime;
        }
        return hash;
    }
}
