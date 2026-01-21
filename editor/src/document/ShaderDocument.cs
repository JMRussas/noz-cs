//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.Maths;
using System.Text;
using System.Text.RegularExpressions;

namespace NoZ.Editor;

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
        WriteGlsl(outputPath, vertexSource, fragmentSource, flags, ConvertToOpenGL);

        // Write OpenGL ES 3.0 version
        WriteGlsl(outputPath + ".gles", vertexSource, fragmentSource, flags, ConvertToOpenGLES);

        // Write HLSL version for DX12
        WriteHlsl(outputPath + ".dx12", vertexSource, fragmentSource, flags);

        // Write WGSL version for WebGPU
        WriteWgsl(outputPath + ".wgsl", flags);
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
        // Extract #version directive if present
        var versionMatch = Regex.Match(source, @"#version\s+\d+[^\n]*\n?");
        var version = versionMatch.Success ? versionMatch.Value : "";

        // Remove the #version from source (we'll add it back at the start)
        var sourceWithoutVersion = versionMatch.Success
            ? source.Remove(versionMatch.Index, versionMatch.Length)
            : source;

        // Build the stage-specific source by defining the appropriate macro
        var define = stage == "VERTEX" ? "#define VERTEX_PROGRAM\n" : "#define FRAGMENT_PROGRAM\n";

        return version + define + sourceWithoutVersion;
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

        using var writer = new BinaryWriter(File.Create(path));
        writer.WriteAssetHeader(AssetType.Shader, Shader.Version);

        var vertexBytes = Encoding.UTF8.GetBytes(glVertex);
        var fragmentBytes = Encoding.UTF8.GetBytes(glFragment);

        writer.Write((uint)vertexBytes.Length);
        writer.Write(vertexBytes);
        writer.Write((uint)fragmentBytes.Length);
        writer.Write(fragmentBytes);
        writer.Write((byte)flags);
    }

    private void WriteHlsl(string path, string vertexSource, string fragmentSource, ShaderFlags flags)
    {
        var (hlslVertex, vertexError) = ShaderCompiler.CompileGlslToHlsl(vertexSource, ShaderStage.Vertex, Name + ".vert");
        if (hlslVertex == null)
        {
            Log.Error($"Failed to compile vertex shader to HLSL: {vertexError}");
            return;
        }

        var (hlslFragment, fragmentError) = ShaderCompiler.CompileGlslToHlsl(fragmentSource, ShaderStage.Fragment, Name + ".frag");
        if (hlslFragment == null)
        {
            Log.Error($"Failed to compile fragment shader to HLSL: {fragmentError}");
            return;
        }

        using var writer = new BinaryWriter(File.Create(path));
        writer.WriteAssetHeader(AssetType.Shader, Shader.Version);

        var vertexBytes = Encoding.UTF8.GetBytes(hlslVertex);
        var fragmentBytes = Encoding.UTF8.GetBytes(hlslFragment);

        writer.Write((uint)vertexBytes.Length);
        writer.Write(vertexBytes);
        writer.Write((uint)fragmentBytes.Length);
        writer.Write(fragmentBytes);
        writer.Write((byte)flags);
    }

    private void WriteWgsl(string path, ShaderFlags flags)
    {
        // GLSL-first approach: Parse GLSL for binding metadata, use manual WGSL for now
        // Future: Auto-generate WGSL from SPIR-V using Tint

        // Read GLSL source to extract binding metadata
        var glslSource = File.ReadAllText(Path);
        var bindings = ExtractBindingsFromGlsl(glslSource);

        // Check for manual .wgsl file
        var wgslSourcePath = System.IO.Path.ChangeExtension(Path, ".wgsl");
        string wgslSource;

        if (File.Exists(wgslSourcePath))
        {
            // Use manual WGSL file
            wgslSource = File.ReadAllText(wgslSourcePath);
            Log.Info($"Using manual WGSL file for {Name}");
        }
        else
        {
            // Try to auto-generate WGSL from GLSL via SPIR-V
            Log.Info($"Attempting to auto-generate WGSL for {Name}");

            var includeDir = System.IO.Path.GetDirectoryName(Path) ?? ".";
            var vertexSource = ExtractStage(glslSource, "VERTEX");
            var fragmentSource = ExtractStage(glslSource, "FRAGMENT");

            vertexSource = ProcessIncludes(vertexSource, includeDir);
            fragmentSource = ProcessIncludes(fragmentSource, includeDir);

            // For now, fall back to error if no manual WGSL exists
            // TODO: Implement SPIR-V → WGSL conversion
            Log.Warning($"WGSL shader not found at {wgslSourcePath}, skipping WebGPU output");
            Log.Warning("Auto-generation from GLSL not yet implemented");
            return;
        }

        using var writer = new BinaryWriter(File.Create(path));
        writer.WriteAssetHeader(AssetType.Shader, Shader.Version);

        var sourceBytes = Encoding.UTF8.GetBytes(wgslSource);

        writer.Write((uint)sourceBytes.Length);
        writer.Write(sourceBytes);
        writer.Write((uint)sourceBytes.Length); // Same source for both stages
        writer.Write(sourceBytes);
        writer.Write((byte)flags);

        // Write binding metadata extracted from GLSL
        writer.Write((uint)bindings.Count);
        foreach (var binding in bindings)
        {
            writer.Write(binding.Binding);
            writer.Write((byte)binding.Type);
            writer.Write(binding.Name);
        }
    }

    private List<ShaderBinding> ExtractBindingsFromGlsl(string glslSource)
    {
        var bindings = new List<ShaderBinding>();
        var lines = glslSource.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip comments and empty lines
            if (line.StartsWith("//") || line.StartsWith("/*") || string.IsNullOrWhiteSpace(line))
                continue;

            // Look for layout(binding = N) declarations in GLSL
            if (line.Contains("layout") && line.Contains("binding"))
            {
                // Extract binding number: layout(binding = 0) or layout(set = 0, binding = 1)
                var bindingStart = line.IndexOf("binding");
                if (bindingStart >= 0)
                {
                    bindingStart = line.IndexOf('=', bindingStart);
                    if (bindingStart >= 0)
                    {
                        bindingStart++;
                        var bindingEnd = line.IndexOfAny(new[] { ',', ')' }, bindingStart);
                        if (bindingEnd > bindingStart)
                        {
                            var bindingStr = line.Substring(bindingStart, bindingEnd - bindingStart).Trim();
                            if (uint.TryParse(bindingStr, out var bindingNumber))
                            {
                                // Determine type from the declaration
                                var bindingType = BindingType.UniformBuffer;
                                var name = "unknown";

                                // Look ahead for the actual declaration
                                var fullDecl = line;
                                // If line doesn't end with semicolon, read next lines
                                while (!fullDecl.Contains(';') && i + 1 < lines.Length)
                                {
                                    i++;
                                    fullDecl += " " + lines[i].Trim();
                                }

                                // Detect type
                                if (fullDecl.Contains("sampler2DArray") || fullDecl.Contains("texture2DArray"))
                                    bindingType = BindingType.Texture2DArray;
                                else if (fullDecl.Contains("sampler2D") || fullDecl.Contains("texture2D"))
                                    bindingType = BindingType.Texture2D;
                                else if (fullDecl.Contains("sampler"))
                                    bindingType = BindingType.Sampler;
                                else if (fullDecl.Contains("uniform"))
                                    bindingType = BindingType.UniformBuffer;

                                // Extract name - look for identifier before semicolon
                                var semicolonIndex = fullDecl.IndexOf(';');
                                if (semicolonIndex > 0)
                                {
                                    var beforeSemicolon = fullDecl.Substring(0, semicolonIndex).Trim();
                                    var words = beforeSemicolon.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (words.Length > 0)
                                        name = words[words.Length - 1].Trim();
                                }

                                bindings.Add(new ShaderBinding
                                {
                                    Binding = bindingNumber,
                                    Type = bindingType,
                                    Name = name
                                });
                            }
                        }
                    }
                }
            }
        }

        // Sort by binding number
        bindings.Sort((a, b) => a.Binding.CompareTo(b.Binding));

        return bindings;
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconShader);
        }
    }
}
