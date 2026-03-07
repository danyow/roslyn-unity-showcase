using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SourceGenerator.Infrastructure
{
    /// <summary>
    /// 物理文件写入工具（避免触发重复编译）
    /// </summary>
    public static class FileWriter
    {
        /// <summary>
        /// 写入物理文件，仅当内容改变时才写入（避免触发重复编译）
        /// </summary>
        /// <param name="context">Source Generator 上下文</param>
        /// <param name="sourceFilePath">源文件路径（用于确定输出目录）</param>
        /// <param name="fileName">输出文件名（如 ClassName.g.cs）</param>
        /// <param name="source">生成的源代码内容</param>
        /// <param name="diagnosticIdPrefix">诊断 ID 前缀（如 AUTOIMPL、DEBUGCFG）</param>
        public static void WriteToPhysicalFile(
            SourceProductionContext context,
            string sourceFilePath,
            string fileName,
            string source,
            string diagnosticIdPrefix = "SRCGEN"
        )
        {
            if (string.IsNullOrEmpty(sourceFilePath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(sourceFilePath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return;
                }

                var outputPath = Path.Combine(directory, fileName);

                // 关键优化：仅当内容改变时才写入
                if (File.Exists(outputPath))
                {
                    var existingContent = File.ReadAllText(outputPath, Encoding.UTF8);
                    if (existingContent == source)
                    {
                        return; // 内容相同，跳过写入
                    }
                }

                // 写入文件
                File.WriteAllText(outputPath, source, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 写入失败不阻止编译，仅报告警告
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        new DiagnosticDescriptor(
                            $"{diagnosticIdPrefix}101",
                            "File write warning",
                            $"Failed to write {fileName}: {ex.Message}",
                            "SourceGenerator",
                            DiagnosticSeverity.Warning,
                            true
                        ),
                        Location.None
                    )
                );
            }
        }
    }
}
