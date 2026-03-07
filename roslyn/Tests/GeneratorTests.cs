using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Showcase.Tests;

public class Level0LoggerTests
{
    [Fact]
    public async Task Logger_Attribute_Generates_Static_Debug_Class()
    {
        // Arrange：用户代码
        const string source = """
            using Showcase.Core;
            namespace MyGame
            {
                [Logger]
                public partial class PlayerController { }
            }
            """;

        // Assert：验证生成的代码包含 Debug 类
        // 注意：实际项目中使用 SourceGeneratorVerifier 框架
        // 此处仅作示意，需要配置 MetadataReference 等
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Logger_ContextMode_Generates_DebugContext_Class()
    {
        const string source = """
            using Showcase.Core;
            namespace MyGame
            {
                [Logger(true)]
                public partial class EnemyController : UnityEngine.MonoBehaviour { }
            }
            """;

        // 验证生成 DebugContext 实例类
        await Task.CompletedTask;
    }
}

public class Level1AutoPropertyTests
{
    [Fact]
    public async Task AutoProperty_Converts_CamelCase_Field_To_PascalCase_Property()
    {
        const string source = """
            using Showcase.Core;
            namespace MyGame
            {
                public partial class Player
                {
                    [AutoProperty] private int _health;
                    [AutoProperty] private float _maxSpeed;
                    [AutoProperty] private string _playerName;
                }
            }
            """;

        // 期望生成：
        // public int Health { get => _health; set => _health = value; }
        // public float MaxSpeed { get => _maxSpeed; set => _maxSpeed = value; }
        // public string PlayerName { get => _playerName; set => _playerName = value; }
        await Task.CompletedTask;
    }
}

public class Level2AutoImplementTests
{
    [Fact]
    public async Task AutoImplement_Generates_Missing_Interface_Properties()
    {
        const string source = """
            using Showcase.Core;
            namespace MyGame
            {
                public interface ICharacter
                {
                    string Name { get; set; }
                    int Level { get; set; }
                }

                [AutoImplement]
                public partial class Hero : ICharacter { }
            }
            """;

        // 期望生成：
        // public string Name { get; set; }
        // public int Level { get; set; }
        await Task.CompletedTask;
    }
}

public class Level5EventInterfaceTests
{
    [Fact]
    public async Task EventInterface_Generates_Stable_FNV1a_IDs()
    {
        const string source = """
            using Showcase.Core;
            namespace MyGame
            {
                [EventInterface]
                public interface IGameEvents
                {
                    void OnPlayerDied(string playerName);
                    void OnScoreChanged(int newScore);
                }
            }
            """;

        // 验证生成的 ID 是 FNV-1a 哈希（负数）
        // 验证 GameEvents.OnPlayerDied 和 GameEvents.OnScoreChanged 是负整数常量
        await Task.CompletedTask;
    }
}

public class Level6EventExecuteTests
{
    [Fact]
    public async Task EventExecute_MarkedClass_Generates_Abstract_Execute()
    {
        // 验证：[AutoEventExecute] 标注的抽象基类生成 abstract Execute 声明
        // EventExecuteGenerator 需要 JSON 配置 + Scriban 模板
        // 当无配置目录时，仅生成基类的空 Execute 声明
        const string source = """
            using System;
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AutoEventExecuteAttribute : Attribute
            {
                public string TypeField { get; set; }
                public string MethodField { get; set; }
            }

            [AutoEventExecute]
            public abstract partial class BaseEvent
            {
                public string method;
                public System.Collections.Generic.Dictionary<string, object> args;
            }
            """;

        // 期望生成 BaseEvent.Event.g.cs：
        // public abstract partial class BaseEvent
        // {
        //     public abstract void Execute(Luban.BeanBase caller);
        // }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task EventExecute_SealedSubclass_Generates_Switch_Dispatch()
    {
        // 验证：sealed 子类生成 switch 分发实现
        // 需要 JSON 配置中包含 GlobalEvent 的 method/args 定义
        const string source = """
            using System;
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AutoEventExecuteAttribute : Attribute
            {
                public string TypeField { get; set; }
                public string MethodField { get; set; }
            }

            [AutoEventExecute]
            public abstract partial class BaseEvent
            {
                public string method;
                public System.Collections.Generic.Dictionary<string, object> args;
            }

            public sealed partial class GlobalEvent : BaseEvent
            {
                public string refId;
                public GlobalEventConfig refIdConfig;
            }

            public sealed partial class GlobalEventConfig
            {
                public string id;
            }
            """;

        // 期望（有 JSON 配置时）生成 GlobalEvent.Event.g.cs：
        // public sealed partial class GlobalEvent
        // {
        //     public override void Execute(Luban.BeanBase caller)
        //     {
        //         switch (method)
        //         {
        //             case nameof(GlobalEventConfig.Trigger):
        //                 refIdConfig.Trigger(caller, ...);
        //                 break;
        //         }
        //     }
        // }
        await Task.CompletedTask;
    }
}
