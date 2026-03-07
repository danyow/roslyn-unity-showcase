# HarmonyX 入门 — 什么是 Harmony？

> 番外篇开篇：理解运行时 IL 补丁与编译期源代码生成的本质区别，以及各自适用的场景。

---

## 你会学到

- **Harmony 的钩子机制**：如何在运行时拦截任意方法调用，在方法执行前后插入自定义逻辑
- **何时选择 Harmony，何时选择 Roslyn**：两者处理问题的时机不同，适用场景完全不重叠
- **IL 补丁 vs 源代码生成**：一个在字节码层面做手脚，一个在源文件层面添砖加瓦

---

## 核心概念

### Harmony 是什么？

HarmonyX 是一个运行时方法补丁库，原理是在程序**运行时**通过反射和 IL（中间语言）重写来修改方法行为，无需修改源代码或重新编译。

核心流程：

```
应用程序启动
  └─ 调用 Harmony.PatchAll()
       ├─ 扫描程序集，找到所有标记了 [HarmonyPatch] 的类
       ├─ 对每个目标方法，在内存中重写其 IL 字节码
       │    ├─ 插入 Prefix 方法调用（在原方法前）
       │    └─ 插入 Postfix 方法调用（在原方法后）
       └─ 后续所有对该方法的调用都会经过补丁
```

### Harmony vs Roslyn：不同的时间，不同的战场

| 维度 | Roslyn 生成器/分析器 | Harmony |
|------|---------------------|---------|
| **工作时机** | **编译时**（源文件 → IL） | **运行时**（IL → IL） |
| **修改对象** | 你自己的源代码 | 任意已编译的方法（包括第三方库） |
| **主要用途** | 减少样板代码，自动化生成 | 修改无法改动的代码行为 |
| **调试难度** | 低（生成的代码可见） | 较高（IL 层面操作，需要反编译工具） |
| **稳定性风险** | 低（编译期错误早发现） | 中（目标方法签名变化会导致补丁失效） |
| **适用场景** | 新功能开发，代码自动化 | 框架扩展，热补丁，测试 mock |

**一句话总结**：

> Roslyn 是在**建房子之前**修改蓝图；Harmony 是在**房子建好之后**凿墙打洞。

### Harmony 的典型使用场景

1. **修改第三方 Unity 插件的行为**：你买了一个插件，它的某个方法有 Bug，但你没有源码。用 Harmony 可以在不修改原始 DLL 的情况下修复它。

2. **为框架方法添加性能监控**：用 Postfix 在每个关键方法调用后记录耗时，无需修改框架代码。

3. **测试环境下的依赖替换**：用 Prefix 拦截网络请求方法，返回 mock 数据，不改动生产代码。

4. **模组（Mod）开发**：Unity 游戏的 Mod 系统几乎都基于 Harmony，允许玩家在不修改游戏本体的情况下扩展游戏。

### Harmony ID 系统

每个 `Harmony` 实例需要一个唯一 ID，用于识别和清理该实例的所有补丁：

```csharp
var harmony = new Harmony("com.yourcompany.yourproject");
```

清理时只影响该 ID 下注册的补丁，不影响其他 Harmony 实例（例如其他插件）的补丁：

```csharp
harmony.UnpatchAll("com.yourcompany.yourproject"); // 只清理自己的补丁
```

---

## 关键代码

### 基础初始化

```csharp
using HarmonyLib;
using UnityEngine;

public class HarmonyBootstrap : MonoBehaviour
{
    private Harmony _harmony;

    void Awake()
    {
        // 创建 Harmony 实例，ID 唯一标识此补丁集
        _harmony = new Harmony("com.showcase.harmony-demo");

        // 自动扫描当前程序集中所有 [HarmonyPatch] 类并应用补丁
        _harmony.PatchAll(Assembly.GetExecutingAssembly());

        Debug.Log("[Harmony] 所有补丁已应用");
    }

    void OnDestroy()
    {
        // 清理所有此实例的补丁（重要！避免内存泄漏或跨场景污染）
        _harmony.UnpatchAll("com.showcase.harmony-demo");
        Debug.Log("[Harmony] 所有补丁已撤销");
    }
}
```

### `PatchAll` vs 手动 `Patch`

```csharp
// 方式一：自动扫描（推荐）
harmony.PatchAll(Assembly.GetExecutingAssembly());

// 方式二：手动指定（用于需要条件化应用补丁的场景）
var originalMethod = typeof(TargetClass).GetMethod("TargetMethod");
var prefixMethod   = typeof(MyPatch).GetMethod("Prefix");
var postfixMethod  = typeof(MyPatch).GetMethod("Postfix");

harmony.Patch(originalMethod,
    prefix:  new HarmonyMethod(prefixMethod),
    postfix: new HarmonyMethod(postfixMethod));
```

### 撤销所有补丁

```csharp
// 撤销当前 Harmony 实例的所有补丁
harmony.UnpatchAll("com.showcase.harmony-demo");

// 核武器：撤销进程中所有 Harmony 实例的所有补丁（谨慎使用）
Harmony.UnpatchAll();
```

---

## 下一章预告

**第 1 章 — Prefix / Postfix 基础**

下一章进入实战：用 `[HarmonyPatch]` 为一个 Unity 方法添加 Prefix 和 Postfix。你将看到：

- Prefix 返回 `false` 如何**阻止**原方法执行
- Postfix 如何在原方法执行完毕后**无条件**运行
- 同一方法上有多个补丁时，执行顺序是怎样的
