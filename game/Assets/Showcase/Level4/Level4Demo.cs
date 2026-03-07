using Showcase.Core;
using Showcase.Generated;
using UnityEngine;

namespace Showcase.Level4
{
    // [AutoService] 标注的类会被聚合到全局 Service 类
    // AutoServiceGenerator 使用 .Collect() 收集所有 [AutoService] 类，一次性生成 Service.g.cs
    [AutoService]
    public class AudioService
    {
        public void Play(string clipName) =>
            UnityEngine.Debug.Log($"[AudioService] 播放: {clipName}");
    }

    [AutoService]
    public class InputService
    {
        public bool IsJumping() => UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space);
    }

    public class Level4Demo : MonoBehaviour
    {
        private void Start()
        {
            // Service.AudioService 和 Service.InputService 都是生成的属性
            // 懒初始化：首次访问时 new AudioService()
            Service.AudioService.Play("bgm_main");

            UnityEngine.Debug.Log($"跳跃按键: {Service.InputService.IsJumping()}");
            UnityEngine.Debug.Log("✓ Level 4 通关：AutoServiceGenerator + Collect() 运行正常");
        }
    }
}
