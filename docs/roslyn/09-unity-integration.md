# 09 — Roslyn Source Generator 在 Unity 中的完整实践

> 从零搭建 → DLL 接入 → 双模式切换 → 8 级生成器实战，一篇覆盖 Roslyn 在 Unity 中的全部工程细节。

---

## 为什么在 Unity 里用 Source Generator？

Unity 开发中存在大量**样板代码**：属性封装、服务注册、事件分发、序列化胶水……这些代码模式固定、手写费时、维护成本高。

传统方案各有局限：

| 方案 | 问题 |
|------|------|
| 手写 | 重复劳动，容易遗漏 |
| 反射 | 运行时开销，IL2CPP 裁剪风险 |
| T4 模板 | 需要手动触发，Unity 不原生支持 |
| 编辑器脚本生成 | 依赖 UnityEditor，CI 环境受限 |

**Roslyn Source Generator** 在编译期运行，生成的代码与手写代码完全等价——零运行时开销、类型安全、IDE 可见。Unity 从 2022.2+ 版本开始支持 `IIncrementalGenerator`，这让编译期代码生成成为现实。

---

## 项目架构总览

```
showcase/
├── roslyn/                         # C# 解决方案（外部 dotnet 项目）
│   ├── showcase.sln
│   ├── Core/                       # 共享 Attribute 定义（netstandard2.0）
│   ├── SourceGenerator.Infrastructure/  # 双模式基础设施
│   ├── Level0.Logger/              # 生成器项目 ×8
│   ├── Level1.AutoProperty/
│   ├── ...
│   ├── Level7.ModuleMesh/
│   └── Tests/                      # dotnet 测试项目
├── game/                           # Unity 项目（6000.0.x）
│   └── Assets/
│       ├── Generators/LevelX/      # 生成器 DLL（RoslynAnalyzer 标签）
│       ├── Showcase/LevelX/        # 演示脚本
│       ├── csc.rsp                 # 编译器响应文件（模式切换）
│       └── showcase.analyzerconfig # 分析器配置
└── docs/                           # 文档
```

核心思想：**生成器代码在外部 dotnet 项目中开发和调试，编译后的 DLL 复制到 Unity 项目中使用**。

---

## 第一步：搭建生成器项目

### csproj 模板

每个生成器项目都遵循相同的 csproj 模板：

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
        <IsRoslynComponent>true</IsRoslynComponent>
        <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
        <LangVersion>latest</LangVersion>
        <Nullable>enable</Nullable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.0" />
        <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
    </ItemGroup>
</Project>
```

四个关键点：

| 属性 | 说明 |
|------|------|
| `netstandard2.0` | Roslyn 分析器必须 target netstandard2.0，Unity 的 Roslyn 运行时要求 |
| `IsRoslynComponent` | 告诉 MSBuild 这是一个 Roslyn 组件，启用分析器打包规则 |
| `EnforceExtendedAnalyzerRules` | 强制执行 Roslyn 分析器编码规范（如不允许引用不安全的 API） |
| `CSharp 4.3.0` | 建议使用 4.3.0 以兼容 Unity 2022.2+ 内置的 Roslyn 版本 |

### 内嵌依赖而非 ProjectReference

Roslyn 分析器在编译器进程中运行，**无法自动加载外部 DLL 依赖**。如果生成器 A 引用了类库 B（通过 `ProjectReference`），Unity 只会加载 A.dll，B.dll 找不到，生成器初始化失败。

解决方案——**源码内嵌**：

```xml
<!-- 不要这样做 -->
<ProjectReference Include="..\Core\Core.csproj" />

<!-- 正确做法：把源码直接编译进生成器 DLL -->
<ItemGroup>
    <Compile Include="..\Core\Attributes\LoggerAttribute.cs">
        <Link>Core\LoggerAttribute.cs</Link>
    </Compile>
    <Compile Include="..\SourceGenerator.Infrastructure\GeneratorConfig.cs">
        <Link>Infrastructure\GeneratorConfig.cs</Link>
    </Compile>
    <Compile Include="..\SourceGenerator.Infrastructure\FileWriter.cs">
        <Link>Infrastructure\FileWriter.cs</Link>
    </Compile>
</ItemGroup>
```

最终编译出的单个 DLL 包含了所有需要的类型定义。

### 第三方 NuGet 包的处理

如果生成器使用了第三方 NuGet 包（如 Scriban），需要**把依赖 DLL 一起复制到 Unity**，并且**都标记为 RoslynAnalyzer**：

```xml
<!-- Level6.EventExecute.csproj -->
<PackageReference Include="Scriban" Version="5.12.0"
                  PrivateAssets="all" GeneratePathProperty="true">
    <IncludeAssets>build; compile</IncludeAssets>
</PackageReference>

<Target Name="PostBuild" AfterTargets="PostBuildEvent">
    <!-- 复制生成器 DLL -->
    <Copy SourceFiles="$(TargetDir)$(AssemblyName).dll"
          DestinationFolder="$(ProjectDir)../../game/Assets/Generators/Level6.EventExecute/" />
    <!-- 复制依赖 DLL -->
    <Copy SourceFiles="$(PkgScriban)/lib/netstandard2.0/Scriban.dll"
          DestinationFolder="$(ProjectDir)../../game/Assets/Generators/Level6.EventExecute/"
          SkipUnchangedFiles="true" />
</Target>
```

`GeneratePathProperty="true"` 会生成 `$(PkgScriban)` 变量，指向 NuGet 缓存中的包路径。

---

## 第二步：接入 Unity

### DLL 复制与 PostBuild

每个生成器项目通过 PostBuild 自动复制 DLL 到 Unity：

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
    <Copy SourceFiles="$(TargetDir)$(AssemblyName).dll"
          DestinationFolder="$(ProjectDir)../../game/Assets/Generators/Level0.Logger/"
          ContinueOnError="false" />
</Target>
```

执行 `dotnet build showcase.sln` 后，所有 DLL 自动就位。

### .dll.meta 文件（核心）

Unity 通过 `.dll.meta` 文件识别 Roslyn 分析器。必须满足两个条件：

1. **`labels` 包含 `RoslynAnalyzer`** — 告诉 Unity 这是分析器/生成器
2. **所有平台 `enabled: 0`** — 不作为运行时插件加载

```yaml
fileFormatVersion: 2
guid: d1000001000000000000000000000000
labels:
- RoslynAnalyzer
PluginImporter:
  serializedVersion: 2
  platformData:
  - first:
      '': Any
    second:
      enabled: 0
      settings: {}
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
```

> 手动创建 `.meta` 文件并提交到版本控制。`guid` 需要唯一，可以用随机值。

### 目录结构

```
game/Assets/Generators/
├── Level0.Logger/
│   ├── Showcase.Level0.Logger.dll          # 生成器 DLL
│   └── Showcase.Level0.Logger.dll.meta     # RoslynAnalyzer 标签
├── Level6.EventExecute/
│   ├── Showcase.Level6.EventExecute.dll    # 生成器 DLL
│   ├── Showcase.Level6.EventExecute.dll.meta
│   ├── Scriban.dll                         # 第三方依赖
│   ├── Scriban.dll.meta                    # 也要标记 RoslynAnalyzer
│   ├── System.Text.Json.dll
│   └── System.Text.Json.dll.meta
└── ...
```

### 验证接入成功

1. `dotnet build showcase.sln` — 所有 DLL 自动复制
2. 打开 Unity → 等待编译完成 → Console 无编译错误
3. 在 Rider/VS 中展开 **Analyzers** 节点，应该看到各生成器的输出文件

---

## 第三步：双模式——AddSource vs WriteFile

### 两种模式的区别

| | AddSource（内存注入） | WriteFile（物理文件） |
|--|--|--|
| **原理** | `context.AddSource()` 注入编译管道 | `File.WriteAllText()` 写入磁盘 |
| **输出位置** | `obj/Generated/` 或编译器内存 | 与源文件同目录（`.g.cs`） |
| **可见性** | IDE Analyzers 树中可见 | 文件系统中可见，可直接打开 |
| **适用场景** | 测试、CI、不需要查看生成代码 | 开发阶段、需要审查生成代码 |
| **版本控制** | 不产生文件变更 | 需要 `.gitignore` 排除 `.g.cs` |

### 实现原理

所有生成器通过 `build_property.GenerateMode` 读取当前模式：

```csharp
// 读取配置
var configProvider = context.AnalyzerConfigOptionsProvider.Select(
    (provider, _) =>
    {
        provider.GlobalOptions.TryGetValue("build_property.GenerateMode", out var mode);
        return GeneratorConfig.ParseMode(mode);
    }
);

// 分支处理
if (mode == GenerateMode.WriteFile)
{
    FileWriter.WriteToPhysicalFile(context, sourceFilePath, fileName, source, "TAG");
}
else
{
    context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
}
```

`ParseMode` 的逻辑：
- `"AddSource"` → AddSource 模式
- `"WriteFile"` 或 `null`/空 → **默认 WriteFile 模式**

### 在 dotnet build 中切换模式

dotnet 项目通过 MSBuild 属性传递：

```xml
<!-- Tests.csproj -->
<PropertyGroup>
    <GenerateMode>AddSource</GenerateMode>
</PropertyGroup>
<ItemGroup>
    <CompilerVisibleProperty Include="GenerateMode" />
</ItemGroup>
```

`CompilerVisibleProperty` 将 MSBuild 属性暴露为 `build_property.GenerateMode`，生成器通过 `AnalyzerConfigOptionsProvider.GlobalOptions` 读取。

### 在 Unity 中切换模式

Unity **不读取** `.globalconfig` 文件，也不使用 MSBuild 系统。传递 `build_property.*` 给 Roslyn 编译器的方法是通过 **`csc.rsp`**：

**`Assets/csc.rsp`**（Unity 自动读取的编译器响应文件）：
```
-analyzerconfig:Assets/showcase.analyzerconfig
```

**`Assets/showcase.analyzerconfig`**：
```ini
is_global = true
build_property.GenerateMode = AddSource
```

`-analyzerconfig:` 是标准的 Roslyn 编译器参数，显式告诉编译器读取指定的分析器配置文件。这不依赖 `.globalconfig` 的自动发现机制。

**切换方式：**

| 目标模式 | 操作 |
|---------|------|
| WriteFile | 删除或清空 `csc.rsp` 内容，重新打开 Unity |
| AddSource | 在 `csc.rsp` 中添加 `-analyzerconfig:` 行，删除已有的 `.g.cs` 文件 |

> WriteFile 模式下生成的 `.g.cs` 文件内容变化时才会写入，避免触发无限编译循环。

---

## 第四步：FileWriter——安全的物理文件写入

WriteFile 模式下，`FileWriter.WriteToPhysicalFile()` 负责写入物理文件：

```csharp
public static void WriteToPhysicalFile(
    SourceProductionContext context,
    string sourceFilePath,    // 触发生成的源文件路径
    string fileName,          // 输出文件名（如 ClassName.g.cs）
    string source,            // 生成的代码内容
    string diagnosticIdPrefix = "SRCGEN")
{
    var directory = Path.GetDirectoryName(sourceFilePath);
    var outputPath = Path.Combine(directory, fileName);

    // 关键优化：仅当内容改变时才写入（避免触发重复编译）
    if (File.Exists(outputPath))
    {
        var existingContent = File.ReadAllText(outputPath, Encoding.UTF8);
        if (existingContent == source)
            return;  // 内容相同，跳过
    }

    File.WriteAllText(outputPath, source, Encoding.UTF8);
}
```

两个设计要点：
1. **增量写入**：比较现有内容，相同则跳过——防止 Unity 因文件变更触发无限编译
2. **`RS1035` 警告**：Roslyn 默认禁止分析器使用 `File` API。需要在 csproj 中 `<NoWarn>$(NoWarn);RS1035</NoWarn>`

---

## 8 个生成器速览

本项目包含 8 个由浅入深的生成器，覆盖了 Roslyn 在 Unity 中的主要应用场景：

### Level 0 — Logger：完整骨架

```csharp
[Logger]
public partial class Level0Demo : MonoBehaviour
{
    void Start()
    {
        Debug.Log("带前缀标签的日志");  // 生成的内嵌 Debug 类
    }
}
```

**核心 API**：`IIncrementalGenerator`、`CreateSyntaxProvider`、`SemanticModel.GetSymbolInfo`

扫描 `[Logger]` 标注的类，生成内嵌的 `private static class Debug`，所有日志自动带上 `[ClassName]` 前缀。这是最基础的生成器骨架，展示了 predicate（语法粗筛）→ transform（语义精确验证）的两步过滤模式。

---

### Level 1 — Hello：自给自足的 Attribute 注入

```csharp
[Hello]  // Attribute 由生成器自己注入，不需要外部引用
public partial class Level1Demo : MonoBehaviour { }
// 生成 → public string HelloMessage => "Hello from Level1Demo!";
```

**核心 API**：`RegisterPostInitializationOutput`

解决"先有鸡还是先有蛋"问题——`[Hello]` 特性的定义本身就是由生成器在编译最早阶段注入的，消费项目无需引用任何额外的包。

---

### Level 2 — AutoProperty：字段级扫描

```csharp
public partial class Level2Demo : MonoBehaviour
{
    [AutoProperty] private int _health = 100;
    [AutoProperty] private float _speed = 5f;
    // 生成 → public int Health { get => _health; set => _health = value; }
    // 生成 → public float Speed { get => _speed; set => _speed = value; }
}
```

**核心 API**：`FieldDeclarationSyntax`

从类级别下沉到字段级别扫描，处理 `private T _camelCase` → `public T PascalCase` 的属性自动生成。需要处理 `int _a, _b;` 一行多变量的情况。

---

### Level 3 — MathType：组合爆炸式代码生成

```csharp
[MathSetter(typeof(Vector3), typeof(float), axis: 3)]
[MathGetter(typeof(Vector3), axis: 3)]
public static partial class Vector3Ext { }
// 生成 → SetX, SetXY, SetXYZ, AddX, SubX, MulX, DivX...
// 生成 → GetXY, GetXZ, GetYZ...
```

**核心 API**：`typeof()` 表达式解析、`SemanticModel.GetTypeInfo`

两个协作生成器（Setter + Getter）为向量类型生成所有分量组合的数学操作方法。展示了参数化生成的能力——通过 `axis` 参数控制生成的组合数。

---

### Level 4 — AutoService：聚合生成 + Scriban 模板

```csharp
[AutoService] public class AudioService { ... }
[AutoService] public class InputService { ... }
// 聚合生成 → public static class Service
//              { public static AudioService AudioService { get; } ... }
```

**核心 API**：`.Collect()`、Scriban 模板引擎、`EmbeddedResource`

第一个使用 `.Collect()` 的生成器——扫描所有 `[AutoService]` 类后**一次性**生成单个 `Service.g.cs` 文件。代码结构用 Scriban 模板描述，比 `StringBuilder` 拼接更直观。

---

### Level 5 — EventInterface：哈希 ID + 诊断报告

```csharp
[EventInterface]
public interface IGameEvents
{
    void OnPlayerDied(string playerName);
    void OnScoreChanged(int newScore);
}
// 生成 → GameEvents.OnPlayerDied = -1234567890 (FNV-1a 哈希)
// 生成 → GameEventsDispatcher.Dispatch(eventId, arg)
// 生成 → GameEventsHelper.OnPlayerDied(playerName)
```

**核心 API**：`InterfaceDeclarationSyntax`、FNV-1a 哈希、`ReportDiagnostic`

为每个接口方法生成稳定的负整数 ID（FNV-1a 32 位哈希），同时生成分发器和便捷 Helper。如果两个方法产生哈希碰撞，通过 `ReportDiagnostic` 报告编译错误。

---

### Level 6 — EventExecute：JSON 配置驱动 + 模板渲染

```csharp
[AutoEventExecute]
public abstract partial class BaseEvent { }
// 读取 JSON 配置 → 生成 abstract Execute(BeanBase caller)
// 子类 GlobalEvent → 生成 override Execute 的 switch 分发
// 配置类 GlobalEventConfig → 生成方法桩（用户实现覆盖）
```

**核心 API**：`System.Text.Json`、Scriban 模板、`File.ReadAllText`（`RS1035`）

最复杂的生成器——从 JSON 配置文件中读取方法签名，用 3 个 Scriban 模板生成抽象声明、具体分发和配置桩。展示了生成器如何与外部数据源交互。

---

### Level 7 — ModuleMesh：双生成器协作（Boss 关卡）

```csharp
public class GameModule
{
    [Event("player.spawned", typeof(string))]
    public void OnPlayerSpawned(string name) { ... }

    [Tool("calculate.damage", typeof(float), typeof(float))]
    public float CalculateDamage(float base) => base * 1.5f;
}
// SchemaGenerator → 提取 Event/Tool 协议
// ModuleCodeGenerator → 生成 ModuleMeshRegistry 注册表
// → ModuleMeshRegistry.Emit("player.spawned", "Player1")
// → ModuleMeshRegistry.Call("calculate.damage", 100f)
```

**核心 API**：`MethodDeclarationSyntax`、双 Generator 协同、`.Collect()`

方法级别 Attribute 扫描——两个独立的生成器（SchemaGenerator 提取协议，ModuleCodeGenerator 生成胶水代码），实现跨模块的 Event/Tool 双通道解耦通信。

---

## 知识进阶路线

```
Level 0  基础骨架
  │       IIncrementalGenerator + CreateSyntaxProvider + SemanticModel
  │
  ↓       新增：生成器自注入 Attribute（无外部依赖）
Level 1  RegisterPostInitializationOutput
  │
  ↓       新增：字段级扫描（非类级）
Level 2  FieldDeclarationSyntax
  │
  ↓       新增：跨文件类型解析 + 参数化生成
Level 3  SemanticModel.GetTypeInfo + typeof() + 组合爆炸
  │
  ↓       新增：多节点聚合 + 模板引擎
Level 4  .Collect() + Scriban + EmbeddedResource
  │
  ↓       新增：接口扫描 + 哈希算法 + 编译诊断
Level 5  InterfaceDeclarationSyntax + FNV-1a + ReportDiagnostic
  │
  ↓       新增：外部文件 I/O + 配置驱动
Level 6  System.Text.Json + File I/O + 多模板渲染
  │
  ↓       新增：方法级扫描 + 双生成器协同
Level 7  MethodDeclarationSyntax + 两阶段生成 + 模块解耦
```

---

## 常见踩坑与最佳实践

### 1. Unity 重新编译时机

Unity 在以下情况触发 C# 重编译：
- Assets 目录下 `.cs` 文件变化
- DLL 文件变化（包括生成器 DLL）
- `.asmdef` 文件变化

**陷阱**：WriteFile 模式下，如果生成器每次都写入相同内容，Unity 仍然会检测到文件修改时间变化而触发重编译，形成无限循环。

**解决**：`FileWriter` 在写入前比较内容，相同则跳过。

### 2. 生成器 DLL 更新后 Unity 不生效

修改生成器代码后：
1. 执行 `dotnet build`（PostBuild 自动复制 DLL）
2. 切换到 Unity 窗口
3. Unity 会自动检测 DLL 变化并重新编译

如果仍不生效，尝试：**关闭 Unity → 删除 `Library/` 目录 → 重新打开**。

### 3. `partial` 关键字不能遗漏

Source Generator 只能向现有类型**追加**成员，不能修改现有代码。目标类必须声明为 `partial`：

```csharp
// ✗ 无法接收生成的代码
public class MyClass : MonoBehaviour { }

// ✓ 正确
public partial class MyClass : MonoBehaviour { }
```

### 4. 生成器中不能引用 UnityEngine

生成器运行在 dotnet 编译器进程中，无法引用 Unity 的程序集。生成器代码中不能 `using UnityEngine;`。

生成的**输出代码**可以引用 UnityEngine（因为它会被 Unity 编译），但生成器**本身的逻辑**不能使用任何 Unity API。

### 5. Attribute 的全限定名验证

```csharp
// ✗ 不安全：用户可能自己写了同名 [Logger]
if (attr.Name.ToString() == "Logger") { ... }

// ✓ 安全：通过语义模型验证完全限定名
var symbol = context.SemanticModel.GetSymbolInfo(attr).Symbol as IMethodSymbol;
if (symbol?.ContainingType.ToDisplayString() == "Showcase.Core.LoggerAttribute") { ... }
```

### 6. netstandard2.0 的限制

生成器必须 target `netstandard2.0`，以下语法/API 不可用：

| 不可用 | 替代方案 |
|--------|---------|
| `record` 类型 | 普通 `class` + 属性 |
| `Span<T>` | `string` / `char[]` |
| `System.Text.Json`（原生） | 需要额外引用 NuGet 包 |
| `init` 属性 | `set` 属性 |
| 默认接口实现 | 抽象类 |

### 7. 调试生成器

Rider 中调试 Roslyn 生成器：

1. 在生成器代码中添加 `System.Diagnostics.Debugger.Launch()`
2. 执行 `dotnet build`
3. 调试器会弹出附加窗口

或在生成器中使用 `ReportDiagnostic` 输出诊断信息：

```csharp
context.ReportDiagnostic(Diagnostic.Create(
    new DiagnosticDescriptor("DBG001", "Debug", "Value: {0}",
        "Debug", DiagnosticSeverity.Warning, true),
    Location.None, someValue));
```

### 8. .gitignore 配置

WriteFile 模式下生成的 `.g.cs` 文件通常不需要提交到版本控制：

```gitignore
# Roslyn 生成的物理文件
*.g.cs
```

---

## 完整构建流程

```bash
# 1. 克隆项目
git clone <repo> && cd showcase

# 2. 构建所有生成器（DLL 自动复制到 Unity）
cd roslyn
dotnet build showcase.sln

# 3. 用 Unity 6000.0.x 打开 game/ 目录

# 4. 验证
#    - Console 无编译错误
#    - Rider Analyzers 树中有生成的文件
#    - 运行各 Level 的 Scene 查看效果
```

**切换 Unity 生成模式**：

```bash
# WriteFile 模式（默认）：生成器写物理 .g.cs 文件
# 清空 csc.rsp 即可（或删除该文件）

# AddSource 模式：代码注入编译管道，无物理文件
# 在 Assets/csc.rsp 中添加：
echo '-analyzerconfig:Assets/showcase.analyzerconfig' > game/Assets/csc.rsp
# 并删除已有的 .g.cs 文件
find game/Assets/Showcase -name "*.g.cs" -delete
```

---

## 延伸阅读

- [00 — 什么是 Roslyn？](00-what-is-roslyn.md) — Roslyn 编译器平台核心概念
- [01 ~ 08 — 各关卡详细教程](01-level0-logger.md) — 每个生成器的实现细节
- [Microsoft 官方文档 — Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Unity 官方文档 — Roslyn analyzers and source generators](https://docs.unity3d.com/Manual/roslyn-analyzers.html)
