//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Shaderc;
using Silk.NET.SPIRV.Cross;

using SpvcCompiler = Silk.NET.SPIRV.Cross.Compiler;
using ShadercCompiler = Silk.NET.Shaderc.Compiler;

namespace NoZ.Editor;

public enum ShaderStage
{
    Vertex,
    Fragment
}

public enum ShaderTarget
{
    Hlsl,
    Msl,
    Spirv,
    Wgsl
}

public class ShaderBindingMetadata
{
    public List<ShaderBinding> Bindings { get; set; } = new();
}

public static class ShaderCompiler
{
    private static Shaderc? _shaderc;
    private static Cross? _spirvCross;

    public static void Init()
    {
        Log.Info("ShaderCompiler.Init");
        _shaderc = Shaderc.GetApi();
        _spirvCross = Cross.GetApi();
    }

    public static void Shutdown()
    {
        _shaderc?.Dispose();
        _spirvCross?.Dispose();
        _shaderc = null;
        _spirvCross = null;
    }

    public static unsafe byte[]? CompileGlslToSpirv(string glslSource, ShaderStage stage, string filename, out string? error)
    {
        error = null;
        if (_shaderc == null)
        {
            error = "Shaderc not initialized";
            return null;
        }

        ShadercCompiler* compiler = _shaderc.CompilerInitialize();
        if (compiler == null)
        {
            error = "Failed to create shaderc compiler";
            return null;
        }

        try
        {
            var options = _shaderc.CompileOptionsInitialize();
            if (options == null)
            {
                error = "Failed to create compile options";
                return null;
            }

            try
            {
                // Target Vulkan 1.0 / SPIR-V 1.0
                _shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
                _shaderc.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc10);
                _shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);

                var shaderKind = stage switch
                {
                    ShaderStage.Vertex => ShaderKind.VertexShader,
                    ShaderStage.Fragment => ShaderKind.FragmentShader,
                    _ => ShaderKind.GlslDefaultVertexShader
                };

                var sourceBytes = Encoding.UTF8.GetBytes(glslSource);
                var filenameBytes = Encoding.UTF8.GetBytes(filename);
                var entryBytes = Encoding.UTF8.GetBytes("main");

                fixed (byte* sourcePtr = sourceBytes)
                fixed (byte* filenamePtr = filenameBytes)
                fixed (byte* entryPtr = entryBytes)
                {
                    var result = _shaderc.CompileIntoSpv(
                        compiler,
                        sourcePtr,
                        (nuint)sourceBytes.Length,
                        shaderKind,
                        filenamePtr,
                        entryPtr,
                        options
                    );

                    if (result == null)
                    {
                        error = "Compilation returned null result";
                        return null;
                    }

                    try
                    {
                        var status = _shaderc.ResultGetCompilationStatus(result);
                        if (status != CompilationStatus.Success)
                        {
                            var errorPtr = _shaderc.ResultGetErrorMessage(result);
                            error = errorPtr != null ? Marshal.PtrToStringUTF8((nint)errorPtr) : $"Compilation failed with status {status}";
                            return null;
                        }

                        var length = _shaderc.ResultGetLength(result);
                        var bytesPtr = _shaderc.ResultGetBytes(result);

                        if (length == 0 || bytesPtr == null)
                        {
                            error = "Compilation produced no output";
                            return null;
                        }

                        var spirvBytes = new byte[(int)length];
                        Marshal.Copy((nint)bytesPtr, spirvBytes, 0, (int)length);
                        return spirvBytes;
                    }
                    finally
                    {
                        _shaderc.ResultRelease(result);
                    }
                }
            }
            finally
            {
                _shaderc.CompileOptionsRelease(options);
            }
        }
        finally
        {
            _shaderc.CompilerRelease(compiler);
        }
    }

    public static unsafe string? ConvertSpirvTo(byte[] spirvBytes, ShaderTarget target, out string? error)
    {
        error = null;
        if (_spirvCross == null)
        {
            error = "SPIRV-Cross not initialized";
            return null;
        }

        Context* context = null;
        var result = _spirvCross.ContextCreate(&context);
        if (result != Result.Success || context == null)
        {
            error = "Failed to create SPIRV-Cross context";
            return null;
        }

        try
        {
            // Convert bytes to uint array (SPIR-V is uint32 words)
            var wordCount = spirvBytes.Length / 4;
            var spirvWords = new uint[wordCount];
            Buffer.BlockCopy(spirvBytes, 0, spirvWords, 0, spirvBytes.Length);

            ParsedIr* parsedIr = null;
            fixed (uint* spirvPtr = spirvWords)
            {
                result = _spirvCross.ContextParseSpirv(context, spirvPtr, (nuint)wordCount, &parsedIr);
            }

            if (result != Result.Success || parsedIr == null)
            {
                error = GetContextError(context) ?? "Failed to parse SPIR-V";
                return null;
            }

            var backend = target switch
            {
                ShaderTarget.Hlsl => Backend.Hlsl,
                ShaderTarget.Msl => Backend.Msl,
                _ => Backend.None
            };

            if (backend == Backend.None)
            {
                error = "Invalid target backend";
                return null;
            }

            SpvcCompiler* spvcCompiler = null;
            result = _spirvCross.ContextCreateCompiler(context, backend, parsedIr, CaptureMode.TakeOwnership, &spvcCompiler);
            if (result != Result.Success || spvcCompiler == null)
            {
                error = GetContextError(context) ?? "Failed to create compiler";
                return null;
            }

            // Set compiler options
            CompilerOptions* compilerOptions = null;
            result = _spirvCross.CompilerCreateCompilerOptions(spvcCompiler, &compilerOptions);
            if (result == Result.Success && compilerOptions != null)
            {
                if (target == ShaderTarget.Hlsl)
                {
                    // HLSL Shader Model 5.1 for DX12
                    _spirvCross.CompilerOptionsSetUint(compilerOptions, CompilerOption.HlslShaderModel, 51);
                }
                else if (target == ShaderTarget.Msl)
                {
                    // Metal 2.0 for modern iOS/macOS
                    _spirvCross.CompilerOptionsSetUint(compilerOptions, CompilerOption.MslVersion, 20000);
                }

                _spirvCross.CompilerInstallCompilerOptions(spvcCompiler, compilerOptions);
            }

            byte* sourcePtr = null;
            result = _spirvCross.CompilerCompile(spvcCompiler, &sourcePtr);
            if (result != Result.Success || sourcePtr == null)
            {
                error = GetContextError(context) ?? "Failed to compile";
                return null;
            }

            return Marshal.PtrToStringUTF8((nint)sourcePtr);
        }
        finally
        {
            _spirvCross.ContextDestroy(context);
        }
    }

    private static unsafe string? GetContextError(Context* context)
    {
        if (_spirvCross == null || context == null)
            return null;

        var errorPtr = _spirvCross.ContextGetLastErrorString(context);
        if (errorPtr == null)
            return null;

        return Marshal.PtrToStringUTF8((nint)errorPtr);
    }

    public static (string? hlsl, string? error) CompileGlslToHlsl(string glslSource, ShaderStage stage, string filename)
    {
        var spirv = CompileGlslToSpirv(glslSource, stage, filename, out var error);
        if (spirv == null)
            return (null, error);

        var hlsl = ConvertSpirvTo(spirv, ShaderTarget.Hlsl, out error);
        return (hlsl, error);
    }

    public static (string? msl, string? error) CompileGlslToMsl(string glslSource, ShaderStage stage, string filename)
    {
        var spirv = CompileGlslToSpirv(glslSource, stage, filename, out var error);
        if (spirv == null)
            return (null, error);

        var msl = ConvertSpirvTo(spirv, ShaderTarget.Msl, out error);
        return (msl, error);
    }

    public static (byte[]? spirv, string? error) CompileGlslToSpirvBytes(string glslSource, ShaderStage stage, string filename)
    {
        var spirv = CompileGlslToSpirv(glslSource, stage, filename, out var error);
        return (spirv, error);
    }

    public static unsafe ShaderBindingMetadata? ReflectBindings(byte[] spirvBytes, out string? error)
    {
        error = null;
        if (_spirvCross == null)
        {
            error = "SPIRV-Cross not initialized";
            return null;
        }

        Context* context = null;
        var result = _spirvCross.ContextCreate(&context);
        if (result != Result.Success || context == null)
        {
            error = "Failed to create SPIRV-Cross context";
            return null;
        }

        try
        {
            // Convert bytes to uint array (SPIR-V is uint32 words)
            var wordCount = spirvBytes.Length / 4;
            var spirvWords = new uint[wordCount];
            Buffer.BlockCopy(spirvBytes, 0, spirvWords, 0, spirvBytes.Length);

            ParsedIr* parsedIr = null;
            fixed (uint* spirvPtr = spirvWords)
            {
                result = _spirvCross.ContextParseSpirv(context, spirvPtr, (nuint)wordCount, &parsedIr);
            }

            if (result != Result.Success || parsedIr == null)
            {
                error = GetContextError(context) ?? "Failed to parse SPIR-V";
                return null;
            }

            SpvcCompiler* spvcCompiler = null;
            result = _spirvCross.ContextCreateCompiler(context, Backend.None, parsedIr, CaptureMode.TakeOwnership, &spvcCompiler);
            if (result != Result.Success || spvcCompiler == null)
            {
                error = GetContextError(context) ?? "Failed to create compiler";
                return null;
            }

            var metadata = new ShaderBindingMetadata();

            // Get all shader resources
            Resources* resources = null;
            result = _spirvCross.CompilerCreateShaderResources(spvcCompiler, &resources);
            if (result != Result.Success || resources == null)
            {
                error = GetContextError(context) ?? "Failed to get shader resources";
                return null;
            }

            // Reflect uniform buffers
            ReflectedResource* uniformBuffers = null;
            nuint uniformBufferCount = 0;
            result = _spirvCross.ResourcesGetResourceListForType(resources, ResourceType.UniformBuffer, &uniformBuffers, &uniformBufferCount);
            if (result == Result.Success && uniformBufferCount > 0)
            {
                for (nuint i = 0; i < uniformBufferCount; i++)
                {
                    var buffer = uniformBuffers[i];
                    var binding = _spirvCross.CompilerGetDecoration(spvcCompiler, buffer.Id, Silk.NET.SPIRV.Decoration.Binding);
                    var namePtr = _spirvCross.CompilerGetName(spvcCompiler, buffer.Id);
                    var name = namePtr != null ? Marshal.PtrToStringUTF8((nint)namePtr) ?? "unknown" : "unknown";

                    metadata.Bindings.Add(new ShaderBinding
                    {
                        Binding = binding,
                        Type = ShaderBindingType.UniformBuffer,
                        Name = name
                    });
                }
            }

            // Reflect sampled images (textures)
            ReflectedResource* sampledImages = null;
            nuint sampledImageCount = 0;
            result = _spirvCross.ResourcesGetResourceListForType(resources, ResourceType.SampledImage, &sampledImages, &sampledImageCount);
            if (result == Result.Success && sampledImageCount > 0)
            {
                for (nuint i = 0; i < sampledImageCount; i++)
                {
                    var image = sampledImages[i];
                    var binding = _spirvCross.CompilerGetDecoration(spvcCompiler, image.Id, Silk.NET.SPIRV.Decoration.Binding);
                    var namePtr = _spirvCross.CompilerGetName(spvcCompiler, image.Id);
                    var name = namePtr != null ? Marshal.PtrToStringUTF8((nint)namePtr) ?? "unknown" : "unknown";

                    // Get the type to determine if it's a 2D texture or 2D array
                    var typeHandle = _spirvCross.CompilerGetTypeHandle(spvcCompiler, image.BaseTypeId);
                    var isArray = _spirvCross.TypeGetImageArrayed(typeHandle) != 0;

                    metadata.Bindings.Add(new ShaderBinding
                    {
                        Binding = binding,
                        Type = isArray ? ShaderBindingType.Texture2DArray : ShaderBindingType.Texture2D,
                        Name = name
                    });
                }
            }

            // Reflect samplers
            ReflectedResource* samplers = null;
            nuint samplerCount = 0;
            result = _spirvCross.ResourcesGetResourceListForType(resources, ResourceType.SeparateSamplers, &samplers, &samplerCount);
            if (result == Result.Success && samplerCount > 0)
            {
                for (nuint i = 0; i < samplerCount; i++)
                {
                    var sampler = samplers[i];
                    var binding = _spirvCross.CompilerGetDecoration(spvcCompiler, sampler.Id, Silk.NET.SPIRV.Decoration.Binding);
                    var namePtr = _spirvCross.CompilerGetName(spvcCompiler, sampler.Id);
                    var name = namePtr != null ? Marshal.PtrToStringUTF8((nint)namePtr) ?? "unknown" : "unknown";

                    metadata.Bindings.Add(new ShaderBinding
                    {
                        Binding = binding,
                        Type = ShaderBindingType.Sampler,
                        Name = name
                    });
                }
            }

            // Sort by binding number for consistent ordering
            metadata.Bindings.Sort((a, b) => a.Binding.CompareTo(b.Binding));

            return metadata;
        }
        finally
        {
            _spirvCross.ContextDestroy(context);
        }
    }

    public static string? CompileSpirvToWgsl(byte[] spirvBytes, out string? error)
    {
        error = null;

        // Write SPIR-V to temporary file
        var tempSpirvPath = Path.GetTempFileName();
        var tempWgslPath = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(tempSpirvPath, spirvBytes);

            // Try to find tint executable
            var tintPath = FindTintExecutable();
            if (tintPath == null)
            {
                error = "Tint executable not found. Please install Google Tint or add it to PATH.";
                return null;
            }

            // Call tint to convert SPIR-V to WGSL
            var startInfo = new ProcessStartInfo
            {
                FileName = tintPath,
                Arguments = $"\"{tempSpirvPath}\" -o \"{tempWgslPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "Failed to start Tint process";
                return null;
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                error = $"Tint conversion failed: {stderr}";
                return null;
            }

            if (!File.Exists(tempWgslPath))
            {
                error = "Tint did not produce output file";
                return null;
            }

            return File.ReadAllText(tempWgslPath);
        }
        catch (Exception ex)
        {
            error = $"Exception during SPIR-V to WGSL conversion: {ex.Message}";
            return null;
        }
        finally
        {
            // Clean up temp files
            if (File.Exists(tempSpirvPath))
                File.Delete(tempSpirvPath);
            if (File.Exists(tempWgslPath))
                File.Delete(tempWgslPath);
        }
    }

    public static string? FindTintExecutable()
    {
        // Check if tint is in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var tintPath = Path.Combine(path, OperatingSystem.IsWindows() ? "tint.exe" : "tint");
                if (File.Exists(tintPath))
                    return tintPath;
            }
        }

        // Check common installation locations on Windows
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var possiblePaths = new[]
            {
                Path.Combine(programFiles, "tint", "tint.exe"),
                Path.Combine(programFiles, "Google", "Tint", "tint.exe"),
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    public static (string? wgsl, string? error) CompileGlslToWgsl(string glslSource, ShaderStage stage, string filename)
    {
        var spirv = CompileGlslToSpirv(glslSource, stage, filename, out var error);
        if (spirv == null)
            return (null, error);

        var wgsl = CompileSpirvToWgsl(spirv, out error);
        return (wgsl, error);
    }
}
