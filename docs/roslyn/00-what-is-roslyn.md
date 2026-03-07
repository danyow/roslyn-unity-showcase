# 00 — 什么是 Roslyn？

## 你会学到

1. Roslyn 编译器平台的核心架构和设计哲学
2. Source Generator 在 Unity 工作流中的位置和价值
3. 语法树（Syntax Tree）与语义模型（Semantic Model）的根本区别

---

## 核心概念

### Roslyn 是"编译器即服务"

传统编译器是一个黑盒：输入 .cs 文件，输出 .dll。你无法干预中间过程。

Roslyn（.NET Compiler Platform）打开了这个黑盒。它将编译过程暴露为一组 API，让你的代码可以：
- **读取**其他代码的结构（语法树）
- **理解**代码的语义（类型、继承关系、方法签名）
- **在编译期生成**新的代码（Source Generator）
- **报告**代码问题（Diagnostic Analyzer）
- **修复**代码问题（Code Fix Provider）

### Source Generator：在编译期"写代码的代码"

Source Generator 是一种特殊的 .NET 程序，它在你的项目编译时运行，可以：
1. 扫描你的源代码
2. 基于扫描结果生成新的 .cs 文件
3. 这些生成的文件和你的手写代码一起编译

```
你的代码 ──→ Roslyn 编译器 ──→ 调用 Source Generator
                                    │
                                    ↓ 生成新的 .cs 代码
                                    │
                                最终 .dll（包含生成的代码）
```

**Unity 中的实际价值：**
- 消除样板代码（属性、序列化、事件 ID）
- 编译期类型安全（无需运行时反射）
- 零运行时开销（生成的代码和手写代码完全等价）

### 语法树 vs 语义模型

这是 Roslyn 最重要的概念区分：

| | 语法树（Syntax Tree） | 语义模型（Semantic Model） |
|--|--|--|
| **是什么** | 代码的文本结构 | 代码的含义 |
| **能做什么** | 识别 `class Foo`、`void Bar()` | 知道 `Foo` 继承自谁、`Bar` 的返回类型 |
| **能否跨文件** | 否（每个文件独立） | 是（跨越整个编译） |
| **速度** | 极快 | 较慢 |
| **API 示例** | `ClassDeclarationSyntax` | `INamedTypeSymbol.GetMembers()` |

**最佳实践**：先用语法树粗筛（快速排除不相关的节点），再用语义模型精确验证（确认类型信息）。这正是 `IIncrementalGenerator` 的 `CreateSyntaxProvider` 中 `predicate` + `transform` 两步设计的意义。

---

## 关键代码

```csharp
// 文件路径: showcase/roslyn/Level0.Logger/LogSourceGenerator.cs

public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var providers = context.SyntaxProvider
        .CreateSyntaxProvider(
            // ① predicate（语法树层面）：只看 class/struct 声明
            //    这一步极快，绝大多数节点在这里被过滤掉
            predicate: static (node, _) => node is ClassDeclarationSyntax,

            // ② transform（语义模型层面）：验证是否有目标 Attribute
            //    通过 SemanticModel 获取 Attribute 的完全限定名
            transform: static (ctx, _) => GetTypeForSourceGen(ctx)
        )
        .Where(static t => t.found);
}

// 语义验证：ContainingType.ToDisplayString() 返回完全限定名
// 防止用户自己写的同名 [Logger] 被误匹配
if (attrSymbol.ContainingType.ToDisplayString() != "Showcase.Core.LoggerAttribute")
    continue;
```

---

## 在 Unity 中运行

1. 先完成 Level 0 的构建（见 [01-level0-logger.md](01-level0-logger.md)）
2. 打开 `game/Assets/Showcase/Level0/Level0Demo.cs`
3. 注意：IDE 中 `Debug.Log(...)` 的 `Debug` 没有报错——它是生成的内嵌类
4. 在 Unity 中运行 Level0 Scene，Console 看到带前缀的日志输出

---

## 下一关预告

Level 1 将展示最小可运行生成器，重点学习 `RegisterPostInitializationOutput`——让生成器**不依赖任何外部引用**，自给自足地注入 Attribute 定义。
