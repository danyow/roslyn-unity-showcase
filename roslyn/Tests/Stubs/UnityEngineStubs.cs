// 编译桩：让 Tests 项目能编译引用 UnityEngine 的文件
// 仅供 dotnet build 使用，Unity 里用真正的 UnityEngine

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public static class Debug
    {
        public static void Log(object message) => System.Console.WriteLine(message);
        public static void LogWarning(object message) => System.Console.WriteLine($"[WARN] {message}");
        public static void LogError(object message) => System.Console.WriteLine($"[ERROR] {message}");
        public static void LogException(System.Exception ex) => System.Console.WriteLine($"[EX] {ex}");
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float this[int index]
        {
            get => index == 0 ? x : y;
            set { if (index == 0) x = value; else y = value; }
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int index]
        {
            get => index == 0 ? x : index == 1 ? y : z;
            set { if (index == 0) x = value; else if (index == 1) y = value; else z = value; }
        }
    }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) => false;
    }

    public enum KeyCode { Space = 32 }
}
