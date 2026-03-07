using Showcase.Core;
using UnityEngine;

namespace Showcase.Level1
{
    // [AutoProperty] 字段：AutoPropertyGenerator 扫描 FieldDeclarationSyntax
    // 命名约定：_camelCase → PascalCase 属性
    public partial class Level1Demo : MonoBehaviour
    {
        [AutoProperty] private int _health = 100;
        [AutoProperty] private float _moveSpeed = 5.0f;
        [AutoProperty] private string _playerName = "Player1";

        private void Start()
        {
            // 这些属性都是由 AutoPropertyGenerator 生成的，不是手写的
            UnityEngine.Debug.Log($"生命值: {Health}");       // → _health
            UnityEngine.Debug.Log($"移动速度: {MoveSpeed}");  // → _moveSpeed
            UnityEngine.Debug.Log($"玩家名: {PlayerName}");   // → _playerName

            // 属性可以 set
            Health = 80;
            UnityEngine.Debug.Log($"受伤后生命值: {Health}");

            UnityEngine.Debug.Log("✓ Level 1 通关：AutoPropertyGenerator 运行正常");
        }
    }
}
