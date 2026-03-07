# Level 3 — AutoImplement Generator

> 第三关：首次进入语义层。用 `SemanticModel.GetTypeInfo` 解析 `typeof()` 表达式，再用 `INamedTypeSymbol.GetMembers()` 枚举接口成员，自动生成方法存根。

---

## 你会学到

- `SemanticModel.GetTypeInfo(TypeOfExpressionSyntax)` 的用法：把语法树中的 `typeof(ISaveable)` 转换为有意义的类型符号 `ITypeSymbol`
- `TypeOfExpressionSyntax` 在语法树中的位置：它是 `Attribute` 的参数，需要从 `AttributeSyntax → ArgumentList → Arguments[0] → Expression` 逐层向下取到
- 通过 `INamedTypeSymbol.GetMembers()` 遍历接口的所有成员（方法、属性），并自动生成带 `throw new NotImplementedException()` 的存根实现

---

## 核心概念

### 语法树 vs 语义模型：两个不同的世界

Roslyn 把对代码的理解分成两层，理解这两层的区别是掌握高级生成器的关键。

**语法树（Syntax Tree）**是对源代码文本的结构化表示，它只关心"长什么样"：

```
AttributeSyntax  [AutoImplement(typeof(ISaveable))]
  └─ ArgumentListSyntax
       └─ AttributeArgumentSyntax
            └─ TypeOfExpressionSyntax  ← 这里
                 └─ IdentifierNameSyntax "ISaveable"
```

在这个层面，`ISaveable` 只是一个字符串标识符，语法树**不知道**它是接口还是类，**不知道**它有哪些方法，甚至**不知道**它是否真的存在。

**语义模型（Semantic Model）**则是编译器对代码含义的理解。它可以回答：

- `ISaveable` 是什么类型？（`INamedTypeSymbol`，Kind = Interface）
- 它有哪些成员？（`GetMembers()` 返回 `void Save()`, `void Load()` 等）
- 它在哪个命名空间？（`ContainingNamespace`）

`SemanticModel.GetTypeInfo(node)` 就是从语法树节点跨越到语义世界的桥梁。

### 关键流程

```
[AutoImplement(typeof(ISaveable))]
         ↓ 语法层：找到 TypeOfExpressionSyntax
         ↓ 语义层：GetTypeInfo → ITypeSymbol
         ↓ 转型：as INamedTypeSymbol
         ↓ 遍历：GetMembers().OfType<IMethodSymbol>()
         ↓ 生成：为每个方法生成 throw new NotImplementedException()
```

---

## 关键代码

### 从 Attribute 中提取 `TypeOfExpressionSyntax`

```csharp
private static ITypeSymbol? GetTargetInterface(GeneratorSyntaxContext ctx)
{
    var cls = (ClassDeclarationSyntax)ctx.Node;

    foreach (var attrList in cls.AttributeLists)
    foreach (var attr in attrList.Attributes)
    {
        // 确认是 AutoImplement attribute
        if (attr.Name.ToString() is not ("AutoImplement" or "AutoImplementAttribute"))
            continue;

        // 取第一个参数
        var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
        if (arg?.Expression is not TypeOfExpressionSyntax typeOf)
            continue;

        // ★ 关键：用语义模型解析 typeof() 内的类型
        var typeInfo = ctx.SemanticModel.GetTypeInfo(typeOf.Type);
        return typeInfo.Type;
    }

    return null;
}
```

### 遍历接口成员，生成存根

```csharp
private static void Generate(SourceProductionContext ctx,
    (string ClassName, ITypeSymbol Interface) target)
{
    var (className, iface) = target;
    if (iface is not INamedTypeSymbol namedType) return;

    var sb = new StringBuilder();
    sb.AppendLine($"partial class {className}");
    sb.AppendLine("{");

    // GetMembers() 遍历所有接口成员
    foreach (var member in namedType.GetMembers())
    {
        if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
        {
            // 构建参数列表
            var paramList = string.Join(", ",
                method.Parameters.Select(p => $"{p.Type.Name} {p.Name}"));

            var returnType = method.ReturnType.Name == "Void" ? "void" : method.ReturnType.Name;

            sb.AppendLine($"    public {returnType} {method.Name}({paramList})");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        throw new System.NotImplementedException();");
            sb.AppendLine($"    }}");
            sb.AppendLine();
        }
        else if (member is IPropertySymbol prop)
        {
            sb.AppendLine($"    public {prop.Type.Name} {prop.Name}");
            sb.AppendLine($"    {{");
            if (!prop.IsWriteOnly)
                sb.AppendLine($"        get => throw new System.NotImplementedException();");
            if (!prop.IsReadOnly)
                sb.AppendLine($"        set => throw new System.NotImplementedException();");
            sb.AppendLine($"    }}");
            sb.AppendLine();
        }
    }

    sb.AppendLine("}");
    ctx.AddSource($"{className}.AutoImplement.g.cs", sb.ToString());
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/Level3/Level3Demo.cs`：

   ```csharp
   [AutoImplement(typeof(ISaveable))]
   public partial class Level3Demo : MonoBehaviour, ISaveable
   {
       void Start() => Save(); // 调用生成的方法
   }
   ```

2. 在 Rider 中展开 `Level3Demo.AutoImplement.g.cs`，确认生成了 `Save()` 和 `Load()` 方法，均抛出 `NotImplementedException`。

3. 运行场景，Console 会打印来自 Unity 的异常日志：

   ```
   NotImplementedException: The method or operation is not implemented.
     at Level3Demo.Save ()
   ```

   这正是预期行为——生成器完成了它的工作（生成编译期骨架），业务逻辑由开发者自行填充。

---

## 下一关预告

**Level 4 — AutoService Generator with Scriban**

下一关引入两项新技术：

1. **`.Collect()`**：把多个独立的 Provider 结果聚合成一个 `ImmutableArray`，实现"扫描多个类，生成一个汇总文件"
2. **Scriban 模板引擎**：用 `.scriban` 文本模板替代 `StringBuilder` 拼字符串，让代码生成逻辑更清晰、更易维护

你将学会如何在 Analyzer 项目中嵌入 `.scriban` 文件为 `EmbeddedResource`，以及为什么 `Scriban.dll` 也需要标记 `<ReferenceOutputAssembly>false</ReferenceOutputAssembly>`。
