using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGenerator.Infrastructure;

namespace Showcase.Generator.Level7;

/// <summary>
/// Level 7 — ModuleCodeGenerator（第二阶段：读取方法信息生成胶水代码）
///
/// 这是 Boss 关的第二个 Generator，与 SchemaGenerator 协作：
///   SchemaGenerator  → 生成 ModuleMeshSchema（Schema 描述）
///   ModuleCodeGenerator → 生成 ModuleMeshRegistry（运行时注册/调用代码）
///
/// 关键教学：
///   - 两个 Generator 可以在同一个程序集中共存，各自独立注册
///   - 跨 Assembly 的模块通信：通过生成的 Registry 类实现解耦
///   - 方法级 Attribute 扫描 + 参数提取的完整流程
/// </summary>
[Generator]
public class ModuleCodeGenerator : IIncrementalGenerator
{
    private const string EventAttributeFullName = "Showcase.Core.ModuleMesh.EventAttribute";
    private const string ToolAttributeFullName = "Showcase.Core.ModuleMesh.ToolAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configProvider = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.GenerateMode", out var mode);
                return GeneratorConfig.ParseMode(mode);
            }
        );

        var eventMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax m
                    && m.AttributeLists.Count > 0,
                transform: static (ctx, _) => GetEventMethodInfo(ctx)
            )
            .Where(static t => t != null)
            .Select(static (t, _) => t!);

        var toolMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax m
                    && m.AttributeLists.Count > 0,
                transform: static (ctx, _) => GetToolMethodInfo(ctx)
            )
            .Where(static t => t != null)
            .Select(static (t, _) => t!);

        context.RegisterSourceOutput(
            eventMethods.Collect().Combine(toolMethods.Collect()).Combine(configProvider),
            static (ctx, t) => GenerateRegistry(ctx, t.Left.Left, t.Left.Right, t.Right)
        );
    }

    private static EventMethodInfo? GetEventMethodInfo(GeneratorSyntaxContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        foreach (var attrList in method.AttributeLists)
        foreach (var attr in attrList.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                continue;
            if (attrSymbol.ContainingType.ToDisplayString() != EventAttributeFullName)
                continue;

            var args = attr.ArgumentList?.Arguments;
            if (args == null || args.Value.Count < 2) continue;

            var eventName = args.Value[0].Expression is LiteralExpressionSyntax lit
                ? lit.Token.ValueText : args.Value[0].ToString().Trim('"');
            var dataType = args.Value[1].Expression is TypeOfExpressionSyntax typeOf
                ? typeOf.Type.ToString() : "object";

            if (method.Parent is not TypeDeclarationSyntax parentType) continue;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(parentType) as INamedTypeSymbol;
            if (classSymbol == null) continue;

            return new EventMethodInfo(
                eventName, dataType, method.Identifier.Text,
                classSymbol.ToDisplayString(), classSymbol.Name, method.SyntaxTree.FilePath);
        }
        return null;
    }

    private static ToolMethodInfo? GetToolMethodInfo(GeneratorSyntaxContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        foreach (var attrList in method.AttributeLists)
        foreach (var attr in attrList.Attributes)
        {
            if (context.SemanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol attrSymbol)
                continue;
            if (attrSymbol.ContainingType.ToDisplayString() != ToolAttributeFullName)
                continue;

            var args = attr.ArgumentList?.Arguments;
            if (args == null || args.Value.Count < 3) continue;

            var toolName = args.Value[0].Expression is LiteralExpressionSyntax lit
                ? lit.Token.ValueText : args.Value[0].ToString().Trim('"');
            var inputType = args.Value[1].Expression is TypeOfExpressionSyntax t1
                ? t1.Type.ToString() : "object";
            var outputType = args.Value[2].Expression is TypeOfExpressionSyntax t2
                ? t2.Type.ToString() : "object";

            if (method.Parent is not TypeDeclarationSyntax parentType) continue;
            var classSymbol = context.SemanticModel.GetDeclaredSymbol(parentType) as INamedTypeSymbol;
            if (classSymbol == null) continue;

            return new ToolMethodInfo(
                toolName, inputType, outputType, method.Identifier.Text,
                classSymbol.ToDisplayString(), classSymbol.Name, method.SyntaxTree.FilePath);
        }
        return null;
    }

    private static void GenerateRegistry(
        SourceProductionContext context,
        ImmutableArray<EventMethodInfo> events,
        ImmutableArray<ToolMethodInfo> tools,
        GenerateMode mode)
    {
        if (events.IsEmpty && tools.IsEmpty) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generator: ModuleCodeGenerator (Level 7 - Phase 2)");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace Showcase.Generated.ModuleMesh");
        sb.AppendLine("{");

        sb.AppendLine("    /// <summary>模块网格运行时注册表：事件订阅 + 工具调用胶水代码</summary>");
        sb.AppendLine("    public static partial class ModuleMeshRegistry");
        sb.AppendLine("    {");

        // 事件处理器存储
        sb.AppendLine("        // ======= 事件处理器注册 =======");
        sb.AppendLine("        private static readonly Dictionary<string, Action<object>> _eventHandlers");
        sb.AppendLine("            = new Dictionary<string, Action<object>>();");
        sb.AppendLine();

        // 工具处理器存储
        sb.AppendLine("        // ======= 工具处理器注册 =======");
        sb.AppendLine("        private static readonly Dictionary<string, Func<object, object>> _toolHandlers");
        sb.AppendLine("            = new Dictionary<string, Func<object, object>>();");
        sb.AppendLine();

        // 收集所有出现过的类
        var allClassKeys = events.Select(e => e.ContainingClass)
            .Concat(tools.Select(t => t.ContainingClass))
            .Distinct();

        foreach (var classFullName in allClassKeys)
        {
            var classEvents = events.Where(e => e.ContainingClass == classFullName).ToArray();
            var classTools  = tools.Where(t => t.ContainingClass == classFullName).ToArray();
            var className   = (classEvents.Length > 0 ? classEvents[0].ClassName : classTools[0].ClassName);

            sb.AppendLine($"        /// <summary>注册 {className} 的所有 [Event] 和 [Tool] 方法</summary>");
            sb.AppendLine($"        public static void Register{className}({classFullName} instance)");
            sb.AppendLine("        {");
            foreach (var method in classEvents)
            {
                sb.AppendLine($"            _eventHandlers[\"{method.EventName}\"] =");
                sb.AppendLine($"                data => instance.{method.MethodName}(({method.DataType})data);");
            }
            foreach (var method in classTools)
            {
                sb.AppendLine($"            _toolHandlers[\"{method.ToolName}\"] =");
                sb.AppendLine($"                input => instance.{method.MethodName}(({method.InputType})input);");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 事件触发方法
        sb.AppendLine("        /// <summary>触发事件</summary>");
        sb.AppendLine("        public static void Emit(string eventName, object data)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_eventHandlers.TryGetValue(eventName, out var handler))");
        sb.AppendLine("                handler(data);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 工具调用方法
        sb.AppendLine("        /// <summary>调用工具</summary>");
        sb.AppendLine("        public static object? Call(string toolName, object input)");
        sb.AppendLine("        {");
        sb.AppendLine("            return _toolHandlers.TryGetValue(toolName, out var handler)");
        sb.AppendLine("                ? handler(input) : null;");
        sb.AppendLine("        }");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        var source = sb.ToString();
        var fileName = "ModuleMesh.Registry.g.cs";

        var filePath = events.Length > 0 ? events[0].FilePath
            : tools.Length > 0 ? tools[0].FilePath
            : null;

        if (mode == GenerateMode.WriteFile && filePath != null)
        {
            FileWriter.WriteToPhysicalFile(context, filePath, fileName, source, "MESH");
        }
        else
        {
            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private sealed class EventMethodInfo
    {
        public string EventName { get; }
        public string DataType { get; }
        public string MethodName { get; }
        public string ContainingClass { get; }
        public string ClassName { get; }
        public string FilePath { get; }

        public EventMethodInfo(string eventName, string dataType, string methodName,
            string containingClass, string className, string filePath)
        {
            EventName = eventName;
            DataType = dataType;
            MethodName = methodName;
            ContainingClass = containingClass;
            ClassName = className;
            FilePath = filePath;
        }
    }

    private sealed class ToolMethodInfo
    {
        public string ToolName { get; }
        public string InputType { get; }
        public string OutputType { get; }
        public string MethodName { get; }
        public string ContainingClass { get; }
        public string ClassName { get; }
        public string FilePath { get; }

        public ToolMethodInfo(string toolName, string inputType, string outputType,
            string methodName, string containingClass, string className, string filePath)
        {
            ToolName = toolName;
            InputType = inputType;
            OutputType = outputType;
            MethodName = methodName;
            ContainingClass = containingClass;
            ClassName = className;
            FilePath = filePath;
        }
    }
}
