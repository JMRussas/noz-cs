//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Silk.NET.OpenGL;
using static SDL.SDL3;

namespace noz.Platform;

public unsafe class OpenGLRender : IRender
{
    private RenderBackendConfig _config = null!;
    private GL _gl = null!;

    public string ShaderExtension => ".glsl";

    // Resource tracking
    private uint _nextBufferId = 1;
    private uint _nextTextureId = 2; // 1 is reserved for white texture
    private byte _nextShaderId = 1;
    private ulong _nextFenceId = 1;

    private readonly Dictionary<uint, uint> _buffers = new();           // Handle -> GL buffer
    private readonly Dictionary<ushort, uint> _textures = new();        // Handle -> GL texture
    private readonly Dictionary<byte, uint> _shaders = new();           // Handle -> GL program
    private readonly Dictionary<ulong, nint> _fences = new();           // Handle -> GL sync

    // VAO for MeshVertex format
    private uint _meshVao;
    private uint _boundVertexBuffer;
    private uint _boundIndexBuffer;
    private uint _boundShader;

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

    public void Init(RenderBackendConfig config)
    {
        _config = config;

        // Create GL context using SDL's GetProcAddress
        _gl = GL.GetApi(name => SDL_GL_GetProcAddress(name));

        // Enable standard blend mode
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Enable MSAA if available
        _gl.Enable(EnableCap.Multisample);

        // Create VAO for MeshVertex format
        _meshVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_meshVao);

        // Setup vertex attributes (will be bound when vertex buffer is bound)
        // Position: location 0, vec3, offset 0
        // UV: location 1, vec2, offset 12
        // Color: location 2, vec4 (normalized bytes), offset 20

        // Create built-in white texture (1x1 white pixel)
        byte[] whitePixel = [255, 255, 255, 255];
        var whiteTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, whiteTex);
        fixed (byte* p = whitePixel)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _textures[TextureHandle.White.Id] = whiteTex;

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
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quadVertices.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        }

        // Position attribute
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        // UV attribute
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
        foreach (var buffer in _buffers.Values)
            _gl.DeleteBuffer(buffer);
        _buffers.Clear();

        foreach (var texture in _textures.Values)
            _gl.DeleteTexture(texture);
        _textures.Clear();

        foreach (var shader in _shaders.Values)
            _gl.DeleteProgram(shader);
        _shaders.Clear();

        foreach (var fence in _fences.Values)
            _gl.DeleteSync(fence);
        _fences.Clear();

        _gl.DeleteVertexArray(_meshVao);
        _gl.Dispose();
    }

    public void BeginFrame()
    {
        _gl.BindVertexArray(_meshVao);
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

    // === Buffer Management ===

    public BufferHandle CreateVertexBuffer(int sizeInBytes, BufferUsage usage)
    {
        var glBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, glBuffer);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)sizeInBytes, null, ToGLUsage(usage));

        var handle = new BufferHandle(_nextBufferId++);
        _buffers[handle.Id] = glBuffer;
        return handle;
    }

    public BufferHandle CreateIndexBuffer(int sizeInBytes, BufferUsage usage)
    {
        var glBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, glBuffer);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)sizeInBytes, null, ToGLUsage(usage));

        var handle = new BufferHandle(_nextBufferId++);
        _buffers[handle.Id] = glBuffer;
        return handle;
    }

    public void DestroyBuffer(BufferHandle handle)
    {
        if (_buffers.TryGetValue(handle.Id, out var glBuffer))
        {
            _gl.DeleteBuffer(glBuffer);
            _buffers.Remove(handle.Id);
        }
    }

    public void UpdateVertexBufferRange(BufferHandle buffer, int offsetBytes, ReadOnlySpan<MeshVertex> data)
    {
        if (!_buffers.TryGetValue(buffer.Id, out var glBuffer))
            return;

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, glBuffer);
        fixed (MeshVertex* p = data)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, offsetBytes, (nuint)(data.Length * MeshVertex.SizeInBytes), p);
        }
    }

    public void UpdateIndexBufferRange(BufferHandle buffer, int offsetBytes, ReadOnlySpan<ushort> data)
    {
        if (!_buffers.TryGetValue(buffer.Id, out var glBuffer))
            return;

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, glBuffer);
        fixed (ushort* p = data)
        {
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, offsetBytes, (nuint)(data.Length * sizeof(ushort)), p);
        }
    }

    public void BindVertexBuffer(BufferHandle buffer)
    {
        if (!_buffers.TryGetValue(buffer.Id, out var glBuffer))
            return;

        if (_boundVertexBuffer == glBuffer)
            return;

        _boundVertexBuffer = glBuffer;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, glBuffer);

        // Setup vertex attributes for MeshVertex layout (68 bytes total)
        // Offsets: position(0), uv(8), normal(16), color(24), opacity(40), bone(44), atlas(48),
        //          frameCount(52), frameWidth(56), frameRate(60), animStartTime(64)
        uint stride = MeshVertex.SizeInBytes;

        // Position: vec2 at offset 0
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        // UV: vec2 at offset 8
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);

        // Normal: vec2 at offset 16
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)16);

        // Color: vec4 at offset 24
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)24);

        // Opacity: float at offset 40
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, (void*)40);

        // Bone: int at offset 44
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribIPointer(5, 1, VertexAttribIType.Int, stride, (void*)44);

        // Atlas: int at offset 48
        _gl.EnableVertexAttribArray(6);
        _gl.VertexAttribIPointer(6, 1, VertexAttribIType.Int, stride, (void*)48);

        // FrameCount: int at offset 52
        _gl.EnableVertexAttribArray(7);
        _gl.VertexAttribIPointer(7, 1, VertexAttribIType.Int, stride, (void*)52);

        // FrameWidth: float at offset 56
        _gl.EnableVertexAttribArray(8);
        _gl.VertexAttribPointer(8, 1, VertexAttribPointerType.Float, false, stride, (void*)56);

        // FrameRate: float at offset 60
        _gl.EnableVertexAttribArray(9);
        _gl.VertexAttribPointer(9, 1, VertexAttribPointerType.Float, false, stride, (void*)60);

        // AnimStartTime: float at offset 64
        _gl.EnableVertexAttribArray(10);
        _gl.VertexAttribPointer(10, 1, VertexAttribPointerType.Float, false, stride, (void*)64);
    }

    public void BindIndexBuffer(BufferHandle buffer)
    {
        if (!_buffers.TryGetValue(buffer.Id, out var glBuffer))
            return;

        if (_boundIndexBuffer == glBuffer)
            return;

        _boundIndexBuffer = glBuffer;
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, glBuffer);
    }

    // === Texture Management ===

    public TextureHandle CreateTexture(int width, int height, ReadOnlySpan<byte> data)
    {
        var glTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, glTexture);

        fixed (byte* p = data)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        var handle = new TextureHandle((ushort)_nextTextureId++);
        _textures[handle.Id] = glTexture;
        return handle;
    }

    public void UpdateTexture(TextureHandle handle, int width, int height, ReadOnlySpan<byte> data)
    {
        if (!_textures.TryGetValue(handle.Id, out var glTexture))
            return;

        _gl.BindTexture(TextureTarget.Texture2D, glTexture);
        fixed (byte* p = data)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
    }

    public void DestroyTexture(TextureHandle handle)
    {
        if (_textures.TryGetValue(handle.Id, out var glTexture))
        {
            _gl.DeleteTexture(glTexture);
            _textures.Remove(handle.Id);
        }
    }

    public void BindTexture(int slot, TextureHandle handle)
    {
        if (!_textures.TryGetValue(handle.Id, out var glTexture))
            return;

        _gl.ActiveTexture(TextureUnit.Texture0 + slot);
        _gl.BindTexture(TextureTarget.Texture2D, glTexture);
    }

    // === Texture Array Management ===

    private readonly Dictionary<ushort, (uint glTexture, int width, int height, int layers)> _textureArrays = new();

    public TextureHandle CreateTextureArray(int width, int height, int layers)
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

        var handle = new TextureHandle((ushort)_nextTextureId++);
        _textureArrays[handle.Id] = (glTexture, width, height, layers);
        return handle;
    }

    public void UpdateTextureArrayLayer(TextureHandle handle, int layer, ReadOnlySpan<byte> data)
    {
        if (!_textureArrays.TryGetValue(handle.Id, out var info))
            return;

        _gl.BindTexture(TextureTarget.Texture2DArray, info.glTexture);
        fixed (byte* p = data)
        {
            _gl.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                0, 0, layer,
                (uint)info.width, (uint)info.height, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
    }

    public void BindTextureArray(int slot, TextureHandle handle)
    {
        if (!_textureArrays.TryGetValue(handle.Id, out var info))
            return;

        _gl.ActiveTexture(TextureUnit.Texture0 + slot);
        _gl.BindTexture(TextureTarget.Texture2DArray, info.glTexture);
    }

    // === Shader Management ===

    public ShaderHandle CreateShader(string name, string vertexSource, string fragmentSource)
    {
        var program = CreateShaderProgram(name, vertexSource, fragmentSource);
        var handle = new ShaderHandle(_nextShaderId++);
        _shaders[handle.Id] = program;
        return handle;
    }

    public void DestroyShader(ShaderHandle handle)
    {
        if (_shaders.TryGetValue(handle.Id, out var program))
        {
            _gl.DeleteProgram(program);
            _shaders.Remove(handle.Id);
        }
    }

    public void BindShader(ShaderHandle handle)
    {
        if (!_shaders.TryGetValue(handle.Id, out var program))
        {
            Console.WriteLine($"[WARN] BindShader: Shader handle {handle.Id} not found!");
            return;
        }

        if (_boundShader == program)
            return;

        _boundShader = program;
        _gl.UseProgram(program);
    }

    private static bool _setUniformMatrixWarning = false;
    private static bool _getUniformLocationWarning = false;
    
    public void SetUniformMatrix4x4(string name, in Matrix4x4 value)
    {
        if (_boundShader == 0)
        {
            if (!_setUniformMatrixWarning)
            {
                Console.WriteLine($"[WARN] SetUniformMatrix4x4: No shader bound");
                _setUniformMatrixWarning = true;
            }

            return;
        }

        var location = _gl.GetUniformLocation(_boundShader, name);
        if (location < 0)
        {
            if (!_getUniformLocationWarning)
            {
                Console.WriteLine($"[WARN] SetUniformMatrix4x4: Uniform '{name}' not found in shader {_boundShader}");
                _getUniformLocationWarning = true;
            }
            return;
        }

        // Matrix4x4 is row-major in .NET, OpenGL expects column-major
        // We need to transpose
        Span<float> data = stackalloc float[16];
        data[0] = value.M11; data[1] = value.M21; data[2] = value.M31; data[3] = value.M41;
        data[4] = value.M12; data[5] = value.M22; data[6] = value.M32; data[7] = value.M42;
        data[8] = value.M13; data[9] = value.M23; data[10] = value.M33; data[11] = value.M43;
        data[12] = value.M14; data[13] = value.M24; data[14] = value.M34; data[15] = value.M44;

        fixed (float* p = data)
        {
            _gl.UniformMatrix4(location, 1, false, p);
        }
    }

    public void SetUniformInt(string name, int value)
    {
        if (_boundShader == 0)
            return;

        var location = _gl.GetUniformLocation(_boundShader, name);
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    public void SetUniformFloat(string name, float value)
    {
        if (_boundShader == 0)
            return;

        var location = _gl.GetUniformLocation(_boundShader, name);
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    public void SetUniformVec2(string name, Vector2 value)
    {
        if (_boundShader == 0)
            return;

        var location = _gl.GetUniformLocation(_boundShader, name);
        if (location >= 0)
            _gl.Uniform2(location, value.X, value.Y);
    }

    public void SetUniformVec4(string name, Vector4 value)
    {
        if (_boundShader == 0)
            return;

        var location = _gl.GetUniformLocation(_boundShader, name);
        if (location >= 0)
            _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public void SetBoneTransforms(ReadOnlySpan<Matrix3x2> bones)
    {
        if (_boundShader == 0)
            return;

        var location = _gl.GetUniformLocation(_boundShader, "uBones");
        if (location < 0)
            return;

        // Each Matrix3x2 becomes 2 vec3s (column-major)
        // M11 M21 M31 -> col0 (x, y, translation.x)
        // M12 M22 M32 -> col1 (x, y, translation.y)
        Span<float> data = stackalloc float[bones.Length * 6];
        for (int i = 0; i < bones.Length; i++)
        {
            var m = bones[i];
            int idx = i * 6;
            // First vec3: column 0 (M11, M21) + translation.x (M31)
            data[idx + 0] = m.M11;
            data[idx + 1] = m.M21;
            data[idx + 2] = m.M31;
            // Second vec3: column 1 (M12, M22) + translation.y (M32)
            data[idx + 3] = m.M12;
            data[idx + 4] = m.M22;
            data[idx + 5] = m.M32;
        }

        fixed (float* p = data)
        {
            _gl.Uniform3(location, (uint)(bones.Length * 2), p);
        }
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

    public void DrawIndexedRange(int firstIndex, int indexCount, int baseVertex = 0)
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

    // === Synchronization ===

    public FenceHandle CreateFence()
    {
        var fence = _gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
        var handle = new FenceHandle(_nextFenceId++);
        _fences[handle.Id] = fence;
        return handle;
    }

    public void WaitFence(FenceHandle fence)
    {
        if (!_fences.TryGetValue(fence.Id, out var glFence))
            return;

        _gl.ClientWaitSync(glFence, SyncObjectMask.Bit, 1_000_000_000);
    }

    public void DeleteFence(FenceHandle fence)
    {
        if (_fences.TryGetValue(fence.Id, out var glFence))
        {
            _gl.DeleteSync(glFence);
            _fences.Remove(fence.Id);
        }
    }

    // === Private Helpers ===

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

        return program;
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
        // Bind MSAA framebuffer if available, otherwise resolve framebuffer
        var fb = _msaaFramebuffer != 0 ? _msaaFramebuffer : _offscreenFramebuffer;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fb);
        _gl.Viewport(0, 0, (uint)_offscreenWidth, (uint)_offscreenHeight);
        _gl.ClearColor(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

        // Rebind mesh VAO for scene rendering
        _gl.BindVertexArray(_meshVao);
        _boundVertexBuffer = 0;
        _boundIndexBuffer = 0;
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

    public void Composite(ShaderHandle compositeShader)
    {
        if (!_shaders.TryGetValue(compositeShader.Id, out var program))
            return;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_offscreenWidth, (uint)_offscreenHeight);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);

        // Bind scene texture and shader, draw fullscreen quad
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _offscreenTexture);
        _gl.UseProgram(program);
        _boundShader = program;

        _gl.BindVertexArray(_fullscreenVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(_meshVao);
    }
}
