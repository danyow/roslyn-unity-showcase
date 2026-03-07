# Level 1 — Hello Generator

> 第一关：用 `RegisterPostInitializationOutput` 在编译前注入 Attribute，再让 SyntaxProvider 扫描它。

---

## 你会学到

- `RegisterPostInitializationOutput` 的作用：在编译流水线的最早阶段把源代码注入到编译上下文中
- 如何编写"自给自足"的生成器——生成器自己定义它所依赖的 Attribute，无需手动在项目里维护该文件
- `context.AddSource(...)` 与 `RegisterPostInitializationOutput` 的区别：前者在增量生成阶段输出最终代码，后者在初始化阶段提前注入辅助代码（如 Attribute）

---

## 核心概念

### 先有鸡还是先有蛋？

假设你的生成器需要扫描 `[Hello]` 标记的类，然后为它们生成代码。问题来了：

> **`[Hello]` Attribute 从哪里来？**

如果 `[Hello]` 定义在普通项目文件里，那编译器在运行生成器之前就能看到它，一切正常。但如果你希望"整个 Attribute 由生成器本身提供"，就会出现循环依赖：

```
生成器要扫描 [Hello] → [Hello] 还不存在 → 编译报错
```

`RegisterPostInitializationOutput` 正是解决这个问题的钥匙。它注册的回调在**编译初始化阶段**（Initialization Phase）执行，早于任何 `SyntaxProvider` 的谓词（Predicate）被调用。时序如下：

```
编译器启动
  └─ 调用 Initialize(context)
       ├─ RegisterPostInitializationOutput 回调 ← 此时注入 HelloAttribute.g.cs
       └─ CreateSyntaxProvider 谓词开始扫描   ← 此时 [Hello] 已在作用域内
```

因此，当 SyntaxProvider 开始遍历语法树时，`HelloAttribute` 已经作为虚拟源文件存在于编译上下文中，编译器能正确解析 `[Hello]`，不会报 CS0246。

---

## 关键代码

### 步骤一：用 `RegisterPostInitializationOutput` 注入 Attribute

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    // 在初始化阶段注入 HelloAttribute 的源代码
    context.RegisterPostInitializationOutput(ctx =>
    {
        ctx.AddSource("HelloAttribute.g.cs", """
            using System;

            [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
            internal sealed class HelloAttribute : Attribute { }
            """);
    });

    // 再注册 SyntaxProvider，此时 [Hello] 已存在
    var provider = context.SyntaxProvider.CreateSyntaxProvider(
        predicate: static (node, _) => node is ClassDeclarationSyntax cls
                                       && cls.AttributeLists.Count > 0,
        transform: static (ctx, _) => GetTarget(ctx)
    ).Where(x => x is not null);

    context.RegisterSourceOutput(provider, Generate);
}
```

### 步骤二：扫描带 `[Hello]` 的类并生成属性

```csharp
private static string? GetTarget(GeneratorSyntaxContext ctx)
{
    var cls = (ClassDeclarationSyntax)ctx.Node;
    foreach (var attrList in cls.AttributeLists)
        foreach (var attr in attrList.Attributes)
            if (ctx.SemanticModel.GetSymbolInfo(attr).Symbol is IMethodSymbol ctor
                && ctor.ContainingType.Name == "HelloAttribute")
                return cls.Identifier.Text;
    return null;
}

private static void Generate(SourceProductionContext ctx, string? className)
{
    if (className is null) return;
    ctx.AddSource($"{className}.Hello.g.cs", $$"""
        partial class {{className}}
        {
            public string HelloMessage => "Hello from {{className}}!";
        }
        """);
}
```

运行后，带 `[Hello]` 的 `partial class` 会自动获得一个 `HelloMessage` 属性。

---

## 在 Unity 中运行

1. 打开 Unity 项目，找到 `Assets/Showcase/Level1/Level1Demo.cs`。
2. 确保 `HelloGenerator` 所在的 `.csproj` 已配置好 Analyzer 引用。
3. 在 Rider 或 Visual Studio 中，展开"Analyzers → HelloGenerator → HelloAttribute.g.cs"，确认 Attribute 已被注入。
4. 运行场景，Console 中会打印：

   ```
   Hello from Level1Demo!
   ```

5. 也可以直接在脚本里访问 `gameObject.GetComponent<Level1Demo>().HelloMessage`，验证生成的属性存在。

---

## 下一关预告

**Level 2 — AutoProperty Generator**

下一关不再扫描整个类，而是深入到**字段级别**：用 `FieldDeclarationSyntax` 扫描私有字段，自动生成对应的公开属性。你将学会如何处理"`int _a, _b;`"这种一行多字段的声明，以及如何把 `_camelCase` 转换成 `PascalCase`。
