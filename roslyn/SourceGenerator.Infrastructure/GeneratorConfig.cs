namespace SourceGenerator.Infrastructure
{
    /// <summary>
    /// Source Generator 生成模式
    /// </summary>
    public enum GenerateMode
    {
        /// <summary>内存注入，生成代码到编译管道（obj/Generated/）</summary>
        AddSource,

        /// <summary>写入物理文件（与源文件同目录）</summary>
        WriteFile,
    }

    /// <summary>
    /// Source Generator 配置
    /// </summary>
    public class GeneratorConfig
    {
        /// <summary>生成模式（默认：WriteFile）</summary>
        public GenerateMode Mode { get; set; } = GenerateMode.WriteFile;

        /// <summary>生成文件扩展名（默认：.g.cs）</summary>
        public string FileExtension { get; set; } = ".g.cs";

        /// <summary>
        /// 编译期烘焙的默认模式：
        /// Debug 构建定义 DEFAULT_ADDSOURCE → 默认 AddSource
        /// Release 构建不定义 → 默认 WriteFile
        /// Unity 侧无法传递 build_property 时自动使用此默认值
        /// </summary>
        private static GenerateMode DefaultMode =>
#if DEFAULT_ADDSOURCE
            GenerateMode.AddSource;
#else
            GenerateMode.WriteFile;
#endif

        /// <summary>
        /// 解析 MSBuild 属性中的生成模式
        /// </summary>
        public static GenerateMode ParseMode(string? mode)
        {
            if (string.IsNullOrEmpty(mode))
                return DefaultMode;

            return mode!.ToLowerInvariant() switch
            {
                "addsource" => GenerateMode.AddSource,
                "writefile" => GenerateMode.WriteFile,
                "file" => GenerateMode.WriteFile,
                _ => DefaultMode,
            };
        }
    }
}
