//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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
    Spirv
}

public static class ShaderCompiler
{
    private static Shaderc? _shaderc;
    private static Cross? _spirvCross;

    public static void Initialize()
    {
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
}
