# 01 — Level 0：Logger（完整骨架）

## 你会学到

1. `IIncrementalGenerator` 的完整骨架：`Initialize` → `CreateSyntaxProvider` → `RegisterSourceOutput`
2. 用 `SemanticModel.GetSymbolInfo` 验证特性全名，防止同名特性产生误匹配
3. 从 `dnyw` 迁移真实项目代码的改造思路

---

## 核心概念

### 为什么用 IIncrementalGenerator 而不是 ISourceGenerator？

`ISourceGenerator`（旧版）每次编译都重新运行，在大型项目中极慢。

`IIncrementalGenerator`（新版，Unity 2022.2+ 支持）引入了**增量缓存**：
- 只有发生变化的语法节点才重新处理
- 未变化的节点直接使用上次缓存的结果
- 在 Unity 中的效果：修改一个脚本不会导致所有生成器重新运行

### CreateSyntaxProvider：两步过滤

```
所有语法节点（可能有数万个）
    ↓ predicate（语法树，极快）
过滤后的节点（只有 class/struct 声明）
    ↓ transform（语义模型，较慢但精确）
确认带有 [Logger] 的类型
```

第一步用语法树粗筛（速度极快，不触发语义解析）；第二步用语义模型精确验证（只对通过第一步的节点执行）。这是增量生成器的性能关键。

### 内嵌 Core vs 引用 Core

生成器 DLL 引入 Unity 时有严格限制：**只有一个 DLL 可以被标记为 `RoslynAnalyzer`**，额外的 DLL 引用会导致 Unity 编译失败。

解决方案：在 `.csproj` 中用 `<Compile Include>` 将 Core 的源码**直接编译进**生成器 DLL：

```xml
<!-- Level0.Logger.csproj -->
<ItemGroup>
    <Compile Include="..\Core\Attributes\LogAttribute.cs">
        <Link>Core\LogAttribute.cs</Link>
    </Compile>
</ItemGroup>
```

这样最终只有一个 DLL（`Showcase.Level0.Logger.dll`），其中包含了 `LoggerAttribute` 的定义。

---

## 关键代码

```csharp
// 文件路径: showcase/roslyn/Level0.Logger/LogSourceGenerator.cs

// ① 完全限定名：即使用户写了自己的 [Logger]，也不会误匹配
private const string AttributeFullName = "Showcase.Core.LoggerAttribute";

// ② transform 步骤：语义验证
private static (...) GetTypeForSourceGen(GeneratorSyntaxContext context)
{
    var syntax = (ClassDeclarationSyntax)context.Node;

    foreach (var attr in syntax.AttributeLists.SelectMany(al => al.Attributes))
    {
        // GetSymbolInfo 触发语义解析，获取 Attribute 构造函数的符号
        if (context.SemanticModel.GetSymbolInfo(attr).Symbol
            is not IMethodSymbol attrSymbol)
            continue;

        // ContainingType.ToDisplayString() = "Showcase.Core.LoggerAttribute"
        if (attrSymbol.ContainingType.ToDisplayString() != AttributeFullName)
            continue;

        return (syntax, found: true, contextMode: ...);
    }
    return (syntax, found: false, contextMode: false);
}
```

```csharp
// ③ 生成的代码（静态模式）：
//    [Logger] class Foo → 内嵌 private static class Debug
//    调用路径：Debug.Log("msg") → UnityEngine.Debug.Log("[Foo] msg")
private static class Debug
{
    private const string Tag = "Foo";  // 编译期确定的类名

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Log(object msg) =>
        UnityEngine.Debug.Log($"[{Tag}] {msg}");
    // ...
}
```

---

## 在 Unity 中运行

1. 执行 `dotnet build showcase/roslyn/Level0.Logger/Level0.Logger.csproj`
2. DLL 自动复制到 `game/Assets/Generators/Level0.Logger/`
3. Unity 重新导入 DLL，自动应用 `RoslynAnalyzer` 标签（来自 `.dll.meta`）
4. 打开 `game/Assets/Showcase/Level0/Level0Demo.cs`
5. 创建 Scene，挂载 `Level0Demo` 组件并运行
6. Console 应看到：
   ```
   [Level0Demo] Level 0 Logger 演示启动！
   [Level0Demo] 这是一条警告
   [Level0Demo] 这是一条错误
   ✓ Level 0 通关：LogSourceGenerator 运行正常
   ```

---

## 下一关预告

Level 1 将展示**最小化生成器**，核心是 `RegisterPostInitializationOutput`：让生成器无需外部依赖，自给自足地注入 `[Hello]` 特性定义。这解决了"先有鸡还是先有蛋"的问题——特性定义本身也由生成器产生。
