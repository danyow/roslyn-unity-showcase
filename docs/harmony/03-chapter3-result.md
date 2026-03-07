# 第 3 章 — `__result`：修改方法返回值

> 深入 Postfix 的最强特性：用 `ref __result` 替换原方法的返回值，调用方对此完全无感知。

---

## 你会学到

- **`ref __result` 参数**：在 Postfix 中读取并修改原方法的返回值，不需要触碰调用方代码或原方法实现
- **Mock 数据注入的实际用途**：在测试环境下拦截真实的数据库/网络调用，返回预设的测试数据，让业务逻辑可以独立测试
- **类型严格匹配**：`__result` 的类型必须与目标方法的返回类型**完全一致**，否则 Harmony 会报运行时错误

---

## 核心概念

### `__result` 的工作原理

原方法正常执行完毕后，它的返回值被 Harmony 暂存。Postfix 收到的 `ref __result` 参数就指向这个暂存值。如果 Postfix 修改了 `__result`，调用方最终收到的是修改后的值。

```
调用方：var players = loader.LoadAllPlayers();

执行流程：
  1. [Prefix 运行]
  2. [原方法运行] → 返回真实数据库结果（128 条记录）
  3. [Postfix 运行] → ref __result 收到真实结果
                   → 修改 __result = mockData（2 条测试记录）
  4. 调用方收到：2 条测试记录  ← 不知道发生了什么
```

### 什么时候用 `ref`，什么时候不用？

- **只读** `__result`：Postfix 只查看返回值，不修改 → 不需要 `ref`
- **读写** `__result`：Postfix 需要修改返回值 → **必须加 `ref`**

```csharp
// 只读（观察返回值，做日志）
static void Postfix(List<Player> __result)
{
    Debug.Log($"返回了 {__result?.Count} 条记录");
    // __result 的修改不会影响调用方
}

// 读写（真正修改返回值）
static void Postfix(ref List<Player> __result)
{
    __result = GetMockPlayers(); // ← 调用方收到 mock 数据
}
```

### 类型匹配要求

`__result` 的类型必须与目标方法的返回类型**完全相同**。常见错误：

```csharp
// 目标方法签名：public int GetScore() → 返回 int
static void Postfix(ref long __result) { } // ✗ 错误：int ≠ long
static void Postfix(ref int __result) { }  // ✓ 正确
```

对于泛型方法或返回接口类型的方法，也需要精确匹配：

```csharp
// 目标方法：public IEnumerable<Player> GetPlayers()
static void Postfix(ref IEnumerable<Player> __result) { } // ✓ 匹配接口类型
static void Postfix(ref List<Player> __result) { }         // ✗ 错误：类型不同
```

### `__result` 与 Prefix 跳过原方法的组合

如果 Prefix 返回 `false` 跳过了原方法，`__result` 在 Postfix 中会是该类型的**默认值**（`null` 或 `0`）。这时 Postfix 可以通过 `ref __result` 提供一个合理的替代值：

```csharp
static bool Prefix(ref string __result)
{
    if (_useCache)
    {
        __result = _cachedValue; // 直接在 Prefix 设置返回值
        return false;            // 跳过原方法
    }
    return true;
}

// 即使原方法被跳过，Postfix 依然运行
static void Postfix(ref string __result)
{
    if (string.IsNullOrEmpty(__result))
        __result = "default_value"; // 兜底处理
}
```

---

## 关键代码

### 示例一：测试环境 Mock 数据注入

```csharp
// 目标：PlayerRepository 的真实查询（会访问数据库）
public class PlayerRepository
{
    public List<PlayerData> GetTopPlayers(int count)
    {
        return _database.Query<PlayerData>(
            "SELECT TOP @count * FROM players ORDER BY score DESC",
            new { count });
    }
}
```

```csharp
// Mock 补丁：只在测试环境启用
[HarmonyPatch(typeof(PlayerRepository), nameof(PlayerRepository.GetTopPlayers))]
public static class PlayerRepositoryMockPatch
{
    // 控制开关，默认关闭
    public static bool EnableMock = false;

    static void Postfix(ref List<PlayerData> __result, int count)
    {
        if (!EnableMock) return; // 非测试环境，不介入

        // 替换为 mock 数据，忽略真实数据库结果
        __result = Enumerable.Range(1, count)
            .Select(i => new PlayerData
            {
                Name  = $"MockPlayer_{i}",
                Score = (count - i + 1) * 1000,
                Level = Random.Range(1, 50)
            })
            .ToList();

        Debug.Log($"[Mock] 已替换为 {__result.Count} 条测试数据");
    }
}
```

使用时：

```csharp
// 测试开始前启用 Mock
PlayerRepositoryMockPatch.EnableMock = true;

var topPlayers = repository.GetTopPlayers(5);
// topPlayers 现在是 mock 数据，不访问数据库

// 测试结束后关闭
PlayerRepositoryMockPatch.EnableMock = false;
```

### 示例二：A/B 测试数据注入

```csharp
[HarmonyPatch(typeof(ConfigService), nameof(ConfigService.GetMaxHealth))]
public static class HealthAbTestPatch
{
    // 通过外部配置控制 A/B 组
    static void Postfix(ref float __result)
    {
        string group = AbTestManager.GetGroup("max_health");
        __result = group switch
        {
            "A" => 100f,  // 对照组
            "B" => 150f,  // 实验组（更高生命值）
            _   => __result // 其他组保持原值
        };

        Debug.Log($"[A/B Test] MaxHealth = {__result} (Group: {group})");
    }
}
```

### 示例三：紧急修复返回值 Bug

当第三方库的某个方法有 Bug，返回了错误的值，而你无法修改源码：

```csharp
// 第三方库 Bug：当玩家等级 > 50 时，GetExpRequired 返回负数
[HarmonyPatch(typeof(ThirdPartyLevelSystem), "GetExpRequired")]
public static class ExpRequiredBugFixPatch
{
    static void Postfix(ref int __result, int level)
    {
        if (__result < 0)
        {
            Debug.LogWarning($"[BugFix] GetExpRequired({level}) 返回了负值 {__result}，已修正");
            __result = level * 1000; // 应急修正公式
        }
    }
}
```

---

## 在 Unity 中运行

1. 打开 `Assets/Showcase/HarmonyChapter3/Chapter3Demo.cs`，场景有一个模拟排行榜系统。

2. 在 Inspector 中勾选"Enable Mock Data"复选框。

3. 点击"刷新排行榜"，Console 打印：

   ```
   [Mock] 已替换为 5 条测试数据
   排行榜更新：MockPlayer_1(5000分), MockPlayer_2(4000分), ...
   ```

4. 取消勾选，再次刷新，Console 打印真实数据（来自内存数据库模拟）：

   ```
   排行榜更新：Hero(9800分), Warrior(7600分), ...
   ```

5. 注意两次刷新的 UI 表现完全相同——调用方代码（排行榜 UI）对 mock 替换毫无感知。

---

## 下一章预告

**第 4 章 — 动态扫描：`[AutoPatch]` 批量应用**

前三章都是手动为每个补丁类应用 Harmony。在大型项目中，补丁类可能有几十上百个，一一调用 `harmony.Patch(...)` 既繁琐又易遗漏。

下一章将演示如何用 `AppDomain.CurrentDomain.GetAssemblies()` + 反射扫描自定义 `[AutoPatch]` Attribute，实现"零配置"的批量补丁注册——只要标记了 `[AutoPatch]`，补丁就自动生效，无需手动注册。
