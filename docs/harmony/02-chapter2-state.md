# 第 2 章 — `__state`：跨钩子传递数据

> Prefix 和 Postfix 是独立的静态方法，`__state` 是 Harmony 提供的"专用信使"，让两个钩子之间可以安全地共享数据。

---

## 你会学到

- **`__state` 特殊参数**：Harmony 在 Prefix 和 Postfix 之间自动传递的状态载体，类型由开发者自由定义
- **Prefix 中的 `ref __state`**：Prefix 通过 `ref` 参数写入状态；如果 Prefix 不赋值，Postfix 收到的是类型默认值（`null` 或 `0`）
- **类型一致性要求**：Prefix 和 Postfix 中 `__state` 的类型**必须完全相同**，否则 Harmony 无法正确传递数据

---

## 核心概念

### 为什么需要 `__state`？

Prefix 和 Postfix 是两个**静态方法**，它们不共享实例状态，也不能通过局部变量通信。但很多场景需要 Prefix 记录某些信息，Postfix 再读取：

```
Prefix → 记录开始时间 T₀
[原方法执行]
Postfix → 读取 T₀，计算 T₁ - T₀ = 耗时
```

如果没有 `__state`，只能用静态变量（线程不安全！）或外部字典（繁琐）来绕过这个限制。

`__state` 是 Harmony 的官方解决方案：每次方法调用都会创建一个独立的 state 实例，在 Prefix 和对应的 Postfix 调用之间传递，天然线程安全（每次调用有自己的 state）。

### `__state` 的类型规则

**规则一：Prefix 和 Postfix 中的 `__state` 类型必须相同。**

```csharp
// ✓ 正确：两边都是 Stopwatch
static void Prefix(ref Stopwatch __state) { ... }
static void Postfix(Stopwatch __state) { ... }

// ✗ 错误：类型不一致，Harmony 会抛出异常
static void Prefix(ref Stopwatch __state) { ... }
static void Postfix(long __state) { ... }  // 类型不匹配！
```

**规则二：Prefix 中必须用 `ref`，Postfix 中不需要（但可以加 `ref` 如果要修改）。**

```csharp
static void Prefix(ref Stopwatch __state)
{
    __state = Stopwatch.StartNew(); // ← Prefix 赋值
}

static void Postfix(Stopwatch __state) // ← Postfix 只读
{
    __state?.Stop();
    Debug.Log($"耗时：{__state?.ElapsedMilliseconds} ms");
}
```

**规则三：如果 Prefix 没有给 `__state` 赋值，Postfix 收到的是类型默认值。**

- 引用类型（如 `Stopwatch`、`string`）默认值为 `null`
- 值类型（如 `int`、`long`、`bool`）默认值为 `0` / `false`

Postfix 中务必做 null 检查或判断默认值，避免 `NullReferenceException`。

### 线程安全性

Harmony 为每次方法**调用栈帧**创建独立的 state 值。这意味着：

- 如果 `TargetMethod` 在多个线程上并发调用，每个线程都有自己的 `__state` 实例
- 不同调用之间的 state 相互隔离，不会互相污染

这是 `__state` 比静态变量方案的关键优势。

---

## 关键代码

### 经典示例：用 `Stopwatch` 测量方法耗时

```csharp
// 目标方法（模拟耗时操作）
public class DataLoader
{
    public List<PlayerData> LoadAllPlayers()
    {
        // 假设这里有复杂的数据库查询
        Thread.Sleep(Random.Range(10, 100));
        return _database.QueryAll<PlayerData>();
    }
}
```

```csharp
[HarmonyPatch(typeof(DataLoader), nameof(DataLoader.LoadAllPlayers))]
public static class DataLoaderPatch
{
    // Prefix：启动计时器，通过 ref __state 传递给 Postfix
    static void Prefix(ref Stopwatch __state)
    {
        __state = Stopwatch.StartNew();
        Debug.Log("[Harmony] LoadAllPlayers 开始执行...");
    }

    // Postfix：接收计时器，停止并输出耗时
    // 注意：这里不用 ref，因为不需要修改 state，只读取
    static void Postfix(Stopwatch __state, List<PlayerData> __result)
    {
        // 防御性 null 检查（如果 Prefix 因异常未能赋值）
        if (__state == null)
        {
            Debug.LogWarning("[Harmony] __state 为 null，计时器未启动");
            return;
        }

        __state.Stop();
        Debug.Log($"[Harmony] LoadAllPlayers 耗时：{__state.ElapsedMilliseconds} ms，" +
                  $"返回 {__result?.Count ?? 0} 条记录");
    }
}
```

### 使用值类型 `__state`（更轻量）

对于简单场景，可以用 `long` 存储 `DateTime.UtcNow.Ticks`：

```csharp
[HarmonyPatch(typeof(PlayerController), "Update")]
public static class UpdateTimerPatch
{
    static void Prefix(ref long __state)
    {
        __state = DateTime.UtcNow.Ticks; // 值类型，直接赋值
    }

    static void Postfix(long __state)
    {
        if (__state == 0L) return; // 默认值检查

        var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - __state);
        if (elapsed.TotalMilliseconds > 16.6) // 超过一帧时间
            Debug.LogWarning($"[Harmony] Update 耗时超标：{elapsed.TotalMilliseconds:F2} ms");
    }
}
```

### 使用自定义结构体 `__state`（携带多个数据）

```csharp
// 自定义状态结构体，可携带任意多个字段
public struct AttackPatchState
{
    public long StartTicks;
    public int  InitialHealth;
    public bool WasCritical;
}

[HarmonyPatch(typeof(CombatSystem), nameof(CombatSystem.Attack))]
public static class AttackPatch
{
    static void Prefix(CombatSystem __instance, ref AttackPatchState __state)
    {
        __state = new AttackPatchState
        {
            StartTicks     = DateTime.UtcNow.Ticks,
            InitialHealth  = __instance.Target?.Health ?? 0,
            WasCritical    = false
        };
    }

    static void Postfix(CombatSystem __instance, AttackPatchState __state)
    {
        var elapsed     = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - __state.StartTicks);
        int damageDealt = __state.InitialHealth - (__instance.Target?.Health ?? 0);

        Debug.Log($"[Combat] 攻击耗时 {elapsed.TotalMilliseconds:F1} ms，" +
                  $"造成 {damageDealt} 点伤害");
    }
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/HarmonyChapter2/Chapter2Demo.cs`，场景中有一个 `DataLoader` 组件。

2. 点击 Inspector 中的"Load Data"按钮，观察 Console：

   ```
   [Harmony] LoadAllPlayers 开始执行...
   [Harmony] LoadAllPlayers 耗时：47 ms，返回 128 条记录
   ```

3. 多次点击，观察耗时的随机波动——每次调用都有独立的计时器，互不干扰。

4. 打开"多线程测试"模式，同时触发多个 `LoadAllPlayers` 调用，确认日志中的耗时数据仍然正确（线程安全验证）。

---

## 下一章预告

**第 3 章 — `__result`：修改方法返回值**

`__state` 解决了 Prefix 到 Postfix 的数据传递。`__result` 则更进一步：在 Postfix 中直接**修改原方法的返回值**，调用方收到的是被修改后的结果，完全无感知。

这在测试 mock、A/B 测试数据注入、紧急修复逻辑错误等场景下极为强大——下一章将演示如何用 `ref __result` 注入测试数据，以及需要注意的类型匹配陷阱。
