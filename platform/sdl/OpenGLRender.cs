//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
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
    private byte _nextShaderId = 2;  // 1 is reserved for sprite shader
    private ulong _nextFenceId = 1;

    private readonly Dictionary<uint, uint> _buffers = new();           // Handle -> GL buffer
    private readonly Dictionary<ushort, uint> _textures = new();        // Handle -> GL texture
    private readonly Dictionary<byte, uint> _shaders = new();           // Handle -> GL program
    private readonly Dictionary<ulong, nint> _fences = new();           // Handle -> GLsync

    // VAO for MeshVertex format
    private uint _meshVao;
    private uint _boundVertexBuffer;
    private uint _boundIndexBuffer;
    private uint _boundShader;

    // Default sprite shader source
    // Matches MeshVertex layout: position, uv, normal, color, opacity, depth, bone, atlas
    private const string SpriteVertexShader = @"#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec2 aNormal;
layout(location = 3) in vec4 aColor;
layout(location = 4) in float aOpacity;
layout(location = 5) in float aDepth;
layout(location = 6) in int aBone;
layout(location = 7) in int aAtlas;

uniform mat4 uProjection;

out vec2 vUV;
out vec4 vColor;
flat out int vAtlas;

void main()
{
    gl_Position = uProjection * vec4(aPosition, aDepth, 1.0);
    vUV = aUV;
    vColor = aColor * aOpacity;
    vAtlas = aAtlas;
}";

    private const string SpriteFragmentShader = @"#version 330 core
in vec2 vUV;
in vec4 vColor;
flat in int vAtlas;

uniform sampler2D uTexture;

out vec4 FragColor;

void main()
{
    vec4 texColor = texture(uTexture, vUV);
    FragColor = texColor * vColor;
}";

    public void Init(RenderBackendConfig config)
    {
        _config = config;

        // Create GL context using SDL's GetProcAddress
        _gl = GL.GetApi(name => (nint)SDL_GL_GetProcAddress(name));

        // Enable standard blend mode
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

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

        // Create built-in sprite shader
        var spriteProgram = CreateShaderProgram(SpriteVertexShader, SpriteFragmentShader);
        _shaders[ShaderHandle.Sprite.Id] = spriteProgram;

        // Set texture uniform to slot 0
        _gl.UseProgram(spriteProgram);
        var texLoc = _gl.GetUniformLocation(spriteProgram, "uTexture");
        if (texLoc >= 0)
            _gl.Uniform1(texLoc, 0);
    }

    public void Shutdown()
    {
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

        // Setup vertex attributes for MeshVertex layout (56 bytes total)
        // Offsets: position(0), uv(8), normal(16), color(24), opacity(40), depth(44), bone(48), atlas(52)
        uint stride = (uint)MeshVertex.SizeInBytes;

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

        // Depth: float at offset 44
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, (void*)44);

        // Bone: int at offset 48
        _gl.EnableVertexAttribArray(6);
        _gl.VertexAttribIPointer(6, 1, VertexAttribIType.Int, stride, (void*)48);

        // Atlas: int at offset 52
        _gl.EnableVertexAttribArray(7);
        _gl.VertexAttribIPointer(7, 1, VertexAttribIType.Int, stride, (void*)52);
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

    // === Shader Management ===

    public ShaderHandle CreateShader(string vertexSource, string fragmentSource)
    {
        var program = CreateShaderProgram(vertexSource, fragmentSource);
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

    public void SetUniformMatrix4x4(string name, in Matrix4x4 value)
    {
        if (_boundShader == 0)
        {
            Console.WriteLine($"[WARN] SetUniformMatrix4x4: No shader bound");
            return;
        }

        var location = _gl.GetUniformLocation(_boundShader, name);
        if (location < 0)
        {
            Console.WriteLine($"[WARN] SetUniformMatrix4x4: Uniform '{name}' not found in shader {_boundShader}");
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

        // Wait with a 1 second timeout
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

    private uint CreateShaderProgram(string vertexSource, string fragmentSource)
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
            throw new Exception($"Vertex shader compilation failed: {info}");
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
            throw new Exception($"Fragment shader compilation failed: {info}");
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
            throw new Exception($"Shader program linking failed: {info}");
        }

        // Cleanup - shaders are now part of the program
        _gl.DetachShader(program, vertexShader);
        _gl.DetachShader(program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
    }
}
