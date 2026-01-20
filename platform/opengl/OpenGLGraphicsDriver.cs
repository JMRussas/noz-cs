//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Silk.NET.OpenGL;

namespace NoZ.Platform;

public unsafe class OpenGLGraphicsDriver : IGraphicsDriver
{
    private GraphicsDriverConfig _config = null!;
    private GL _gl = null!;

    public string ShaderExtension => ""; // todo: handle gles ".glsl";

    // Resource tracking - arrays indexed by handle
    private const int MaxBuffers = 256;
    private const int MaxTextures = 1024;
    private const int MaxShaders = 64;
    private const int MaxFences = 16;
    private const int MaxMeshes = 32;

    private int _nextBufferId = 1;
    private int _nextTextureId = 2; // 1 is reserved for white texture
    private int _nextShaderId = 1;
    private int _nextFenceId = 1;
    private int _nextMeshId = 1;
    private uint _boundShader = 0;

    private struct TextureInfo
    {
        public uint GlTexture;
        public int Width;
        public int Height;
        public int Layers;
        public bool IsArray;
        public TextureFormat Format;
    }

    private readonly uint[] _buffers = new uint[MaxBuffers];
    private readonly TextureInfo[] _textures = new TextureInfo[MaxTextures];
    private readonly uint[] _shaders = new uint[MaxShaders];
    private readonly nint[] _fences = new nint[MaxFences];
    private readonly (uint vao, uint vbo, uint ebo, int stride)[] _meshes = new (uint, uint, uint, int)[MaxMeshes];

    // Offscreen render target
    private uint _offscreenFramebuffer;
    private uint _offscreenTexture;
    private uint _offscreenDepthRenderbuffer;
    private uint _msaaFramebuffer;
    private uint _msaaColorRenderbuffer;
    private uint _msaaDepthRenderbuffer;
    private int _offscreenWidth;
    private int _offscreenHeight;
    private int _msaaSamples;

    // Fullscreen quad for composite pass
    private uint _fullscreenVao;
    private uint _fullscreenVbo;

    public void Init(GraphicsDriverConfig config)
    {
        _config = config;

        _gl = GL.GetApi(config.Platform.GetGraphicsProcAddress);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Enable(EnableCap.Multisample);

        // Create fullscreen quad VAO/VBO
        // Positions and UVs for a fullscreen quad (two triangles)
        float[] quadVertices =
        [
            // Position    UV
            -1f, -1f,      0f, 0f,
             1f, -1f,      1f, 0f,
             1f,  1f,      1f, 1f,
            -1f, -1f,      0f, 0f,
             1f,  1f,      1f, 1f,
            -1f,  1f,      0f, 1f,
        ];

        _fullscreenVao = _gl.GenVertexArray();
        _fullscreenVbo = _gl.GenBuffer();

        _gl.BindVertexArray(_fullscreenVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _fullscreenVbo);
        fixed (float* p = quadVertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quadVertices.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public void Shutdown()
    {
        // Clean up offscreen target
        DestroyOffscreenTarget();

        // Clean up fullscreen quad resources
        if (_fullscreenVbo != 0)
            _gl.DeleteBuffer(_fullscreenVbo);
        if (_fullscreenVao != 0)
            _gl.DeleteVertexArray(_fullscreenVao);

        // Clean up resources
        for (var i = 0; i < MaxBuffers; i++)
            if (_buffers[i] != 0)
                _gl.DeleteBuffer(_buffers[i]);

        for (var i = 0; i < MaxTextures; i++)
            if (_textures[i].GlTexture != 0)
                _gl.DeleteTexture(_textures[i].GlTexture);

        for (var i = 0; i < MaxShaders; i++)
            if (_shaders[i] != 0)
                _gl.DeleteProgram(_shaders[i]);

        for (var i = 0; i < MaxFences; i++)
            if (_fences[i] != 0)
                _gl.DeleteSync(_fences[i]);

        for (var i = 0; i < MaxMeshes; i++)
        {
            ref var va = ref _meshes[i];
            if (va.vao != 0)
            {
                _gl.DeleteVertexArray(va.vao);
                _gl.DeleteBuffer(va.vbo);
                _gl.DeleteBuffer(va.ebo);
            }
        }

        _gl.Dispose();
    }

    public void BeginFrame()
    {
    }

    public void EndFrame()
    {
        // Note: SwapBuffers is handled by IPlatform, not IRender
    }

    public void Clear(Color color)
    {
        _gl.ClearColor(color.R, color.G, color.B, color.A);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        _gl.Viewport(x, y, (uint)width, (uint)height);
    }

    public void SetScissor(int x, int y, int width, int height)
    {
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x, y, (uint)width, (uint)height);
    }

    public void DisableScissor()
    {
        _gl.Disable(EnableCap.ScissorTest);
    }

    // === Mesh Management ===

    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        var descriptor = T.GetFormatDescriptor();

        var vao = _gl.GenVertexArray();
        var vbo = _gl.GenBuffer();
        var ebo = _gl.GenBuffer();

        _gl.BindVertexArray(vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(maxVertices * descriptor.Stride), null, ToGLUsage(usage));

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(maxIndices * sizeof(ushort)), null, ToGLUsage(usage));

        foreach (var attr in descriptor.Attributes)
        {
            _gl.EnableVertexAttribArray((uint)attr.Location);

            var glType = attr.Type switch
            {
                VertexAttribType.Float => VertexAttribPointerType.Float,
                VertexAttribType.Int => VertexAttribPointerType.Int,
                VertexAttribType.UByte => VertexAttribPointerType.UnsignedByte,
                _ => VertexAttribPointerType.Float
            };

            if (attr.Type == VertexAttribType.Int)
            {
                const VertexAttribIType glIType = VertexAttribIType.Int;
                _gl.VertexAttribIPointer(
                    (uint)attr.Location,
                    attr.Components,
                    glIType,
                    (uint)descriptor.Stride,
                    (void*)attr.Offset);
            }
            else
            {
                _gl.VertexAttribPointer(
                    (uint)attr.Location,
                    attr.Components,
                    glType,
                    attr.Normalized,
                    (uint)descriptor.Stride,
                    (void*)attr.Offset);
            }
        }

        if (!string.IsNullOrEmpty(name))
        {
            SetDebugLabel(ObjectIdentifier.VertexArray, vao, name);
            SetDebugLabel(ObjectIdentifier.Buffer, vbo, $"{name}.VBO");
            SetDebugLabel(ObjectIdentifier.Buffer, ebo, $"{name}.EBO");
        }

        _gl.BindVertexArray(0);

        var handle = _nextMeshId++;
        _meshes[handle] = (vao, vbo, ebo, descriptor.Stride);
        return (nuint)handle;
    }

    public void DestroyMesh(nuint handle)
    {
        ref var va = ref _meshes[(int)handle];
        if (va.vao != 0)
        {
            _gl.DeleteVertexArray(va.vao);
            _gl.DeleteBuffer(va.vbo);
            _gl.DeleteBuffer(va.ebo);
            va = (0, 0, 0, 0);
        }
    }

    public void BindMesh(nuint handle)
    {
        ref var va = ref _meshes[(int)handle];
        if (va.vao == 0) return;
        _gl.BindVertexArray(va.vao);
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        ref var va = ref _meshes[(int)handle];
        if (va.vao == 0) return;

        if (vertexData.Length > 0)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, va.vbo);
            fixed (byte* p = vertexData)
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)vertexData.Length, p);
        }

        if (indexData.Length > 0)
        {
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, va.ebo);
            fixed (ushort* p = indexData)
                _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)(indexData.Length * sizeof(ushort)), p);
        }
    }

    // === Buffer Management ===

    public void DestroyBuffer(nuint handle)
    {
        var glBuffer = _buffers[(int)handle];
        if (glBuffer != 0)
        {
            _gl.DeleteBuffer(glBuffer);
            _buffers[(int)handle] = 0;
        }
    }

    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        var glBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, glBuffer);
        _gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)sizeInBytes, null, ToGLUsage(usage));
        SetDebugLabel(ObjectIdentifier.Buffer, glBuffer, name);

        var handle = _nextBufferId++;
        _buffers[handle] = glBuffer;
        return (nuint)handle;
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        var glBuffer = _buffers[(int)buffer];
        if (glBuffer == 0) return;

        _gl.BindBuffer(BufferTargetARB.UniformBuffer, glBuffer);
        fixed (byte* p = data)
        {
            _gl.BufferSubData(BufferTargetARB.UniformBuffer, offsetBytes, (nuint)data.Length, p);
        }
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        var glBuffer = _buffers[(int)buffer];
        if (glBuffer == 0) return;

        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, (uint)slot, glBuffer);
    }

    public nuint CreateTexture(
        int width,
        int height,
        ReadOnlySpan<byte> data,
        TextureFormat format,
        TextureFilter filter)
    {
        var glTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, glTexture);

        var (internalFormat, pixelFormat, pixelType) = format switch
        {
            TextureFormat.R8 => (InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            TextureFormat.RG8 => (InternalFormat.RG8, PixelFormat.RG, PixelType.UnsignedByte),
            TextureFormat.RGB8 => (InternalFormat.Rgb8, PixelFormat.Rgb, PixelType.UnsignedByte),
            TextureFormat.RGBA32F => (InternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float),
            _ => (InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte)
        };

        fixed (byte* p = data)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                (uint)width, (uint)height, 0, pixelFormat, pixelType, p);
        }

        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)ToTextureMinFilter(filter));
        _gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)ToTextureMaxFilter(filter));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        var handle = _nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            GlTexture = glTexture,
            Width = width,
            Height = height,
            Layers = 0,
            IsArray = false,
            Format = format
        };
        return (nuint)handle;
    }

    public void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data)
    {
        ref var info = ref _textures[(int)handle];
        if (info.GlTexture == 0) return;

        var (pixelFormat, pixelType) = info.Format switch
        {
            TextureFormat.R8 => (PixelFormat.Red, PixelType.UnsignedByte),
            TextureFormat.RG8 => (PixelFormat.RG, PixelType.UnsignedByte),
            TextureFormat.RGB8 => (PixelFormat.Rgb, PixelType.UnsignedByte),
            TextureFormat.RGBA32F => (PixelFormat.Rgba, PixelType.Float),
            _ => (PixelFormat.Rgba, PixelType.UnsignedByte)
        };

        _gl.BindTexture(TextureTarget.Texture2D, info.GlTexture);
        fixed (byte* p = data)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                (uint)width, (uint)height, pixelFormat, pixelType, p);
        }
    }

    public void DestroyTexture(nuint handle)
    {
        ref var info = ref _textures[(int)handle];
        if (info.GlTexture != 0)
        {
            _gl.DeleteTexture(info.GlTexture);
            info = default;
        }
    }

    public void BindTexture(nuint handle, int slot)
    {
        ref var info = ref _textures[(int)handle];
        if (info.GlTexture == 0) return;

        _gl.ActiveTexture(TextureUnit.Texture0 + slot);
        if (info.IsArray)
            _gl.BindTexture(TextureTarget.Texture2DArray, info.GlTexture);
        else
            _gl.BindTexture(TextureTarget.Texture2D, info.GlTexture);
    }

    // === Texture Array Management ===

    public nuint CreateTextureArray(int width, int height, int layers)
    {
        var glTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, glTexture);

        // Allocate storage for all layers
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8,
            (uint)width, (uint)height, (uint)layers, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);

        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        var handle = _nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            GlTexture = glTexture,
            Width = width,
            Height = height,
            Layers = layers,
            IsArray = true
        };
        return (nuint)handle;
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name)
    {
        var layers = layerData.Length;
        var glTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, glTexture);

        if (name != null)
            SetDebugLabel(ObjectIdentifier.Texture, glTexture, name);

        var internalFormat = format switch
        {
            TextureFormat.RGBA8 => InternalFormat.Rgba8,
            TextureFormat.RGB8 => InternalFormat.Rgb8,
            TextureFormat.RG8 => InternalFormat.RG8,
            TextureFormat.R8 => InternalFormat.R8,
            _ => InternalFormat.Rgba8
        };

        var pixelFormat = format switch
        {
            TextureFormat.RGBA8 => PixelFormat.Rgba,
            TextureFormat.RGB8 => PixelFormat.Rgb,
            TextureFormat.RG8 => PixelFormat.RG,
            TextureFormat.R8 => PixelFormat.Red,
            _ => PixelFormat.Rgba
        };

        // Allocate storage for all layers
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, internalFormat,
            (uint)width, (uint)height, (uint)layers, 0,
            pixelFormat, PixelType.UnsignedByte, null);

        // Upload each layer
        for (int i = 0; i < layers; i++)
        {
            fixed (byte* p = layerData[i])
            {
                _gl.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                    0, 0, i,
                    (uint)width, (uint)height, 1,
                    pixelFormat, PixelType.UnsignedByte, p);
            }
        }

        var minFilter = filter == TextureFilter.Nearest ? TextureMinFilter.Nearest : TextureMinFilter.Linear;
        var magFilter = filter == TextureFilter.Nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear;

        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)minFilter);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)magFilter);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        var handle = _nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            GlTexture = glTexture,
            Width = width,
            Height = height,
            Layers = layers,
            IsArray = true
        };
        return (nuint)handle;
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        ref var info = ref _textures[(int)handle];
        if (info.GlTexture == 0 || !info.IsArray) return;

        _gl.BindTexture(TextureTarget.Texture2DArray, info.GlTexture);
        fixed (byte* p = data)
        {
            _gl.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                0, 0, layer,
                (uint)info.Width, (uint)info.Height, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
    }

    // === Shader Management ===

    public nuint CreateShader(string name, string vertexSource, string fragmentSource)
    {
        var program = CreateShaderProgram(name, vertexSource, fragmentSource);
        var handle = _nextShaderId++;
        _shaders[handle] = program;
        return (nuint)handle;
    }

    public void DestroyShader(nuint handle)
    {
        var program = _shaders[(int)handle];
        if (program != 0)
        {
            _gl.DeleteProgram(program);
            _shaders[(int)handle] = 0;
        }
    }

    public void BindShader(nuint handle)
    {
        var program = _shaders[(int)handle];
        if (program == 0)
        {
            Console.WriteLine($"[WARN] BindShader: Shader handle {handle} not found!");
            return;
        }

        if (_boundShader == program)
            return;

        _boundShader = program;
        _gl.UseProgram(program);
    }

    // === State Management ===

    public void SetBlendMode(BlendMode mode)
    {
        switch (mode)
        {
            case BlendMode.None:
                _gl.Disable(EnableCap.Blend);
                break;

            case BlendMode.Alpha:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;

            case BlendMode.Additive:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;

            case BlendMode.Multiply:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);
                break;

            case BlendMode.Premultiplied:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    // === Drawing ===

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        if (baseVertex == 0)
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedShort,
                (void*)(firstIndex * sizeof(ushort)));
        }
        else
        {
            _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedShort,
                (void*)(firstIndex * sizeof(ushort)), baseVertex);
        }
    }

    public nuint CreateFence()
    {
        var fence = _gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
        var handle = _nextFenceId++;
        _fences[handle] = fence;
        return (nuint)handle;
    }

    public void WaitFence(nuint fence)
    {
        var glFence = _fences[(int)fence];
        if (glFence == 0) return;

        _gl.ClientWaitSync(glFence, SyncObjectMask.Bit, 1_000_000_000);
    }

    public void DeleteFence(nuint fence)
    {
        var glFence = _fences[(int)fence];
        if (glFence != 0)
        {
            _gl.DeleteSync(glFence);
            _fences[(int)fence] = 0;
        }
    }

    // === Private Helpers ===

    private static TextureMinFilter ToTextureMinFilter(TextureFilter filter) => filter switch
    {
        TextureFilter.Nearest => TextureMinFilter.Nearest,
        _ => TextureMinFilter.Linear
    };

    private static TextureMagFilter ToTextureMaxFilter(TextureFilter filter) => filter switch
    {
        TextureFilter.Nearest => TextureMagFilter.Nearest,
        _ => TextureMagFilter.Linear
    };
    
    private static BufferUsageARB ToGLUsage(BufferUsage usage) => usage switch
    {
        BufferUsage.Static => BufferUsageARB.StaticDraw,
        BufferUsage.Dynamic => BufferUsageARB.DynamicDraw,
        BufferUsage.Stream => BufferUsageARB.StreamDraw,
        _ => BufferUsageARB.DynamicDraw
    };

    private uint CreateShaderProgram(string name, string vertexSource, string fragmentSource)
    {
        // Compile vertex shader
        var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexSource);
        _gl.CompileShader(vertexShader);

        _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vertexStatus);
        if (vertexStatus == 0)
        {
            var info = _gl.GetShaderInfoLog(vertexShader);
            _gl.DeleteShader(vertexShader);
            throw new Exception($"[{name}] Vertex shader: {info}");
        }

        // Compile fragment shader
        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentSource);
        _gl.CompileShader(fragmentShader);

        _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fragmentStatus);
        if (fragmentStatus == 0)
        {
            var info = _gl.GetShaderInfoLog(fragmentShader);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            throw new Exception($"[{name}] Fragment shader: {info}");
        }

        // Link program
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            var info = _gl.GetProgramInfoLog(program);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            _gl.DeleteProgram(program);
            throw new Exception($"[{name}] Link: {info}");
        }

        // Cleanup - shaders are now part of the program
        _gl.DetachShader(program, vertexShader);
        _gl.DetachShader(program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        SetDebugLabel(ObjectIdentifier.Program, program, name);

        return program;
    }

    private void SetDebugLabel(ObjectIdentifier type, uint id, string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        // Use -1 (0xFFFFFFFF) to let OpenGL calculate the length from null-terminated string
        _gl.ObjectLabel(type, id, unchecked((uint)-1), label);
    }

    // === Render Passes ===

    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        if (width == _offscreenWidth && height == _offscreenHeight && msaaSamples == _msaaSamples)
            return;

        DestroyOffscreenTarget();

        _offscreenWidth = width;
        _offscreenHeight = height;
        _msaaSamples = msaaSamples;

        // Create resolve texture (non-MSAA, for sampling)
        _offscreenTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _offscreenTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Create depth/stencil renderbuffer for resolve framebuffer
        _offscreenDepthRenderbuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _offscreenDepthRenderbuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);

        // Create resolve framebuffer (non-MSAA)
        _offscreenFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _offscreenFramebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _offscreenTexture, 0);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _offscreenDepthRenderbuffer);

        if (msaaSamples > 1)
        {
            // Create MSAA color renderbuffer
            _msaaColorRenderbuffer = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColorRenderbuffer);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)msaaSamples,
                InternalFormat.Rgba8, (uint)width, (uint)height);

            // Create MSAA depth renderbuffer
            _msaaDepthRenderbuffer = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepthRenderbuffer);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)msaaSamples,
                InternalFormat.Depth24Stencil8, (uint)width, (uint)height);

            // Create MSAA framebuffer (for rendering)
            _msaaFramebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFramebuffer);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, _msaaColorRenderbuffer);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, _msaaDepthRenderbuffer);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
    }

    private void DestroyOffscreenTarget()
    {
        if (_offscreenFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_offscreenFramebuffer);
            _offscreenFramebuffer = 0;
        }
        if (_offscreenTexture != 0)
        {
            _gl.DeleteTexture(_offscreenTexture);
            _offscreenTexture = 0;
        }
        if (_offscreenDepthRenderbuffer != 0)
        {
            _gl.DeleteRenderbuffer(_offscreenDepthRenderbuffer);
            _offscreenDepthRenderbuffer = 0;
        }
        if (_msaaFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_msaaFramebuffer);
            _msaaFramebuffer = 0;
        }
        if (_msaaColorRenderbuffer != 0)
        {
            _gl.DeleteRenderbuffer(_msaaColorRenderbuffer);
            _msaaColorRenderbuffer = 0;
        }
        if (_msaaDepthRenderbuffer != 0)
        {
            _gl.DeleteRenderbuffer(_msaaDepthRenderbuffer);
            _msaaDepthRenderbuffer = 0;
        }

        _offscreenWidth = 0;
        _offscreenHeight = 0;
        _msaaSamples = 0;
    }

    public void BeginScenePass(Color clearColor)
    {
        var fb = _msaaFramebuffer != 0 ? _msaaFramebuffer : _offscreenFramebuffer;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fb);
        _gl.Viewport(0, 0, (uint)_offscreenWidth, (uint)_offscreenHeight);
        _gl.ClearColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public void EndScenePass()
    {
        // If using MSAA, blit to resolve framebuffer
        if (_msaaFramebuffer == 0)
            return;

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFramebuffer);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _offscreenFramebuffer);
        _gl.BlitFramebuffer(
            0, 0, _offscreenWidth, _offscreenHeight,
            0, 0, _offscreenWidth, _offscreenHeight,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Composite(nuint compositeShader)
    {
        var program = _shaders[(int)compositeShader];
        if (program == 0) return;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_offscreenWidth, (uint)_offscreenHeight);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);

        // Bind scene texture and shader, draw fullscreen quad
        _gl.UseProgram(program);
        _boundShader = program;

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _offscreenTexture);

        var loc = _gl.GetUniformLocation(program, "sampler_texture");
        if (loc >= 0)
            _gl.Uniform1(loc, 0);

        _gl.BindVertexArray(_fullscreenVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }
}
