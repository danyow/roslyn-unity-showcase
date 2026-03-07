using Showcase.Core.MathType;
using UnityEngine;

namespace Showcase.Level3
{
    // MathSetter/MathGetter 生成器扫描此特性
    // 自动生成 Vector3 的 Set/Add/Sub/Mul/Div/Mod 及 GetXY/GetXZ 等扩展方法
    [MathSetter(typeof(Vector3), typeof(float), axis: 3, reduction: true)]
    [MathGetter(typeof(Vector3), axis: 3)]
    public static partial class Vector3Ext { }

    public class Level3Demo : MonoBehaviour
    {
        private void Start()
        {
            var v = new Vector3(1, 2, 3);

            // 使用 MathSetterGenerator 生成的扩展方法
            v = Vector3Ext.SetX(v, 10f);
            UnityEngine.Debug.Log($"SetX(10): {v}");

            // 使用 MathGetterGenerator 生成的扩展方法
            var xy = Vector3Ext.GetXY(v);
            UnityEngine.Debug.Log($"GetXY: {xy}");

            UnityEngine.Debug.Log("✓ Level 3 通关：MathType 生成向量扩展方法");
        }
    }
}
