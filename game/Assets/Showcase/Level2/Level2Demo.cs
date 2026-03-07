using Showcase.Core;
using UnityEngine;

namespace Showcase.Level2
{
    public interface ICharacter
    {
        string Name { get; set; }
        int Level { get; set; }
        float Health { get; set; }
    }

    // [AutoImplement] 标注在 partial class 上
    // AutoImplementGenerator 扫描所有实现的接口，自动生成缺失的属性 { get; set; }
    [AutoImplement]
    public partial class Level2Demo : MonoBehaviour, ICharacter
    {
        private void Start()
        {
            // 这些属性由 AutoImplementGenerator 自动生成
            Name = "Hero";
            Level = 1;
            Health = 100f;

            UnityEngine.Debug.Log($"角色: {Name}, 等级: {Level}, 生命: {Health}");
            UnityEngine.Debug.Log("✓ Level 2 通关：AutoImplementGenerator 自动生成接口属性");
        }
    }
}
