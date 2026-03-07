# Level 5 — EventInterface Generator

> 第五关：为事件接口生成稳定的负整数 ID，引入 FNV-1a 哈希算法，并用 `ReportDiagnostic` 检测 ID 碰撞冲突。

---

## 你会学到

- **`InterfaceDeclarationSyntax`**：如何在谓词中命中接口声明节点，而非类声明
- **FNV-1a 哈希算法**：一种简单、快速且稳定的非加密哈希，保证"相同接口名 → 相同 ID"，跨编译会话不变
- **`context.ReportDiagnostic`**：当两个接口的哈希值碰撞时，主动报告编译错误（Diagnostic），让开发者在编译期发现问题而非运行时

---

## 核心概念

### 为什么使用负整数域？

事件系统通常用整数 ID 来标识事件类型。正整数域往往被手动分配的枚举值或其他系统占用。把自动生成的事件 ID 限定在**负整数域**，可以从根本上避免与正整数 ID 空间的冲突：

```
正整数域 [1, 2^31-1]  → 手动分配 / 其他系统
负整数域 [-2^31, -1]  → EventInterface 自动生成
```

即使两个系统都使用整数 ID，只要约定各自的域，就永远不会撞车。

### FNV-1a 哈希的稳定性

FNV-1a（Fowler–Noll–Vo）是一个**确定性哈希**：给定相同的输入字节序列，输出始终相同，与运行时间、平台、编译器版本无关。

这一点对事件系统至关重要：如果 ID 每次编译都变化，所有序列化存档的事件 ID 就会失效。FNV-1a 保证：

```
FNV1a("OnPlayerDied") → 始终 → 同一个 int 值（取负）
```

算法本身极其简单：

```
hash = 2166136261  (FNV offset basis for 32-bit)
for each byte b:
    hash ^= b
    hash *= 16777619  (FNV prime for 32-bit)
```

结果取反（`~hash` 或 `-hash`）映射到负整数域。

### ID 碰撞检测与 `ReportDiagnostic`

哈希碰撞虽然概率极低，但不为零。生成器维护一个 `Dictionary<int, string>` 记录已分配的 ID：

```
第一次：OnPlayerDied  → ID = -1234567  → 记录到 idMap[-1234567] = "OnPlayerDied"
第二次：OnEnemyDied   → ID = -1234567  → 检测到碰撞！
```

发生碰撞时，调用 `context.ReportDiagnostic` 报告一个 `DiagnosticSeverity.Error` 级别的诊断，编译立即失败，开发者在 IDE 中会看到红色下划线和错误信息，而不是等到运行时才发现问题。

---

## 关键代码

### 谓词：命中接口声明

```csharp
context.SyntaxProvider.CreateSyntaxProvider(
    predicate: static (node, _) => node is InterfaceDeclarationSyntax iface
                                   && iface.AttributeLists.Count > 0,
    transform: static (ctx, _) => GetEventInterfaceInfo(ctx)
)
```

注意与 Level 1-3 的区别：这里匹配的是 `InterfaceDeclarationSyntax`，而不是 `ClassDeclarationSyntax`。

### FNV-1a 哈希实现

```csharp
private static int Fnv1aHash(string text)
{
    const uint FnvOffsetBasis = 2166136261u;
    const uint FnvPrime = 16777619u;

    uint hash = FnvOffsetBasis;
    foreach (char c in text)
    {
        hash ^= (byte)c;
        hash *= FnvPrime;
    }

    // 转为有符号整数，再取反映射到负整数域
    int signed = (int)hash;
    return signed < 0 ? signed : ~signed;
}

// 示例：
// Fnv1aHash("OnPlayerDied")  → 固定返回某个负整数，如 -1839264512
// Fnv1aHash("OnEnemySpawned")→ 固定返回另一个负整数，如 -947823104
```

### ID 碰撞检测

```csharp
private static void GenerateEventIds(
    SourceProductionContext ctx,
    ImmutableArray<EventInterfaceInfo?> interfaces)
{
    var idMap = new Dictionary<int, string>();
    var sb = new StringBuilder();

    sb.AppendLine("public static class GameEvents");
    sb.AppendLine("{");

    foreach (var info in interfaces.Where(i => i is not null))
    {
        int id = Fnv1aHash(info!.InterfaceName);

        // ★ 碰撞检测
        if (idMap.TryGetValue(id, out var existingName))
        {
            var diag = Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "EVT001",
                    title: "事件 ID 碰撞",
                    messageFormat: "接口 '{0}' 与 '{1}' 的 FNV-1a 哈希值相同（{2}），请重命名其中一个",
                    category: "EventInterface",
                    defaultSeverity: DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location: null,
                info.InterfaceName, existingName, id);

            ctx.ReportDiagnostic(diag);
            continue;
        }

        idMap[id] = info.InterfaceName;
        sb.AppendLine($"    /// <summary>{info.InterfaceName} 的稳定事件 ID</summary>");
        sb.AppendLine($"    public const int {info.InterfaceName} = {id};");
    }

    sb.AppendLine("}");
    ctx.AddSource("GameEvents.g.cs", sb.ToString());
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/Level5/Level5Demo.cs`，查看事件接口定义：

   ```csharp
   [EventInterface] public interface IOnPlayerDied { }
   [EventInterface] public interface IOnEnemySpawned { }
   ```

2. 展开生成的 `GameEvents.g.cs`，确认每个接口都有唯一的负整数常量：

   ```csharp
   public static class GameEvents
   {
       public const int IOnPlayerDied   = -1839264512;
       public const int IOnEnemySpawned = -947823104;
   }
   ```

3. 在事件分发器中使用：

   ```csharp
   // 派发事件
   EventDispatcher.Emit(GameEvents.IOnPlayerDied, playerData);

   // 订阅事件
   EventDispatcher.On(GameEvents.IOnPlayerDied, OnPlayerDied);
   ```

4. 运行场景，Console 打印：

   ```
   [EventDispatcher] Emitting: -1839264512 (IOnPlayerDied)
   Player died! Restarting in 3 seconds...
   ```

---

## 下一关预告

**Level 6 — EventAnalyzer**

下一关引入 Roslyn 的另一面：**`DiagnosticAnalyzer`**。与生成器不同，分析器是**只读**的——它不生成任何代码，只检查现有代码并报告问题。你将学会用 `RegisterSyntaxNodeAction` 检测错误的事件调用方式，并配合 **`CodeFixProvider`** 提供"灯泡"一键修复功能。
