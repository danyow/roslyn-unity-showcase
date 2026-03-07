// 编译桩：让 Tests 项目能编译 game/Assets/Showcase/Level6/Luban/ 下的 Luban 生成文件
// 仅供 dotnet build 使用，Unity 里用真正的 com.code-philosophy.luban UPM 包

using System;
using System.Collections;

namespace Luban
{
    public class SerializationException : System.Exception
    {
        public SerializationException() { }
        public SerializationException(string message) : base(message) { }
    }

    public abstract class BeanBase
    {
        public abstract int GetTypeId();
    }

    public class ByteBuf
    {
        public int ReadInt() => 0;
        public float ReadFloat() => 0f;
        public string ReadString() => string.Empty;
        public bool ReadBool() => false;
        public int ReadSize() => 0;
    }

    public static class StringUtil
    {
        public static string CollectionToString(IDictionary dict) => dict?.ToString() ?? "{}";
    }
}
