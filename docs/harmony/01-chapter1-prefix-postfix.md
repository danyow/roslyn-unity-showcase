# 第 1 章 — Prefix / Postfix 基础

> 用最简单的两个钩子点理解 Harmony 的核心工作方式：方法执行前拦截（Prefix）和方法执行后监听（Postfix）。

---

## 你会学到

- **`[HarmonyPatch]` Attribute** 如何把一个静态类声明为针对特定方法的补丁
- **Prefix 的 `bool` 返回值**：返回 `true` 继续执行原方法，返回 `false` 跳过原方法——这是 Harmony 最强大也最危险的能力
- **Postfix 始终运行**：无论原方法是否正常返回（即使 Prefix 跳过了原方法），Postfix 都会执行；多个补丁叠加在同一方法上时的链式执行顺序

---

## 核心概念

### Prefix：门卫

Prefix 是在目标方法被调用**之前**运行的钩子。它有两种形态：

**`void` Prefix**（观察者）：只是旁观，不影响目标方法的执行。

```csharp
static void Prefix() { /* 记录日志、统计次数等 */ }
```

**`bool` Prefix**（拦截器）：返回值决定是否继续执行原方法。

```csharp
static bool Prefix()
{
    if (ShouldSkip()) return false; // ← 跳过原方法！
    return true;                    // ← 正常执行原方法
}
```

返回 `false` 的含义是："我已经处理了这个调用，不需要原方法运行"。这在 mock 测试、紧急熔断、条件跳过等场景中非常有用。

### Postfix：善后者

Postfix 在目标方法**执行完毕后**运行，**始终**会被调用（除非原方法抛出未捕获的异常）。即使 Prefix 返回了 `false`（跳过原方法），Postfix 依然执行。

常见用途：
- 记录方法耗时（配合 `__state`，见第 2 章）
- 修改方法返回值（配合 `ref __result`，见第 3 章）
- 清理 Prefix 中申请的资源

### 多补丁的执行顺序

当同一方法上有多个 Harmony 补丁时（来自不同 Harmony 实例），执行顺序如下：

```
调用 TargetMethod()
  ↓
Prefix_A（优先级高）→ Prefix_B → Prefix_C
  ↓
[原始 TargetMethod]（除非某个 Prefix 返回 false）
  ↓
Postfix_C → Postfix_B → Postfix_A（反序）
```

Prefix 按优先级从高到低执行，Postfix 按**相反**顺序执行（类似栈的进出）。可以用 `[HarmonyPriority]` 控制优先级。

---

## 关键代码

### 基础补丁示例：为 `PlayerController.Jump` 添加日志

```csharp
// 目标类（假设这是第三方代码，无法修改）
public class PlayerController
{
    public void Jump(float force)
    {
        _rigidbody.AddForce(Vector3.up * force, ForceMode.Impulse);
    }
}
```

```csharp
// 补丁类：必须是静态类
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Jump))]
public static class PlayerControllerJumpPatch
{
    // Prefix：在 Jump 执行前打印日志
    // 参数名 __instance 是 Harmony 特殊参数，表示被补丁的实例
    static void Prefix(PlayerController __instance, float force)
    {
        Debug.Log($"[Harmony Prefix] {__instance.name} 即将跳跃，力度={force}");
    }

    // Postfix：在 Jump 执行后打印日志
    static void Postfix(PlayerController __instance, float force)
    {
        Debug.Log($"[Harmony Postfix] {__instance.name} 跳跃完成，力度={force}");
    }
}
```

### `bool` Prefix：条件拦截

```csharp
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Jump))]
public static class JumpInterceptPatch
{
    // 当玩家被眩晕时阻止跳跃
    static bool Prefix(PlayerController __instance)
    {
        if (__instance.IsStunned)
        {
            Debug.Log("[Harmony] 眩晕中，跳跃被阻止");
            return false; // ← 不执行原方法
        }
        return true; // ← 正常跳跃
    }
}
```

### `[HarmonyPriority]` 控制执行顺序

```csharp
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Jump))]
[HarmonyPriority(Priority.High)] // 比默认优先级（400）更高
public static class EarlyJumpPatch
{
    static void Prefix() => Debug.Log("我最先执行");
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/HarmonyChapter1/Chapter1Demo.cs`，场景中有一个 `PlayerController`。

2. 按下空格键，观察 Console：

   ```
   [Harmony Prefix] Player 即将跳跃，力度=10
   [PlayerController] 执行跳跃
   [Harmony Postfix] Player 跳跃完成，力度=10
   ```

3. 启用 `JumpInterceptPatch`（取消注释），让玩家进入眩晕状态后再按空格：

   ```
   [Harmony] 眩晕中，跳跃被阻止
   ```

   注意：`[PlayerController] 执行跳跃` 这行**不会出现**，原方法被完全跳过。

---

## 下一章预告

**第 2 章 — `__state`：跨钩子传递数据**

Prefix 和 Postfix 是两个独立的方法，它们之间没有直接的变量共享。如果你想在 Prefix 里记录开始时间，在 Postfix 里计算耗时，就需要用到 `__state` 特殊参数。

下一章将演示如何用 `Stopwatch __state` 精确测量方法执行时间，并解释 `__state` 在 Prefix 和 Postfix 签名中必须保持**相同类型**的原因。
