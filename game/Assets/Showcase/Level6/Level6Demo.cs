using System.Collections.Generic;
using Showcase.Core;
using UnityEngine;

namespace Showcase.Level6
{
    /// <summary>
    /// Level 6 演示：EventExecuteGenerator + Luban 完整工作流
    ///
    /// 生成器读取 JSON 配置 + Scriban 模板，为 Luban 事件体系生成：
    ///   1. BaseEvent.Event.g.cs   — abstract Execute(BeanBase caller) 声明
    ///   2. GlobalEvent.Event.g.cs — override Execute switch 分发到 GlobalEventConfig 方法
    ///   3. GlobalEventConfig.Event.g.cs — [Obsolete] 方法桩（如果手写实现缺失）
    /// </summary>
    public class Level6Demo : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log("=== Level 6: EventExecuteGenerator + Luban 事件分发 ===");

            // 1. 手动构造一个 GlobalEvent（模拟 Luban 反序列化后的结果）
            var evt = new GlobalEvent
            {
                method = "Trigger",
                args = new Dictionary<string, BaseValue>
                {
                    ["delay"] = new FloatValue { value = 0.5f },
                    ["force"] = new BoolValue { value = true },
                },
                returnType = null,
                refId = "game_over_event",
                refIdConfig = new GlobalEventConfig { id = "game_over_event" },
            };

            Debug.Log($"  构造 GlobalEvent: {evt}");

            // 2. 调用生成的 Execute 方法
            //    Execute 内部 switch(method) 分发到 refIdConfig.Trigger(caller, delay, force)
            var caller = evt.refIdConfig; // 用 config 自身作为 caller 演示
            Debug.Log("  调用 evt.Execute(caller) ...");
            evt.Execute(caller);

            Debug.Log("=== Level 6 通关：EventExecuteGenerator 生成事件分发代码 ===");
        }
    }
}
