# 第 4 章 — 动态扫描：`[AutoPatch]` 批量应用

> 用反射扫描替代手动注册，实现"标记即生效"的零配置补丁系统。

---

## 你会学到

- **`AppDomain.CurrentDomain.GetAssemblies()` + `GetCustomAttributes` 反射扫描**：在运行时自动发现所有标记了 `[AutoPatch]` 的类，批量应用补丁
- **`StackTracePatch.ApplyPatches()` 模式**：来自 dnyw 项目的实际架构参考，展示如何在框架层统一管理补丁的生命周期
- **批量打补丁 vs 单独打补丁**：`PatchAll(assembly)` 与逐一调用 `Patch(method)` 的取舍——前者简单但缺乏控制，后者精细但繁琐

---

## 核心概念

### 为什么需要动态扫描？

随着项目规模增长，补丁类会越来越多。手动注册的问题：

```csharp
// 手动注册 — 随着补丁增多，这个方法会变成噩梦
void ApplyAllPatches(Harmony harmony)
{
    harmony.Patch(typeof(A).GetMethod("M1"), prefix: new HarmonyMethod(typeof(APatch), "Prefix"));
    harmony.Patch(typeof(B).GetMethod("M2"), prefix: new HarmonyMethod(typeof(BPatch), "Prefix"));
    harmony.Patch(typeof(C).GetMethod("M3"), postfix: new HarmonyMethod(typeof(CPatch), "Postfix"));
    // ... 50 行后 ...
    // 新人加补丁时经常忘记在这里注册
}
```

动态扫描的理想形态：

```csharp
// 只需在补丁类上加 [AutoPatch]，扫描器自动发现并注册
[AutoPatch]
[HarmonyPatch(typeof(PlayerController), "Jump")]
public static class JumpPatch { ... }

// 启动时一行代码搞定所有补丁
AutoPatchScanner.ApplyAll(harmony);
```

### `PatchAll` vs 手动 `Patch` 的取舍

| | `harmony.PatchAll(assembly)` | 手动 `harmony.Patch(...)` |
|---|---|---|
| **代码量** | 一行 | 每个补丁都要写 |
| **粒度控制** | 无（全部应用） | 可按条件应用 |
| **错误隔离** | 某个补丁失败会中断整体 | 可对每个 Patch 单独 try-catch |
| **适用场景** | 简单项目，补丁全部有效 | 生产环境，需要容错 |

动态扫描结合 `[AutoPatch]` 是两者的折中：自动发现，但对每个补丁单独 `try-catch`，失败一个不影响其他。

### `ApplyPatches()` 模式（来自 dnyw 项目）

`StackTracePatch.ApplyPatches()` 是 dnyw 项目中的实际实现，展示了一种成熟的补丁管理架构：

1. **集中入口**：所有 Harmony 初始化逻辑集中在一个 `ApplyPatches()` 静态方法中
2. **分类管理**：按功能域（如 `StackTrace`、`Logging`、`Network`）分组，每组有独立的 Harmony 实例
3. **优雅降级**：如果某个补丁因目标方法不存在（版本差异）而失败，记录警告但不崩溃
4. **统一撤销**：所有 Harmony 实例的 ID 遵循统一命名规范，便于批量撤销

### 反射扫描的性能考量

`GetAssemblies()` 和 `GetTypes()` 在大型项目中可能耗时数百毫秒。最佳实践：

- 只在**启动阶段**执行一次，结果缓存
- 用 `Assembly.GetExecutingAssembly()` 代替 `GetAssemblies()`，限定扫描范围
- 如果有多个程序集，明确列出需要扫描的程序集，避免扫描所有 Unity 内置程序集

---

## 关键代码

### 自定义 `[AutoPatch]` Attribute

```csharp
/// <summary>
/// 标记此 Harmony 补丁类应由 AutoPatchScanner 自动发现并应用。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AutoPatchAttribute : Attribute
{
    /// <summary>可选：控制此补丁是否启用（用于 Feature Flag）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>可选：补丁分组，同组的补丁共享一个 Harmony 实例</summary>
    public string Group { get; set; } = "default";
}
```

### `AutoPatchScanner`：动态扫描实现

```csharp
public static class AutoPatchScanner
{
    private static readonly Dictionary<string, Harmony> _harmonyInstances = new();

    /// <summary>
    /// 扫描指定程序集中所有标记了 [AutoPatch] 的类，并应用补丁。
    /// </summary>
    public static void ApplyAll(Assembly assembly, string harmonyIdPrefix = "com.auto")
    {
        // 遍历程序集中的所有类型
        foreach (var type in assembly.GetTypes())
        {
            // 查找 [AutoPatch] Attribute
            var autoPatch = type.GetCustomAttribute<AutoPatchAttribute>();
            if (autoPatch == null || !autoPatch.Enabled) continue;

            // 获取或创建该分组的 Harmony 实例
            var harmonyId = $"{harmonyIdPrefix}.{autoPatch.Group}";
            if (!_harmonyInstances.TryGetValue(harmonyId, out var harmony))
            {
                harmony = new Harmony(harmonyId);
                _harmonyInstances[harmonyId] = harmony;
            }

            // 容错：单独 try-catch，一个补丁失败不影响其他
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                Debug.Log($"[AutoPatch] 已应用：{type.Name} (Group: {autoPatch.Group})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutoPatch] 应用 {type.Name} 失败：{ex.Message}");
            }
        }
    }

    /// <summary>撤销所有 AutoPatch 管理的补丁</summary>
    public static void UnpatchAll()
    {
        foreach (var (id, harmony) in _harmonyInstances)
        {
            harmony.UnpatchAll(id);
            Debug.Log($"[AutoPatch] 已撤销组：{id}");
        }
        _harmonyInstances.Clear();
    }

    /// <summary>撤销特定分组的补丁</summary>
    public static void UnpatchGroup(string group, string harmonyIdPrefix = "com.auto")
    {
        var harmonyId = $"{harmonyIdPrefix}.{group}";
        if (_harmonyInstances.TryGetValue(harmonyId, out var harmony))
        {
            harmony.UnpatchAll(harmonyId);
            _harmonyInstances.Remove(harmonyId);
            Debug.Log($"[AutoPatch] 已撤销组：{harmonyId}");
        }
    }
}
```

### 使用方式：标记补丁类

```csharp
// 只需加上 [AutoPatch]，扫描器自动处理
[AutoPatch(Group = "performance")]
[HarmonyPatch(typeof(DataLoader), nameof(DataLoader.LoadAllPlayers))]
public static class DataLoaderTimerPatch
{
    static void Prefix(ref Stopwatch __state) => __state = Stopwatch.StartNew();
    static void Postfix(Stopwatch __state)    => Debug.Log($"耗时：{__state.ElapsedMilliseconds}ms");
}

[AutoPatch(Group = "debug", Enabled = false)] // Enabled=false，默认不应用
[HarmonyPatch(typeof(CombatSystem), "Attack")]
public static class AttackDebugPatch
{
    static void Prefix() => Debug.Log("攻击开始");
}
```

### 启动时初始化（对应 `StackTracePatch.ApplyPatches()` 模式）

```csharp
public static class HarmonyManager
{
    /// <summary>
    /// 集中管理所有 Harmony 补丁的入口。
    /// 对应 dnyw 项目中的 StackTracePatch.ApplyPatches() 模式。
    /// </summary>
    public static void ApplyPatches()
    {
        // 扫描主程序集
        var mainAssembly = Assembly.GetExecutingAssembly();
        AutoPatchScanner.ApplyAll(mainAssembly, "com.showcase");

        // 如果有插件程序集，也可以额外扫描
        foreach (var pluginAssembly in GetPluginAssemblies())
            AutoPatchScanner.ApplyAll(pluginAssembly, "com.showcase.plugin");

        Debug.Log("[HarmonyManager] 所有补丁应用完毕");
    }

    public static void RemovePatches()
    {
        AutoPatchScanner.UnpatchAll();
        Debug.Log("[HarmonyManager] 所有补丁已撤销");
    }

    private static IEnumerable<Assembly> GetPluginAssemblies()
    {
        // 只返回明确标记为插件的程序集，避免扫描整个 AppDomain
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetCustomAttribute<HarmonyPluginAttribute>() != null);
    }
}

// 在 Unity 启动时调用
public class GameBootstrap : MonoBehaviour
{
    void Awake()
    {
        HarmonyManager.ApplyPatches();
    }

    void OnDestroy()
    {
        HarmonyManager.RemovePatches();
    }
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/HarmonyChapter4/Chapter4Demo.cs`，场景中有多个不同类型的系统组件。

2. 启动场景，Console 打印自动扫描结果：

   ```
   [AutoPatch] 已应用：DataLoaderTimerPatch (Group: performance)
   [AutoPatch] 已应用：JumpInterceptPatch (Group: default)
   [AutoPatch] 已应用：PlayerRepositoryMockPatch (Group: debug)
   [HarmonyManager] 所有补丁应用完毕
   ```

3. 注意 `AttackDebugPatch`（`Enabled=false`）不在列表中——它被正确跳过。

4. 操作游戏，验证前三章的所有功能都通过 AutoPatch 自动生效：计时器、跳跃拦截、Mock 数据。

5. 点击"卸载所有补丁"按钮，Console 打印：

   ```
   [AutoPatch] 已撤销组：com.showcase.performance
   [AutoPatch] 已撤销组：com.showcase.default
   [AutoPatch] 已撤销组：com.showcase.debug
   [HarmonyManager] 所有补丁已撤销
   ```

   再次操作游戏，确认所有补丁行为均已消失，系统恢复原始状态。

---

## 恭喜番外通关！

你已完成 HarmonyX 番外篇的全部 4 章。回顾一下学到的所有技术：

| 章节 | 核心技术 | 典型用途 |
|------|----------|----------|
| 第 0 章 — 入门 | `Harmony` 实例、`PatchAll`、`UnpatchAll` | 环境搭建，理解 IL 补丁原理 |
| 第 1 章 — Prefix/Postfix | `bool Prefix`、`void Postfix`、多补丁链式执行 | 拦截、观察、条件跳过 |
| 第 2 章 — `__state` | `ref __state`、跨钩子状态传递、线程安全 | 性能计时、上下文传递 |
| 第 3 章 — `__result` | `ref __result`、返回值替换 | Mock 测试、A/B 测试、Bug 紧急修复 |
| 第 4 章 — 动态扫描 | `[AutoPatch]`、反射扫描、批量注册 | 大型项目补丁管理 |

### Roslyn + Harmony 组合使用

两套工具互补，覆盖了代码修改的完整时间线：

```
源代码编写阶段  → Roslyn Analyzer 实时检查，给出 Diagnostic
编译阶段       → Roslyn Generator 生成样板代码
运行时          → Harmony 补丁修改无法在编译期触及的行为
```

在实际项目中，你可以用 Roslyn Generator 减少 Harmony 补丁的数量（很多需要"钩子"的场景可以用生成器在设计期解决），同时在真正需要运行时干预的地方用 Harmony 补丁。两者配合，可以以极低的侵入性实现非常强大的代码扩展能力。

---

## 下一章预告

**第 5 章 — 实战：为 Debug.Log 添加带颜色的前缀**

前四章掌握了 Harmony 的全部核心 API。下一章用一个来自真实项目的完整案例，综合运用手动 `Patch` + `AccessTools` + `ref` 参数篡改，为 Unity 内置的 `Debug.Log` / `LogWarning` / `LogError` 全局注入颜色前缀——无需修改项目中任何一行现有代码。
