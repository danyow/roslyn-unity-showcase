# Level 2 — AutoProperty Generator

> 第二关：用 `FieldDeclarationSyntax` 扫描私有字段，自动生成公开属性，并处理 `_camelCase → PascalCase` 的命名转换。

---

## 你会学到

- `FieldDeclarationSyntax` 的结构：一个字段声明节点可以包含多个变量（`int _a, _b, _c;`），需要遍历 `Declaration.Variables`
- 如何从 `VariableDeclaratorSyntax` 的 `Identifier.Text` 中提取字段名，并用命名规则转换为属性名
- 按**包含类型**分组：一个类里可能有多个字段，需要聚合后统一为该类生成一个源文件，避免重复 `partial class` 块冲突

---

## 核心概念

### 为什么要扫描字段而不是类？

Level 1 扫描的是带特定 Attribute 的**类**，粒度较粗。AutoProperty 的需求更精细：

- 开发者在类里写私有字段 `private float _health;`
- 生成器为每个字段自动生成 `public float Health { get; set; }`
- 字段是否需要生成，由字段本身的 Attribute（如 `[AutoProperty]`）或命名前缀决定

因此谓词（Predicate）需要命中 **`FieldDeclarationSyntax`** 节点，而不是 `ClassDeclarationSyntax`。

### 一行多字段的陷阱

C# 允许在一行声明多个字段：

```csharp
private float _health, _mana, _stamina;
```

在语法树中，这对应**一个** `FieldDeclarationSyntax` 节点，但它的 `Declaration.Variables` 列表中有**三个** `VariableDeclaratorSyntax`。如果只取第一个，会漏掉其余字段。正确做法是遍历所有 `Variables`：

```
FieldDeclarationSyntax
  └─ VariableDeclarationSyntax (Type: float)
       ├─ VariableDeclaratorSyntax (_health)
       ├─ VariableDeclaratorSyntax (_mana)
       └─ VariableDeclaratorSyntax (_stamina)
```

### 按包含类型分组

一个类可能有多个字段，每个字段都会命中一次 Predicate。如果为每个字段各自输出一个 `partial class` 文件，会产生多个同名 `partial class` 块，虽然编译上没问题，但文件数量爆炸，IDE 展示也很混乱。

标准做法是用 `.Collect()` 或在 Transform 阶段提取 `(类名, 字段列表)` 元组，再按类名分组，最终每个类只生成一个文件。

---

## 关键代码

### 谓词：命中字段声明

```csharp
context.SyntaxProvider.CreateSyntaxProvider(
    predicate: static (node, _) => node is FieldDeclarationSyntax field
                                   && field.AttributeLists.Count > 0,
    transform: static (ctx, _) => ExtractFieldInfo(ctx)
)
```

### 提取字段信息

```csharp
private static FieldInfo? ExtractFieldInfo(GeneratorSyntaxContext ctx)
{
    var field = (FieldDeclarationSyntax)ctx.Node;

    // 确认带有 [AutoProperty]
    bool hasAttr = field.AttributeLists
        .SelectMany(al => al.Attributes)
        .Any(a => a.Name.ToString() is "AutoProperty" or "AutoPropertyAttribute");
    if (!hasAttr) return null;

    // 获取包含类型名称
    if (field.Parent is not ClassDeclarationSyntax cls) return null;

    // 收集所有变量名，以及字段类型
    var typeName = field.Declaration.Type.ToString();
    var variables = field.Declaration.Variables
        .Select(v => v.Identifier.Text)
        .ToList();

    return new FieldInfo(cls.Identifier.Text, typeName, variables);
}
```

### 命名转换：`_camelCase → PascalCase`

```csharp
private static string ToPropertyName(string fieldName)
{
    // 去掉下划线前缀，首字母大写
    var name = fieldName.TrimStart('_');
    if (string.IsNullOrEmpty(name)) return fieldName;
    return char.ToUpperInvariant(name[0]) + name[1..];
}

// 示例：
// "_health"    → "Health"
// "_moveSpeed" → "MoveSpeed"
// "_playerName"→ "PlayerName"
```

### 生成属性代码

```csharp
private static void Generate(SourceProductionContext ctx, ImmutableArray<FieldInfo> fields)
{
    // 按类名分组
    foreach (var group in fields.GroupBy(f => f.ClassName))
    {
        var sb = new StringBuilder();
        sb.AppendLine($"partial class {group.Key}");
        sb.AppendLine("{");

        foreach (var field in group)
        foreach (var varName in field.VariableNames)
        {
            var propName = ToPropertyName(varName);
            sb.AppendLine($"    public {field.TypeName} {propName}");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        get => {varName};");
            sb.AppendLine($"        set => {varName} = value;");
            sb.AppendLine($"    }}");
        }

        sb.AppendLine("}");
        ctx.AddSource($"{group.Key}.AutoProperty.g.cs", sb.ToString());
    }
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/Level2/Level2Demo.cs`，可以看到：

   ```csharp
   [AutoProperty] private float _health;
   [AutoProperty] private float _moveSpeed;
   [AutoProperty] private string _playerName;
   ```

2. 在 Rider 中展开 Analyzers，查看 `Level2Demo.AutoProperty.g.cs`，确认生成了：

   ```csharp
   public float Health { get => _health; set => _health = value; }
   public float MoveSpeed { get => _moveSpeed; set => _moveSpeed = value; }
   public string PlayerName { get => _playerName; set => _playerName = value; }
   ```

3. 运行场景，Console 输出玩家数据：

   ```
   Health=100, MoveSpeed=5.5, PlayerName=Hero
   ```

---

## 下一关预告

**Level 3 — AutoImplement Generator**

下一关进入**语义层**。仅靠语法树无法知道一个 `typeof(ISaveable)` 究竟代表哪个接口、有哪些方法——你需要 `SemanticModel.GetTypeInfo()` 来查询符号信息，并用 `INamedTypeSymbol.GetMembers()` 遍历接口成员，自动生成抛出 `NotImplementedException` 的方法存根。
