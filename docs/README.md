# Roslyn 演示项目 + Harmony 番外：知识地图

本项目通过逐级演示，带你从 Roslyn 零基础到 Boss 关，配套 Harmony 运行时补丁番外。

## 快速开始

```bash
# 1. 构建所有生成器（DLL 自动复制到 Unity）
cd showcase/roslyn
dotnet build showcase.sln

# 2. 用 Unity 6000.0.x 打开 showcase/game/
# 3. 无编译错误 → 运行各 Level 的 Scene
```

---

## Roslyn 知识进阶地图

```
L0: 完整骨架（Logger）
    IIncrementalGenerator, CreateSyntaxProvider,
    SemanticModel.GetSymbolInfo, ContainingType.ToDisplayString
 ↓ 新增：生成器自给自足注入 Attribute 定义
L1: RegisterPostInitializationOutput
    [Hello] → HelloMessage 属性（最小可运行生成器）
 ↓ 新增：扫描字段而非类
L2: FieldDeclarationSyntax
    [AutoProperty] 字段 → PascalCase 属性
 ↓ 新增：语义模型跨文件类型解析
L3: SemanticModel.GetTypeInfo + INamedTypeSymbol.GetMembers()
    [AutoImplement(typeof(IInterface))] → 接口方法骨架
 ↓ 新增：Scriban 模板 + 多节点聚合生成单文件
L4: Scriban + .Collect()
    所有 [AutoService] 类 → 聚合 Service 单例类
 ↓ 新增：InterfaceDeclaration + FNV-1a 哈希 ID + 碰撞诊断
L5: InterfaceDeclarationSyntax + Diagnostic
    [EventInterface] 接口 → int 常量 + 分发器 + Helper
 ↓ 新增：只读 Analyzer + 自动代码修复
L6: DiagnosticAnalyzer + CodeFixProvider
    检查事件调用规范，提供 Quick Fix
 ↓ 新增：方法级 Attribute + 双 Generator 协作
L7: [Boss] MethodDeclarationSyntax + 双 Generator
    [Event]/[Tool] 方法 → SchemaGen + ModuleCodeGen 两阶段
```

---

## Roslyn 章节索引

| 章节 | 文档 | 核心 API | 演示脚本 |
|------|------|---------|---------|
| 背景知识 | [00-what-is-roslyn.md](roslyn/00-what-is-roslyn.md) | — | — |
| Level 0 | [01-level0-logger.md](roslyn/01-level0-logger.md) | `IIncrementalGenerator` | `Level0Demo.cs` |
| Level 1 | [02-level1-hello.md](roslyn/02-level1-hello.md) | `RegisterPostInitializationOutput` | `Level1Demo.cs` |
| Level 2 | [03-level2-auto-property.md](roslyn/03-level2-auto-property.md) | `FieldDeclarationSyntax` | `Level2Demo.cs` |
| Level 3 | [04-level3-auto-implement.md](roslyn/04-level3-auto-implement.md) | `GetTypeInfo` + `GetMembers` | `Level3Demo.cs` |
| Level 4 | [05-level4-auto-service.md](roslyn/05-level4-auto-service.md) | Scriban + `.Collect()` | `Level4Demo.cs` |
| Level 5 | [06-level5-event-interface.md](roslyn/06-level5-event-interface.md) | `InterfaceDeclarationSyntax` | `Level5Demo.cs` |
| Level 6 | [07-level6-event-analyzer.md](roslyn/07-level6-event-analyzer.md) | `DiagnosticAnalyzer` | `Level6Demo.cs` |
| Level 7 | [08-level7-module-mesh.md](roslyn/08-level7-module-mesh.md) | `MethodDeclarationSyntax` + 双 Generator | `Level7Demo.cs` |
| **Unity 实践** | [09-unity-integration.md](roslyn/09-unity-integration.md) | 完整工程实践 | — |

---

## Harmony 番外章节索引

| 章节 | 文档 | 核心概念 | 演示脚本 |
|------|------|---------|---------|
| 背景知识 | [00-what-is-harmony.md](harmony/00-what-is-harmony.md) | — | — |
| Chapter 1 | [01-chapter1-prefix-postfix.md](harmony/01-chapter1-prefix-postfix.md) | Prefix/Postfix | `Chapter1Demo.cs` |
| Chapter 2 | [02-chapter2-state.md](harmony/02-chapter2-state.md) | `__state` 状态传递 | `Chapter2Demo.cs` |
| Chapter 3 | [03-chapter3-result.md](harmony/03-chapter3-result.md) | `ref __result` 修改返回值 | `Chapter3Demo.cs` |
| Chapter 4 | [04-chapter4-dynamic-scan.md](harmony/04-chapter4-dynamic-scan.md) | 动态扫描 + 自定义 Attribute | `Chapter4Demo.cs` |

---

## 项目结构

```
showcase/
├── roslyn/           # C# 解决方案（dotnet build 生成 DLL）
│   ├── showcase.sln
│   ├── Core/         # 共享 Attribute 定义（netstandard2.0）
│   ├── Level0.Logger ~ Level7.ModuleMesh/
│   └── Tests/
├── game/             # Unity 项目（6000.0.x）
│   └── Assets/
│       ├── Generators/   # 各 Level 的 DLL + asmdef 壳
│       ├── Showcase/     # 演示 MonoBehaviour 脚本
│       ├── Harmony/      # Harmony 番外演示脚本
│       └── Plugins/      # 0Harmony.dll 等第三方依赖
└── docs/             # 本文档目录
```

---

## Unity DLL 接入链路

```
dotnet build showcase.sln
    ↓ PostBuild
    DLL 复制到 game/Assets/Generators/LevelX/
    ↓ Unity 读取 DLL.meta（labels: RoslynAnalyzer）
    ↓ 编译期调用生成器
    ↓ 生成的代码在 IDE 中可见（Rider: Alt+Shift+G 查看）
```

> **提示**：每次修改生成器代码后，需要重新 `dotnet build` 并重启 Unity（或等待 Unity 自动重新导入）。
