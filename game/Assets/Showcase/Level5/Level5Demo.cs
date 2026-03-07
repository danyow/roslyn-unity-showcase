using Showcase.Core;
using UnityEngine;

namespace Showcase.Level5
{
    // [EventInterface] 接口 → 生成 int 常量类 + 分发器 + Helper
    [EventInterface]
    public interface IGameEvents
    {
        void OnPlayerDied(string playerName);
        void OnScoreChanged(int newScore);
        void OnLevelComplete();
    }

    public class GameEventHandler : IGameEvents
    {
        public void OnPlayerDied(string playerName) =>
            UnityEngine.Debug.Log($"[事件] 玩家死亡: {playerName}");
        public void OnScoreChanged(int newScore) =>
            UnityEngine.Debug.Log($"[事件] 分数变化: {newScore}");
        public void OnLevelComplete() =>
            UnityEngine.Debug.Log("[事件] 关卡完成!");
    }

    public class Level5Demo : MonoBehaviour
    {
        private void Start()
        {
            // 生成的事件 ID 常量（FNV-1a 哈希，负数域）
            UnityEngine.Debug.Log($"OnPlayerDied ID: {GameEvents.OnPlayerDied}");
            UnityEngine.Debug.Log($"OnScoreChanged ID: {GameEvents.OnScoreChanged}");

            // 注册处理器
            GameEventsDispatcher.Register(new GameEventHandler());

            // 通过 Helper 触发事件（无需记住 ID）
            GameEventsHelper.OnPlayerDied("Player1");
            GameEventsHelper.OnScoreChanged(1000);

            UnityEngine.Debug.Log("✓ Level 5 通关：EventInterfaceGenerator 运行正常");
        }
    }
}
