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
            ".wgsl",
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
        ImportWgsl(outputPath, GetShaderFlags());
    }

    private void ImportWgsl(string outputPath, ShaderFlags flags)
    {
        var wgslSource = File.ReadAllText(Path);

        // Parse bindings directly from WGSL source
        var bindings = ParseWgslBindings(wgslSource);

        // Write WGSL shader asset with metadata
        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Shader, Shader.Version);

        var sourceBytes = Encoding.UTF8.GetBytes(wgslSource);

        // WGSL uses same source for both stages (combined vertex+fragment)
        writer.Write((uint)sourceBytes.Length);
        writer.Write(sourceBytes);
        writer.Write((uint)sourceBytes.Length);
        writer.Write(sourceBytes);
        writer.Write((byte)flags);

        // Write binding metadata
        writer.Write((uint)bindings.Count);
        foreach (var binding in bindings)
        {
            writer.Write(binding.Binding);
            writer.Write((byte)binding.Type);
            writer.Write(binding.Name);
        }

        Log.Info($"Imported WGSL shader {Name} with {bindings.Count} bindings");
    }

    private List<ShaderBinding> ParseWgslBindings(string wgslSource)
    {
        var bindings = new List<ShaderBinding>();
        var bindingDict = new Dictionary<uint, ShaderBinding>();

        // Pattern: @group(N) @binding(M) var<uniform> name: Type;
        // Pattern: @group(N) @binding(M) var name: texture_2d<f32>;
        // Pattern: @group(N) @binding(M) var name: texture_2d_array<f32>;
        // Pattern: @group(N) @binding(M) var name: sampler;

        var bindingPattern = @"@group\s*\(\s*(\d+)\s*\)\s*@binding\s*\(\s*(\d+)\s*\)\s*var(?:<(\w+)>)?\s+(\w+)\s*:\s*([^;]+);";
        var matches = Regex.Matches(wgslSource, bindingPattern);

        foreach (Match match in matches)
        {
            var group = uint.Parse(match.Groups[1].Value);
            var binding = uint.Parse(match.Groups[2].Value);
            var storageClass = match.Groups[3].Value; // uniform, storage, etc.
            var name = match.Groups[4].Value;
            var type = match.Groups[5].Value.Trim();

            // Determine binding type from WGSL type
            ShaderBindingType bindingType;
            if (storageClass == "uniform" || type.Contains("uniform"))
            {
                bindingType = ShaderBindingType.UniformBuffer;
            }
            else if (type.Contains("texture_2d_array"))
            {
                bindingType = ShaderBindingType.Texture2DArray;
            }
            else if (type.Contains("texture_2d") || type.Contains("texture_cube"))
            {
                bindingType = ShaderBindingType.Texture2D;
            }
            else if (type.Contains("sampler"))
            {
                bindingType = ShaderBindingType.Sampler;
            }
            else
            {
                Log.Warning($"Unknown WGSL binding type: {type} for {name}, assuming uniform buffer");
                bindingType = ShaderBindingType.UniformBuffer;
            }

            // Only support group 0 for now
            if (group == 0)
            {
                bindingDict[binding] = new ShaderBinding
                {
                    Binding = binding,
                    Type = bindingType,
                    Name = name
                };
            }
        }

        return bindingDict.Values.OrderBy(b => b.Binding).ToList();
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
        // GLSL-first approach: Auto-generate WGSL from SPIR-V and extract binding metadata
        var glslSource = File.ReadAllText(Path);
        var includeDir = System.IO.Path.GetDirectoryName(Path) ?? ".";

        var vertexSource = ExtractStage(glslSource, "VERTEX");
        var fragmentSource = ExtractStage(glslSource, "FRAGMENT");

        vertexSource = ProcessIncludes(vertexSource, includeDir);
        fragmentSource = ProcessIncludes(fragmentSource, includeDir);

        // Compile GLSL to SPIR-V
        var vertexSpirv = ShaderCompiler.CompileGlslToSpirv(vertexSource, ShaderStage.Vertex, Name + ".vert", out var vertexError);
        if (vertexSpirv == null)
        {
            Log.Error($"Failed to compile vertex shader to SPIR-V: {vertexError}");
            return;
        }

        var fragmentSpirv = ShaderCompiler.CompileGlslToSpirv(fragmentSource, ShaderStage.Fragment, Name + ".frag", out var fragmentError);
        if (fragmentSpirv == null)
        {
            Log.Error($"Failed to compile fragment shader to SPIR-V: {fragmentError}");
            return;
        }

        // Reflect bindings from both stages and merge them
        var vertexMetadata = ShaderCompiler.ReflectBindings(vertexSpirv, out var vertexReflectError);
        if (vertexMetadata == null)
        {
            Log.Error($"Failed to reflect vertex shader bindings: {vertexReflectError}");
            return;
        }

        var fragmentMetadata = ShaderCompiler.ReflectBindings(fragmentSpirv, out var fragmentReflectError);
        if (fragmentMetadata == null)
        {
            Log.Error($"Failed to reflect fragment shader bindings: {fragmentReflectError}");
            return;
        }

        // Merge bindings from both stages (WebGPU requires global bindings across stages)
        var bindings = new List<ShaderBinding>();
        var bindingDict = new Dictionary<uint, ShaderBinding>();

        foreach (var binding in vertexMetadata.Bindings)
        {
            bindingDict[binding.Binding] = binding;
        }

        foreach (var binding in fragmentMetadata.Bindings)
        {
            if (!bindingDict.ContainsKey(binding.Binding))
            {
                bindingDict[binding.Binding] = binding;
            }
        }

        bindings = bindingDict.Values.OrderBy(b => b.Binding).ToList();

        // Check for manual .wgsl file first, fall back to auto-generation
        var wgslSourcePath = System.IO.Path.ChangeExtension(Path, ".wgsl");
        string? wgslSource = null;

        if (File.Exists(wgslSourcePath))
        {
            wgslSource = File.ReadAllText(wgslSourcePath);
            Log.Info($"Using manual WGSL file for {Name}");
        }
        else
        {
            // Auto-generate WGSL from SPIR-V using Tint
            Log.Info($"Auto-generating WGSL for {Name} from SPIR-V");

            // For WGSL, we need to compile both stages together
            // Currently Tint expects a single SPIR-V module, so we'll use vertex stage
            // In the future, we might need a more sophisticated approach
            wgslSource = ShaderCompiler.CompileSpirvToWgsl(vertexSpirv, out var wgslError);
            if (wgslSource == null)
            {
                Log.Warning($"Failed to auto-generate WGSL: {wgslError}");
                Log.Warning($"Falling back to manual WGSL file requirement");
                Log.Warning($"Please create {wgslSourcePath} manually");
                return;
            }
        }

        using var writer = new BinaryWriter(File.Create(path));
        writer.WriteAssetHeader(AssetType.Shader, Shader.Version);

        var sourceBytes = Encoding.UTF8.GetBytes(wgslSource);

        writer.Write((uint)sourceBytes.Length);
        writer.Write(sourceBytes);
        writer.Write((uint)sourceBytes.Length); // Same source for both stages
        writer.Write(sourceBytes);
        writer.Write((byte)flags);

        // Write binding metadata extracted from SPIR-V reflection
        writer.Write((uint)bindings.Count);
        foreach (var binding in bindings)
        {
            writer.Write(binding.Binding);
            writer.Write((byte)binding.Type);
            writer.Write(binding.Name);
        }
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
