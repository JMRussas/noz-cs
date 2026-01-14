//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;
using System.Text.RegularExpressions;

namespace noz.editor;

public class ShaderDocument : Document
{
    public bool Blend { get; set; }
    public bool Depth { get; set; }
    public bool DepthLess { get; set; }
    public bool Postprocess { get; set; }
    public bool UiComposite { get; set; }
    public bool PremultipliedAlpha { get; set; }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Shader,
            ".glsl",
            () => new ShaderDocument()
        ));
    }

    public override void LoadMetadata(PropertySet meta)
    {
        Blend = meta.GetBool("shader", "blend", false);
        Depth = meta.GetBool("shader", "depth", false);
        DepthLess = meta.GetBool("shader", "depth_less", false);
        Postprocess = meta.GetBool("shader", "postproc", false);
        UiComposite = meta.GetBool("shader", "composite", false);
        PremultipliedAlpha = meta.GetBool("shader", "premultiplied", false);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (Blend) meta.SetBool("shader", "blend", true);
        if (Depth) meta.SetBool("shader", "depth", true);
        if (DepthLess) meta.SetBool("shader", "depth_less", true);
        if (Postprocess) meta.SetBool("shader", "postproc", true);
        if (UiComposite) meta.SetBool("shader", "composite", true);
        if (PremultipliedAlpha) meta.SetBool("shader", "premultiplied", true);
    }

    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        var source = File.ReadAllText(Path);
        var includeDir = System.IO.Path.GetDirectoryName(Path) ?? ".";

        var vertexSource = ExtractStage(source, "VERTEX");
        var fragmentSource = ExtractStage(source, "FRAGMENT");

        vertexSource = ProcessIncludes(vertexSource, includeDir);
        fragmentSource = ProcessIncludes(fragmentSource, includeDir);

        var flags = GetShaderFlags();

        // Write OpenGL 4.3 version
        WriteGlsl(outputPath + ".glsl", vertexSource, fragmentSource, flags, ConvertToOpenGL);

        // Write OpenGL ES 3.0 version
        WriteGlsl(outputPath + ".gles", vertexSource, fragmentSource, flags, ConvertToOpenGLES);
    }

    private ShaderFlags GetShaderFlags()
    {
        var flags = ShaderFlags.None;
        if (Blend) flags |= ShaderFlags.Blend;
        if (Depth) flags |= ShaderFlags.Depth;
        if (DepthLess) flags |= ShaderFlags.DepthLess;
        if (Postprocess) flags |= ShaderFlags.Postprocess;
        if (UiComposite) flags |= ShaderFlags.UiComposite;
        if (PremultipliedAlpha) flags |= ShaderFlags.PremultipliedAlpha;
        return flags;
    }

    private static string ExtractStage(string source, string stage)
    {
        var vertexPattern = new Regex(@"//@ VERTEX\s*\n([\s\S]*?)//@ END");
        var fragmentPattern = new Regex(@"//@ FRAGMENT\s*\n([\s\S]*?)//@ END");

        var result = source;

        if (stage == "VERTEX")
        {
            var match = vertexPattern.Match(result);
            if (match.Success)
                result = match.Groups[1].Value;
            result = fragmentPattern.Replace(result, "");
        }
        else if (stage == "FRAGMENT")
        {
            var match = fragmentPattern.Match(result);
            if (match.Success)
                result = match.Groups[1].Value;
            result = vertexPattern.Replace(result, "");
        }

        return result.Trim();
    }

    private static string ProcessIncludes(string source, string baseDir)
    {
        var result = new StringBuilder();
        var lines = source.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#include"))
            {
                var quote1 = trimmed.IndexOf('"');
                var quote2 = trimmed.LastIndexOf('"');
                if (quote1 >= 0 && quote2 > quote1)
                {
                    var filename = trimmed.Substring(quote1 + 1, quote2 - quote1 - 1);
                    var includePath = System.IO.Path.Combine(baseDir, filename);

                    if (File.Exists(includePath))
                    {
                        var includeContent = File.ReadAllText(includePath);
                        var includeDir = System.IO.Path.GetDirectoryName(includePath) ?? baseDir;
                        result.AppendLine(ProcessIncludes(includeContent, includeDir));
                    }
                    else
                    {
                        Log.Error($"Could not open include file: {includePath}");
                    }
                    continue;
                }
            }
            result.AppendLine(line);
        }

        return result.ToString();
    }

    private static string ConvertToOpenGL(string source)
    {
        var result = source;

        // Remove #version directive
        result = Regex.Replace(result, @"#version\s+\d+[^\n]*\n?", "");

        // Remove set = X (Vulkan-specific)
        result = Regex.Replace(result, @",?\s*set\s*=\s*\d+\s*,?", ",");

        // Replace row_major with std140
        result = Regex.Replace(result, @"\brow_major\b", "std140");

        // Clean up layout qualifiers
        result = CleanupLayoutQualifiers(result);

        // Add std140 to uniform blocks
        result = AddStd140ToUniformBlocks(result);

        // Prepend OpenGL 4.3 version
        return "#version 430 core\n\n" + result;
    }

    private static string ConvertToOpenGLES(string source)
    {
        var result = source;

        // Remove #version directive
        result = Regex.Replace(result, @"#version\s+\d+[^\n]*\n?", "");

        // Remove set, binding, and location (not supported in GLES 3.0)
        result = Regex.Replace(result, @",?\s*set\s*=\s*\d+\s*,?", ",");
        result = Regex.Replace(result, @",?\s*binding\s*=\s*\d+\s*,?", ",");
        result = Regex.Replace(result, @",?\s*location\s*=\s*\d+\s*,?", ",");

        // Replace row_major with std140
        result = Regex.Replace(result, @"\brow_major\b", "std140");

        // Remove 'f' suffix from float literals
        result = Regex.Replace(result, @"(\d+\.\d*|\d*\.\d+|\d+)[fF]\b", "$1");

        // Clean up layout qualifiers
        result = CleanupLayoutQualifiers(result);

        // Add std140 to uniform blocks
        result = AddStd140ToUniformBlocks(result);

        // Prepend GLES 3.0 version with precision qualifiers
        return "#version 300 es\nprecision highp float;\nprecision highp int;\n\n" + result;
    }

    private static string CleanupLayoutQualifiers(string source)
    {
        var result = source;

        // Clean up double commas
        result = Regex.Replace(result, @"\s*,\s*,\s*", ", ");

        // Clean up trailing commas in layout()
        result = Regex.Replace(result, @",\s*\)", ")");

        // Clean up leading commas in layout()
        result = Regex.Replace(result, @"\(\s*,", "(");

        // Remove empty layout() declarations
        result = Regex.Replace(result, @"layout\s*\(\s*\)\s*", "");

        return result;
    }

    private static string AddStd140ToUniformBlocks(string source)
    {
        var lines = source.Split('\n');
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            var modifiedLine = line;

            if (line.Contains("uniform") && !line.Contains("sampler") && line.Contains("{"))
            {
                if (!line.Contains("layout"))
                {
                    // No layout, add layout(std140)
                    modifiedLine = Regex.Replace(line, @"^(\s*)uniform\s+(\w+)\s*\{", "$1layout(std140) uniform $2 {");
                }
                else if (!line.Contains("std140"))
                {
                    // Has layout but no std140, add it
                    modifiedLine = Regex.Replace(line, @"layout\s*\(([^)]*)\)\s*uniform\s+", "layout(std140, $1) uniform ");
                }
            }

            result.AppendLine(modifiedLine);
        }

        return result.ToString();
    }

    private static void WriteGlsl(string path, string vertexSource, string fragmentSource, ShaderFlags flags, Func<string, string> converter)
    {
        var glVertex = converter(vertexSource);
        var glFragment = converter(fragmentSource);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");

        using var writer = new BinaryWriter(File.Create(path));

        // Asset header
        writer.Write(Constants.AssetSignature);
        writer.Write((byte)AssetType.Shader);
        writer.Write(Shader.Version);
        writer.Write((ushort)0); // flags in header (unused)

        // Shader data
        var vertexBytes = Encoding.UTF8.GetBytes(glVertex);
        var fragmentBytes = Encoding.UTF8.GetBytes(glFragment);

        writer.Write((uint)vertexBytes.Length);
        writer.Write(vertexBytes);
        writer.Write((uint)fragmentBytes.Length);
        writer.Write(fragmentBytes);
        writer.Write((byte)flags);
    }
}
