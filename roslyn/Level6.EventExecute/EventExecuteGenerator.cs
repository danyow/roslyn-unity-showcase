using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Scriban;
using SourceGenerator.Infrastructure;

namespace Showcase.Generator.Level6;

/// <summary>
/// Level 6 — EventExecute 生成器（JSON 配置驱动 + Scriban 模板）
///
/// 引入 API：
///   - AdditionalTextsProvider / File I/O  读取外部 JSON 配置
///   - Scriban Template.Parse + Render     模板引擎生成复杂代码
///   - 嵌入资源 (EmbeddedResource)         将 .scriban 打包进 DLL
///   - Assembly.GetManifestResourceStream   运行时加载嵌入模板
///
/// 生成内容：
///   [AutoEventExecute] abstract class BaseEvent → 生成抽象 Execute 声明
///   sealed class SpecificEvent : BaseEvent      → 生成 switch 分发实现
///   Config class                                → 生成方法桩（待手动实现）
///
/// 关键教学：
///   - JSON 配置解析：System.Text.Json 遍历 JSON 数组提取方法签名
///   - 两阶段生成：基类（抽象声明）+ 子类（具体实现）
///   - Scriban 模板：适合复杂控制流的代码生成（if/for/for.last）
///   - 配置优先级：Attribute 参数 > MSBuild 属性 > 默认值
/// </summary>
[Generator]
public class EventExecuteGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "AutoEventExecuteAttribute";
    private const string AttributeShortName = "AutoEventExecute";
    private const string DefaultTypeFieldName = "$type";
    private const string DefaultMethodFieldName = "method";
    private const string DefaultArgsFieldName = "args";
    private const string DefaultReturnTypeFieldName = "returnType";
    private const string DefaultFileExtension = ".Event.g.cs";
    private const string DefaultConfigDir = "Assets/Showcase/Level6/Configs";

    /// <summary>
    /// BaseValue 子类名到 C# 类型的映射表
    /// </summary>
    private static readonly Dictionary<string, string> ValueTypeToCSharpMap = new()
    {
        { "IntValue", "int" },
        { "FloatValue", "float" },
        { "StringValue", "string" },
        { "BoolValue", "bool" },
    };

    /// <summary>
    /// BaseReturnType 子类名到 C# 返回类型和分发方法名的映射表
    /// </summary>
    private static readonly Dictionary<string, (string CsType, string DispatchMethod)> ReturnTypeMap = new()
    {
        { "IntType", ("int", "RequestInt") },
        { "FloatType", ("float", "RequestFloat") },
        { "StringType", ("string", "RequestString") },
        { "BoolType", ("bool", "RequestBool") },
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 读取 MSBuild 可配置属性
        var configProvider = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.GenerateMode", out var mode);
                provider.GlobalOptions.TryGetValue("build_property.EventExecuteConfigPath", out var configPath);
                provider.GlobalOptions.TryGetValue("build_property.EventExecuteTypeField", out var typeField);
                provider.GlobalOptions.TryGetValue("build_property.EventExecuteMethodField", out var methodField);
                return new GeneratorSettings
                {
                    Mode = GeneratorConfig.ParseMode(mode),
                    ConfigPath = configPath ?? string.Empty,
                    TypeField = typeField ?? string.Empty,
                    MethodField = methodField ?? string.Empty,
                };
            }
        );

        // 语法过滤
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax cds
                    && (cds.AttributeLists.Count > 0
                        || (cds.BaseList != null && cds.Modifiers.Any(m => m.Text == "sealed"))),
                transform: static (ctx, _) => ExtractClassInfo(ctx)
            )
            .Where(static info => info != null);

        // 收集所有类信息，组合配置
        var combined = classDeclarations.Collect().Combine(configProvider);

        context.RegisterSourceOutput(
            combined,
            static (spc, data) =>
            {
                var (classInfos, settings) = data;
                GenerateCode(spc, classInfos!, settings);
            }
        );
    }

    private static ClassInfo? ExtractClassInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol == null) return null;

        // 检查是否标记了 AutoEventExecuteAttribute
        // 注意：必须过滤到当前语法树中声明的属性，避免 partial class 跨文件时重复生成
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.ApplicationSyntaxReference?.SyntaxTree != classDecl.SyntaxTree)
                continue;

            var attrName = attr.AttributeClass?.Name;
            if (attrName == AttributeFullName || attrName == AttributeShortName)
            {
                string? typeField = null;
                string? methodField = null;

                foreach (var namedArg in attr.NamedArguments)
                {
                    switch (namedArg.Key)
                    {
                        case "TypeField": typeField = namedArg.Value.Value as string; break;
                        case "MethodField": methodField = namedArg.Value.Value as string; break;
                    }
                }

                return new ClassInfo
                {
                    ClassName = symbol.Name,
                    SourceFilePath = classDecl.SyntaxTree.FilePath,
                    IsMarkedClass = true,
                    AttributeTypeField = typeField,
                    AttributeMethodField = methodField,
                };
            }
        }

        // 检查是否为 sealed 子类
        if (symbol.IsSealed && symbol.BaseType != null)
        {
            var baseType = symbol.BaseType;
            while (baseType != null)
            {
                foreach (var attr in baseType.GetAttributes())
                {
                    var attrName = attr.AttributeClass?.Name;
                    if (attrName == AttributeFullName || attrName == AttributeShortName)
                    {
                        string? refIdConfigTypeName = null;
                        HashSet<string>? configExistingMethods = null;

                        foreach (var member in symbol.GetMembers())
                        {
                            if (member is IFieldSymbol field && field.Name == "refIdConfig")
                            {
                                refIdConfigTypeName = field.Type.Name;
                                configExistingMethods = new HashSet<string>();
                                if (field.Type is INamedTypeSymbol configType)
                                {
                                    foreach (var configMember in configType.GetMembers())
                                    {
                                        if (configMember is IMethodSymbol method
                                            && method.MethodKind == MethodKind.Ordinary
                                            && !method.IsImplicitlyDeclared)
                                        {
                                            var location = method.Locations.FirstOrDefault();
                                            if (location?.SourceTree?.FilePath.EndsWith(DefaultFileExtension) == true)
                                                continue;
                                            configExistingMethods.Add(method.Name);
                                        }
                                    }
                                }
                                break;
                            }
                        }

                        return new ClassInfo
                        {
                            ClassName = symbol.Name,
                            SourceFilePath = classDecl.SyntaxTree.FilePath,
                            IsMarkedClass = false,
                            BaseClassName = baseType.Name,
                            RefIdConfigTypeName = refIdConfigTypeName,
                            ConfigExistingMethods = configExistingMethods,
                        };
                    }
                }
                baseType = baseType.BaseType;
            }
        }

        return null;
    }

    private static void GenerateCode(
        SourceProductionContext context,
        ImmutableArray<ClassInfo?> classInfos,
        GeneratorSettings settings)
    {
        if (classInfos.IsDefaultOrEmpty) return;

        // 分离标记类和子类
        var markedClasses = new Dictionary<string, ClassInfo>();
        var subclasses = new List<ClassInfo>();

        foreach (var info in classInfos)
        {
            if (info == null) continue;
            if (info.IsMarkedClass) markedClasses[info.ClassName] = info;
            else subclasses.Add(info);
        }

        if (markedClasses.Count == 0) return;

        // 加载 Scriban 模板
        var abstractTemplate = LoadEmbeddedTemplate("AbstractExecute.scriban");
        var concreteTemplate = LoadEmbeddedTemplate("ConcreteExecute.scriban");
        var configTemplate = LoadEmbeddedTemplate("ConfigExecute.scriban");
        if (abstractTemplate == null || concreteTemplate == null || configTemplate == null) return;

        foreach (var markedClass in markedClasses.Values)
        {
            var typeField = ResolveField(markedClass.AttributeTypeField, settings.TypeField, DefaultTypeFieldName);
            var methodField = ResolveField(markedClass.AttributeMethodField, settings.MethodField, DefaultMethodFieldName);

            // 解析配置目录（优先 MSBuild 属性，回退到从源文件向上查找项目根目录）
            var configDir = ResolveConfigDir(settings.ConfigPath, markedClass.SourceFilePath);

            // 找到属于当前标记类的子类
            var relevantSubclasses = subclasses
                .Where(s => s.BaseClassName == markedClass.ClassName)
                .GroupBy(s => s.ClassName)
                .Select(g => g.First())
                .ToList();

            // 读取 JSON 配置（如果有配置目录的话）
            Dictionary<string, Dictionary<string, MethodInfo>> eventTypeMethods;
            if (configDir != null && Directory.Exists(configDir))
            {
                try { eventTypeMethods = ParseMethodsFromDirectory(configDir, typeField, methodField); }
                catch { eventTypeMethods = new Dictionary<string, Dictionary<string, MethodInfo>>(); }
            }
            else
            {
                eventTypeMethods = new Dictionary<string, Dictionary<string, MethodInfo>>();
            }

            // 收集所有子类涉及的返回类型集合
            var allReturnTypes = new Dictionary<string, (string CsType, string DispatchMethod)>();
            foreach (var subclass in relevantSubclasses)
            {
                if (eventTypeMethods.TryGetValue(subclass.ClassName, out var methods))
                {
                    foreach (var m in methods.Values)
                    {
                        if (m.DispatchMethod != "Execute" && !allReturnTypes.ContainsKey(m.DispatchMethod))
                            allReturnTypes[m.DispatchMethod] = (m.ReturnCsType, m.DispatchMethod);
                    }
                }
            }

            // 生成基类抽象 Execute 声明
            var returnTypesForTemplate = allReturnTypes.Values
                .OrderBy(rt => rt.DispatchMethod)
                .Select(rt => new { cs_type = rt.CsType, dispatch_method = rt.DispatchMethod })
                .ToList();
            var abstractSource = abstractTemplate.Render(
                new { class_name = markedClass.ClassName, return_types = returnTypesForTemplate });

            var abstractFileName = $"{markedClass.ClassName}{DefaultFileExtension}";
            EmitSource(context, settings.Mode, markedClass.SourceFilePath, abstractFileName, abstractSource);

            var configDirName = configDir != null ? Path.GetFileName(configDir) : "config";

            // 生成子类实现
            foreach (var subclass in relevantSubclasses)
            {
                if (!eventTypeMethods.TryGetValue(subclass.ClassName, out var methodDict) || methodDict.Count == 0)
                    continue;

                var methodGroups = methodDict.Values
                    .GroupBy(m => m.DispatchMethod)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        dispatch_method = g.Key,
                        return_type = g.First().ReturnCsType,
                        methods = g.OrderBy(m => m.MethodName)
                            .Select(m => new
                            {
                                method_name = m.MethodName,
                                @params = m.Params.Select(p => new
                                {
                                    name = p.Name, value_type = p.ValueType, cs_type = p.CsType,
                                }).ToList(),
                            }).ToList(),
                    }).ToList();

                var concreteSource = concreteTemplate.Render(new
                {
                    class_name = subclass.ClassName,
                    config_class_name = subclass.RefIdConfigTypeName,
                    config_file = configDirName,
                    method_groups = methodGroups,
                });

                var concreteFileName = $"{subclass.ClassName}{DefaultFileExtension}";
                EmitSource(context, settings.Mode, subclass.SourceFilePath, concreteFileName, concreteSource);

                // 生成 Config 类方法声明
                if (!string.IsNullOrEmpty(subclass.RefIdConfigTypeName))
                {
                    var allMethods = methodDict.Values
                        .OrderBy(m => m.MethodName)
                        .Select(m => new
                        {
                            method_name = m.MethodName, return_type = m.ReturnCsType,
                            @params = m.Params.Select(p => new { name = p.Name, value_type = p.ValueType, cs_type = p.CsType }).ToList(),
                        }).ToList();

                    var missingMethods = allMethods
                        .Where(m => subclass.ConfigExistingMethods == null
                            || !subclass.ConfigExistingMethods.Contains((string)m.method_name))
                        .ToList();

                    if (missingMethods.Count > 0)
                    {
                        var configSource = configTemplate.Render(new
                        {
                            config_class_name = subclass.RefIdConfigTypeName,
                            config_file = configDirName,
                            methods = missingMethods,
                        });
                        var configFileName = $"{subclass.RefIdConfigTypeName}{DefaultFileExtension}";
                        EmitSource(context, settings.Mode, subclass.SourceFilePath, configFileName, configSource);
                    }
                }
            }
        }
    }

    private static void EmitSource(
        SourceProductionContext context,
        GenerateMode mode,
        string sourceFilePath,
        string fileName,
        string source)
    {
        if (mode == GenerateMode.WriteFile)
        {
            FileWriter.WriteToPhysicalFile(context, sourceFilePath, fileName, source, "EVTEXEC");
        }
        else
        {
            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string ResolveField(string? attributeValue, string msBuildValue, string defaultValue)
    {
        if (!string.IsNullOrEmpty(attributeValue)) return attributeValue!;
        if (!string.IsNullOrEmpty(msBuildValue)) return msBuildValue;
        return defaultValue;
    }

    /// <summary>
    /// 解析配置目录：优先使用 MSBuild 属性，回退到从源文件向上查找项目根目录
    /// </summary>
    private static string? ResolveConfigDir(string configuredPath, string sourceFilePath)
    {
        if (!string.IsNullOrEmpty(configuredPath))
        {
            return configuredPath;
        }

        var projectRoot = FindProjectRoot(sourceFilePath);
        if (projectRoot == null) return null;
        return Path.Combine(projectRoot, DefaultConfigDir);
    }

    /// <summary>
    /// 从源文件路径向上查找包含 DefaultConfigDir 的项目根目录
    /// </summary>
    private static string? FindProjectRoot(string sourceFilePath)
    {
        var dir = Path.GetDirectoryName(sourceFilePath);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, DefaultConfigDir)))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static Template? LoadEmbeddedTemplate(string templateName)
    {
        var assembly = typeof(EventExecuteGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(templateName));
        if (resourceName == null) return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Template.Parse(reader.ReadToEnd());
    }

    private static Dictionary<string, Dictionary<string, MethodInfo>> ParseMethodsFromDirectory(
        string configDir, string typeField, string methodField)
    {
        var result = new Dictionary<string, Dictionary<string, MethodInfo>>();

        foreach (var jsonFile in Directory.GetFiles(configDir, "*.json"))
        {
            var json = File.ReadAllText(jsonFile, Encoding.UTF8);
            var fileMethods = ParseMethodsFromJson(json, typeField, methodField);
            foreach (var kvp in fileMethods)
            {
                if (!result.TryGetValue(kvp.Key, out var methodDict))
                {
                    methodDict = new Dictionary<string, MethodInfo>();
                    result[kvp.Key] = methodDict;
                }
                foreach (var methodKvp in kvp.Value)
                {
                    if (!methodDict.ContainsKey(methodKvp.Key))
                        methodDict[methodKvp.Key] = methodKvp.Value;
                }
            }
        }
        return result;
    }

    private static Dictionary<string, Dictionary<string, MethodInfo>> ParseMethodsFromJson(
        string json, string typeField, string methodField)
    {
        var result = new Dictionary<string, Dictionary<string, MethodInfo>>();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array) return result;

        foreach (var configElement in root.EnumerateArray())
        {
            foreach (var property in configElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var eventElement in property.Value.EnumerateArray())
                {
                    if (eventElement.ValueKind != JsonValueKind.Object) continue;
                    if (!eventElement.TryGetProperty(typeField, out var typeElement)) continue;
                    if (!eventElement.TryGetProperty(methodField, out var methodElement)) continue;

                    var typeName = typeElement.GetString();
                    var methodName = methodElement.GetString();
                    if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName)) continue;
                    if (!IsValidIdentifier(methodName!)) continue;

                    if (!result.TryGetValue(typeName!, out var methodDict))
                    {
                        methodDict = new Dictionary<string, MethodInfo>();
                        result[typeName!] = methodDict;
                    }

                    // 解析 args 参数
                    var paramList = new List<MethodParamInfo>();
                    if (eventElement.TryGetProperty(DefaultArgsFieldName, out var argsElement)
                        && argsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var argEntry in argsElement.EnumerateArray())
                        {
                            if (argEntry.ValueKind != JsonValueKind.Array || argEntry.GetArrayLength() != 2) continue;
                            var paramName = argEntry[0].GetString();
                            if (string.IsNullOrEmpty(paramName) || !IsValidIdentifier(paramName!)) continue;

                            var valueElement = argEntry[1];
                            if (valueElement.ValueKind == JsonValueKind.Object
                                && valueElement.TryGetProperty(DefaultTypeFieldName, out var argTypeElement))
                            {
                                var valueType = argTypeElement.GetString();
                                if (!string.IsNullOrEmpty(valueType) && ValueTypeToCSharpMap.TryGetValue(valueType!, out var csType))
                                {
                                    paramList.Add(new MethodParamInfo
                                    {
                                        Name = paramName!, ValueType = valueType!, CsType = csType,
                                    });
                                }
                            }
                        }
                    }

                    if (!methodDict.ContainsKey(methodName!))
                    {
                        var returnCsType = "void";
                        var dispatchMethod = "Execute";
                        if (eventElement.TryGetProperty(DefaultReturnTypeFieldName, out var returnTypeElement)
                            && returnTypeElement.ValueKind == JsonValueKind.Object
                            && returnTypeElement.TryGetProperty(DefaultTypeFieldName, out var returnTypeType))
                        {
                            var returnTypeName = returnTypeType.GetString();
                            if (!string.IsNullOrEmpty(returnTypeName) && ReturnTypeMap.TryGetValue(returnTypeName!, out var mapping))
                            {
                                returnCsType = mapping.CsType;
                                dispatchMethod = mapping.DispatchMethod;
                            }
                        }

                        methodDict[methodName!] = new MethodInfo
                        {
                            MethodName = methodName!, Params = paramList,
                            ReturnCsType = returnCsType, DispatchMethod = dispatchMethod,
                        };
                    }
                }
            }
        }
        return result;
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (var i = 1; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
        return true;
    }

    private sealed class GeneratorSettings
    {
        public GenerateMode Mode { get; set; }
        public string ConfigPath { get; set; } = string.Empty;
        public string TypeField { get; set; } = string.Empty;
        public string MethodField { get; set; } = string.Empty;
    }

    private sealed class ClassInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string SourceFilePath { get; set; } = string.Empty;
        public bool IsMarkedClass { get; set; }
        public string? BaseClassName { get; set; }
        public string? AttributeTypeField { get; set; }
        public string? AttributeMethodField { get; set; }
        public string? RefIdConfigTypeName { get; set; }
        public HashSet<string>? ConfigExistingMethods { get; set; }
    }

    private sealed class MethodParamInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
        public string CsType { get; set; } = string.Empty;
    }

    private sealed class MethodInfo
    {
        public string MethodName { get; set; } = string.Empty;
        public List<MethodParamInfo> Params { get; set; } = new();
        public string ReturnCsType { get; set; } = "void";
        public string DispatchMethod { get; set; } = "Execute";
    }
}
