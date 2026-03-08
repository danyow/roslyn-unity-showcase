# 第 5 章 — 实战：为 Debug.Log 添加带颜色的前缀

> 综合运用手动 `Patch` + `AccessTools` + `ref` 参数篡改，为 Unity 内置的 `Debug.Log` 系列方法全局注入颜色前缀。

---

## 你会学到

- **手动 `harmony.Patch()` vs `[HarmonyPatch]` Attribute**：当目标方法有多个重载时，手动 Patch 配合 `AccessTools.Method` 指定参数类型列表是更精确的选择
- **`ref object message` 参数篡改**：在 Prefix 中通过 `ref` 修改目标方法的入参，原方法收到的是被修改后的值
- **Unity Rich Text 颜色标签**：用 `<color=#RRGGBBAA>text</color>` 在 Console 中实现彩色日志

---

## 核心概念

### 为什么选这个案例？

日志是开发中最高频的操作。当项目变大后，Console 里数百行白色日志难以快速区分级别。如果能让 `Debug.Log` 带绿色前缀、`LogWarning` 带黄色前缀、`LogError` 带红色前缀，一眼就能定位问题。

传统方案是封装一个自定义 `Logger` 类，但这要求所有代码都改用新 API——第三方插件的日志无法覆盖。

Harmony 的方案是**直接 Patch `Debug.Log` 本身**，全局生效，零侵入。

### 手动 Patch 的必要性

`Debug.Log` 有两个重载：

```csharp
public static void Log(object message);
public static void Log(object message, Object context);
```

如果用 `[HarmonyPatch(typeof(Debug), "Log")]`，Harmony 无法确定你要 Patch 哪个重载。手动调用 `harmony.Patch()` 并通过参数类型数组精确匹配：

```csharp
// 匹配 Debug.Log(object)
AccessTools.Method(typeof(Debug), nameof(Debug.Log), new[] { typeof(object) })

// 匹配 Debug.Log(object, Object)
AccessTools.Method(typeof(Debug), nameof(Debug.Log), new[] { typeof(object), typeof(Object) })
```

### `ref` 参数篡改

Prefix 的参数名必须与目标方法的参数名一致。`Debug.Log(object message)` 的参数名是 `message`，所以 Prefix 签名是：

```csharp
public static bool Prefix(ref object message)
{
    message = $"[前缀] {message}";  // 修改参数
    return true;                     // 继续执行原方法
}
```

加了 `ref` 后，对 `message` 的修改会**反映到原方法调用**中。原方法执行时收到的就是带前缀的字符串。

---

## 关键代码

### 颜色工具类

```csharp
using UnityEngine;

public static class Debugger
{
    private static readonly string kGreenColor  = ColorUtility.ToHtmlStringRGBA(Color.green);
    private static readonly string kCyanColor   = ColorUtility.ToHtmlStringRGBA(Color.cyan);
    private static readonly string kYellowColor = ColorUtility.ToHtmlStringRGBA(Color.yellow);
    private static readonly string kRedColor    = ColorUtility.ToHtmlStringRGBA(Color.red);

    private static string ColorLog(string str, string color)
    {
#if UNITY_EDITOR
        return $"<color=#{color}>{str}</color>";
#else
        return str; // 非编辑器环境不加颜色标签
#endif
    }

    public static string GreenLog(string str)  => ColorLog(str, kGreenColor);
    public static string CyanLog(string str)   => ColorLog(str, kCyanColor);
    public static string YellowLog(string str) => ColorLog(str, kYellowColor);
    public static string RedLog(string str)    => ColorLog(str, kRedColor);
}
```

要点：
- `ColorUtility.ToHtmlStringRGBA` 将 `Color` 转为 `RRGGBBAA` 格式 Hex 字符串
- `#if UNITY_EDITOR` 条件编译：真机或 Logcat 中不输出 HTML 标签

### 补丁类：6 个 Prefix 覆盖 3 个方法 × 2 个重载

```csharp
using HarmonyLib;
using UnityEngine;

public static class DebugReplacePatch
{
    private static class LogPatch
    {
        public static bool Prefix(ref object message)
        {
            message = $"{Debugger.GreenLog("D")} {message}";
            return true;
        }

        public static bool PrefixContext(ref object message, Object context)
        {
            message = $"{Debugger.GreenLog("D")} {message}";
            return true;
        }
    }

    private static class WarningPatch
    {
        public static bool Prefix(ref object message)
        {
            message = $"{Debugger.YellowLog("W")} {message}";
            return true;
        }

        public static bool PrefixContext(ref object message, Object context)
        {
            message = $"{Debugger.YellowLog("W")} {message}";
            return true;
        }
    }

    private static class ErrorPatch
    {
        public static bool Prefix(ref object message)
        {
            message = $"{Debugger.RedLog("E")} {message}";
            return true;
        }

        public static bool PrefixContext(ref object message, Object context)
        {
            message = $"{Debugger.RedLog("E")} {message}";
            return true;
        }
    }

    public static void ApplyPatches(Harmony harmony)
    {
        // Debug.Log — 2 个重载
        harmony.Patch(
            AccessTools.Method(typeof(Debug), nameof(Debug.Log),
                new[] { typeof(object) }),
            new HarmonyMethod(AccessTools.Method(
                typeof(LogPatch), nameof(LogPatch.Prefix))));
        harmony.Patch(
            AccessTools.Method(typeof(Debug), nameof(Debug.Log),
                new[] { typeof(object), typeof(Object) }),
            new HarmonyMethod(AccessTools.Method(
                typeof(LogPatch), nameof(LogPatch.PrefixContext))));

        // Debug.LogWarning — 2 个重载
        harmony.Patch(
            AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning),
                new[] { typeof(object) }),
            new HarmonyMethod(AccessTools.Method(
                typeof(WarningPatch), nameof(WarningPatch.Prefix))));
        harmony.Patch(
            AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning),
                new[] { typeof(object), typeof(Object) }),
            new HarmonyMethod(AccessTools.Method(
                typeof(WarningPatch), nameof(WarningPatch.PrefixContext))));

        // Debug.LogError — 2 个重载
        harmony.Patch(
            AccessTools.Method(typeof(Debug), nameof(Debug.LogError),
                new[] { typeof(object) }),
            new HarmonyMethod(AccessTools.Method(
                typeof(ErrorPatch), nameof(ErrorPatch.Prefix))));
        harmony.Patch(
            AccessTools.Method(typeof(Debug), nameof(Debug.LogError),
                new[] { typeof(object), typeof(Object) }),
            new HarmonyMethod(AccessTools.Method(
                typeof(ErrorPatch), nameof(ErrorPatch.PrefixContext))));
    }
}
```

### 启动入口

```csharp
public class Chapter5Demo : MonoBehaviour
{
    private Harmony _harmony;

    void Awake()
    {
        _harmony = new Harmony("showcase.chapter5");
        DebugReplacePatch.ApplyPatches(_harmony);

        // 验证效果
        Debug.Log("普通日志 — 绿色 D 前缀");
        Debug.LogWarning("警告日志 — 黄色 W 前缀");
        Debug.LogError("错误日志 — 红色 E 前缀");
    }

    void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }
}
```

---

## 设计解读

### 为什么 Prefix 返回 `true`？

这里的目的是**修改参数后继续执行原方法**，而非替换或阻止。返回 `true` 意味着原方法正常执行，只是它收到的 `message` 已经被我们篡改过了。

### 为什么 `PrefixContext` 忽略了 `context` 参数？

`Debug.Log(object message, Object context)` 的第二个参数 `context` 用于在 Console 中点击日志时高亮关联的 GameObject。我们只需要修改 `message`，`context` 原样透传给原方法即可。Prefix 的参数只要**声明**了就会被 Harmony 注入，不声明的参数不受影响。

### 为什么不用 `[HarmonyPatch]` Attribute？

三个原因：
1. `Debug.Log` 有多个同名重载，Attribute 无法区分
2. 每个重载需要对应不同签名的 Prefix 方法（有无 `context` 参数）
3. 手动 `Patch` 让 6 个补丁注册集中在一个 `ApplyPatches` 方法中，便于统一管理

### 全局生效的含义

补丁应用后，**项目中所有代码**（包括第三方插件）调用 `Debug.Log` / `LogWarning` / `LogError` 时都会带上颜色前缀。这就是 Harmony 运行时补丁的威力——无需修改任何现有代码。

---

## 在 Unity 中运行

1. 打开 `Assets/Scenes/Chapter5.unity`。

2. 进入 Play Mode，Console 输出：

   ```
   D 普通日志 — 绿色 D 前缀
   W 警告日志 — 黄色 W 前缀
   E 错误日志 — 红色 E 前缀
   D Chapter 5 通关：Debug.Log 颜色前缀已激活
   ```

   其中 `D` 为绿色、`W` 为黄色、`E` 为红色（在 Unity Console 中显示为彩色）。

3. 注意最后一行 `Chapter 5 通关` 本身也带上了绿色 `D` 前缀——因为它也是通过 `Debug.Log` 输出的，补丁已全局生效。

---

## 恭喜番外全部通关！

你已完成 HarmonyX 番外篇的全部 5 章。回顾一下学到的所有技术：

| 章节 | 核心技术 | 典型用途 |
|------|----------|----------|
| 第 0 章 — 入门 | `Harmony` 实例、`PatchAll`、`UnpatchAll` | 环境搭建，理解 IL 补丁原理 |
| 第 1 章 — Prefix/Postfix | `bool Prefix`、`void Postfix`、多补丁链式执行 | 拦截、观察、条件跳过 |
| 第 2 章 — `__state` | `ref __state`、跨钩子状态传递、线程安全 | 性能计时、上下文传递 |
| 第 3 章 — `__result` | `ref __result`、返回值替换 | Mock 测试、A/B 测试、Bug 紧急修复 |
| 第 4 章 — 动态扫描 | `[AutoPatch]`、反射扫描、批量注册 | 大型项目补丁管理 |
| 第 5 章 — Debug.Log 着色 | 手动 `Patch`、`AccessTools`、`ref` 参数篡改 | 全局日志增强、无侵入式改造 |

### Roslyn + Harmony 组合使用

两套工具互补，覆盖了代码修改的完整时间线：

```
源代码编写阶段  → Roslyn Analyzer 实时检查，给出 Diagnostic
编译阶段       → Roslyn Generator 生成样板代码
运行时          → Harmony 补丁修改无法在编译期触及的行为
```

**感谢你完成整个展示项目！** 现在你已经掌握了 Roslyn 增量生成器（8 关）和 HarmonyX 运行时补丁（5 章）的核心知识，可以把这些技术运用到你的实际 Unity 项目中了。
