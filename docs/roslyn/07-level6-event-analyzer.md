# Level 6 — EventAnalyzer

> 第六关：Roslyn 的只读面——`DiagnosticAnalyzer` 分析现有代码并报告问题，`CodeFixProvider` 提供一键修复。

---

## 你会学到

- **`DiagnosticAnalyzer`**：只读的代码分析器，不生成任何文件，专注于检测代码中的错误模式并报告 `Diagnostic`
- **`CodeFixProvider`**：与分析器配合，在 IDE 中提供"灯泡"（Lightbulb）菜单，让开发者一键应用推荐的代码修复
- **`RegisterSyntaxNodeAction`**：按语法节点类型注册分析回调，在编译器遍历语法树时被调用

---

## 核心概念

### Analyzer 与 Generator 的本质区别

| 特性 | `IIncrementalGenerator` | `DiagnosticAnalyzer` |
|------|------------------------|----------------------|
| 主要职责 | **生成**新的源代码文件 | **分析**现有代码，报告问题 |
| 是否写入文件 | 是（`AddSource`） | 否（只读） |
| 输出 | `.g.cs` 生成文件 | `Diagnostic`（警告/错误） |
| IDE 表现 | 在 Analyzers 树中出现新文件 | 代码下出现黄色/红色波浪线 |
| 可配合 | — | `CodeFixProvider`（灯泡修复） |

两者可以**共存于同一个程序集**。Level 5 的 `EventInterfaceGenerator` 生成代码，Level 6 的 `EventAnalyzer` 检查这些生成代码的使用方式是否正确——它们是互补关系。

### `DiagnosticSeverity` 等级

```csharp
DiagnosticSeverity.Error    // 红色波浪线，阻止编译（或在设置中视为错误）
DiagnosticSeverity.Warning  // 黄色波浪线，不阻止编译
DiagnosticSeverity.Info     // 蓝色提示
DiagnosticSeverity.Hidden   // 不显示，但 IDE 工具可读
```

EventAnalyzer 对"使用字符串字面量作为事件 ID"报告 `Warning`（EVT002），建议改用 `GameEvents.XXX` 常量。

### `CodeFixProvider` 的灯泡机制

当用户把光标放在波浪线上时，IDE 会查询所有注册了对应 Diagnostic ID 的 `CodeFixProvider`，把它们的修复动作显示在灯泡菜单里。`CodeFixProvider` 通过 **Roslyn 的语法树变换 API**（`SyntaxFactory`、`DocumentEditor`）来修改代码，整个过程不涉及文件 I/O，而是在内存中操作语法树。

---

## 关键代码

### `DiagnosticAnalyzer` 骨架

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EventAnalyzer : DiagnosticAnalyzer
{
    // 描述要报告的诊断
    public static readonly DiagnosticDescriptor RuleEvt002 = new(
        id:                 "EVT002",
        title:              "使用字符串字面量作为事件 ID",
        messageFormat:      "事件调用 '{0}' 使用了字符串字面量，建议改用 GameEvents.{1} 常量",
        category:           "EventInterface",
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "直接使用字符串会绕过 FNV-1a 哈希校验，导致 ID 不稳定。");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleEvt002);

    public override void Initialize(AnalysisContext context)
    {
        // ★ 允许并发执行（Roslyn 最佳实践）
        context.EnableConcurrentExecution();
        // 不分析生成的代码（避免误报）
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // 注册：当遇到方法调用表达式时触发回调
        context.RegisterSyntaxNodeAction(
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        // 检查是否调用了 EventDispatcher.Emit 或 EventDispatcher.On
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.Text is not ("Emit" or "On"))
            return;

        // 检查第一个参数是否是字符串字面量（错误用法）
        var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (firstArg?.Expression is not LiteralExpressionSyntax literal)
            return;
        if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var stringValue = literal.Token.ValueText;
        var diag = Diagnostic.Create(
            RuleEvt002,
            literal.GetLocation(),
            stringValue, stringValue);

        ctx.ReportDiagnostic(diag);
    }
}
```

### `CodeFixProvider`：注册修复动作

```csharp
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EventCodeFixProvider))]
[Shared]
public class EventCodeFixProvider : CodeFixProvider
{
    // 声明能修复哪些 Diagnostic ID
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EventAnalyzer.RuleEvt002.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // 找到出问题的字符串字面量节点
        var literal = root?.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<LiteralExpressionSyntax>()
            .First();

        if (literal is null) return;

        // 注册修复动作：把字符串替换为 GameEvents.XXX
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "替换为 GameEvents 常量",
                createChangedDocument: ct => ReplaceWithConstant(context.Document, literal, ct),
                equivalenceKey: "ReplaceWithGameEventsConst"),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithConstant(
        Document document,
        LiteralExpressionSyntax literal,
        CancellationToken ct)
    {
        var stringValue = literal.Token.ValueText;
        // 生成替换节点：GameEvents.OnPlayerDied
        var newNode = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("GameEvents"),
            SyntaxFactory.IdentifierName(stringValue));

        var root = await document.GetSyntaxRootAsync(ct);
        var newRoot = root!.ReplaceNode(literal, newNode);
        return document.WithSyntaxRoot(newRoot);
    }
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/Level6/Level6Demo.cs`，找到以下注释行：

   ```csharp
   // 取消注释下一行以触发 EVT002 警告：
   // EventDispatcher.Emit("OnPlayerDied", playerData);
   ```

2. 在 Rider 或 Visual Studio 中取消注释该行。

3. IDE 会在 `"OnPlayerDied"` 下显示**黄色波浪线**，悬停查看：

   ```
   EVT002: 事件调用 'Emit' 使用了字符串字面量，建议改用 GameEvents.OnPlayerDied 常量
   ```

4. 点击灯泡图标（或按 `Alt+Enter`），选择"替换为 GameEvents 常量"，代码自动变为：

   ```csharp
   EventDispatcher.Emit(GameEvents.IOnPlayerDied, playerData);
   ```

5. 运行场景，Console 打印正常事件分发日志，无警告。

---

## 下一关预告

**Level 7 — ModuleMesh（Boss 关卡）**

最终关卡将两个生成器协同工作：一个生成器扫描**方法级别的 Attribute**（`MethodDeclarationSyntax`），另一个生成器读取前者的输出，生成跨模块通信的胶水代码。你将看到 Roslyn 生成器在复杂架构中的实际应用——模块解耦、注册表模式、以及 Event/Tool 双通道通信。
