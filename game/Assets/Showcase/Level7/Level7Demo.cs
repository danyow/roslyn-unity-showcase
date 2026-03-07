using Showcase.Core.ModuleMesh;
using Showcase.Generated.ModuleMesh;
using UnityEngine;

namespace Showcase.Level7
{
    /// <summary>
    /// Level 7 — ModuleMesh Boss 关演示
    /// [Event] 和 [Tool] 由两个生成器协作处理：
    ///   SchemaGenerator → 提取信息生成 ModuleMeshSchema
    ///   ModuleCodeGenerator → 生成 ModuleMeshRegistry 运行时胶水代码
    /// </summary>
    public class GameModule
    {
        // 方法级 Attribute：比类级更细粒度的描述
        [Event("player.spawned", typeof(string))]
        public void OnPlayerSpawned(string playerName) =>
            UnityEngine.Debug.Log($"[GameModule] 玩家出生: {playerName}");

        [Tool("calculate.damage", typeof(float), typeof(float))]
        public float CalculateDamage(float baseDamage) => baseDamage * 1.5f;
    }

    public class Level7Demo : MonoBehaviour
    {
        private void Start()
        {
            var module = new GameModule();

            // 生成的注册表：将 GameModule 的 [Event] 方法注册到 ModuleMeshRegistry
            ModuleMeshRegistry.RegisterGameModule(module);

            // 通过注册表触发事件（解耦！调用方不需要知道 GameModule）
            ModuleMeshRegistry.Emit("player.spawned", "Player1");

            // 调用工具
            var damage = (float)ModuleMeshRegistry.Call("calculate.damage", 100f)!;
            UnityEngine.Debug.Log($"计算伤害: {damage}");

            UnityEngine.Debug.Log("✓ Level 7 通关：ModuleMesh 双 Generator 协作运行正常");
        }
    }
}
