//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public static unsafe class Render
{
    private const int MaxSortGroups = 16;
    private const int MaxStateStack = 16;
    private const int MaxVertices = 65536;
    private const int MaxIndices = 196608;
    private const int MaxTextures = 2;
    private const int IndexShift = 0;
    private const int OrderShift = sizeof(ushort) * 1;
    private const int GroupShift = sizeof(ushort) * 2;
    private const int LayerShift = sizeof(ushort) * 3;

    private enum UniformType : byte { Float, Vec2, Vec4, Matrix4x4 }

    private struct UniformEntry
    {
        public UniformType Type;
        public string Name;
        public Vector4 Value;
        public Matrix4x4 MatrixValue;
    }

    private struct BatchState()
    {
        public nuint Shader;
        public fixed ulong Textures[MaxTextures];
        public BlendMode BlendMode;
        public int ViewportX;
        public int ViewportY;
        public int ViewportWidth;
        public int ViewportHeight;
        public bool ScissorEnabled;
        public int ScissorX;
        public int ScissorY;
        public int ScissorWidth;
        public int ScissorHeight;
    }

    private struct Batch
    {
        public int IndexOffset;
        public int IndexCount;
        public ushort State;
    }
    
    private struct State
    {
        public Color Color;
        public Shader? Shader;
        public Matrix3x2 Transform;
        public fixed ulong Textures[MaxTextures];
        public ushort SortLayer;
        public ushort SortGroup;
        public ushort SortIndex;
        public ushort BoneIndex;
        public BlendMode BlendMode;
        public int ViewportX;
        public int ViewportY;
        public int ViewportWidth;
        public int ViewportHeight;
        public bool ScissorEnabled;
        public int ScissorX;
        public int ScissorY;
        public int ScissorWidth;
        public int ScissorHeight;
    }
    
    public static RenderConfig Config { get; private set; } = null!;
    public static IRenderDriver Driver { get; private set; } = null!;
    public static Camera? Camera { get; private set; }

    public static ref readonly RenderStats Stats => ref _stats;

    private const int MaxBones = 64;
    private static int _boneCount;
    private static float _time;
    private static Shader? _compositeShader;
    private static bool _inUIPass;
    private static bool _inScenePass;
    private static RenderStats _stats;
    private static ushort[] _sortGroupStack = null!;
    private static State[] _stateStack = null!;
    private static Matrix3x2[] _bones = null!;
    private static BatchState[] _batchStates = null!;
    private static ushort _sortGroupStackDepth = 0;
    private static int _stateStackDepth = 0;
    private static bool _batchStateDirty = true;
    private static int _batchStateCount = 0;

    private static ref State CurrentState => ref _stateStack[_stateStackDepth];

    #region Batching
    private static nuint _vertexBuffer;
    private static nuint _indexBuffer;
    private static nuint _boneUbo;
    private const int BoneUboBindingPoint = 0;
    private static int _maxDrawCommands;
    private static int _maxBatches;
    private static MeshVertex[] _vertices = null!;
    private static ushort[] _indices = null!;
    private static ushort[] _sortedIndices = null!;
    private static int _vertexCount;
    private static int _indexCount;
    private static RenderCommand[] _commands = null!;
    private static int _commandCount;
    private static Batch[] _batches = null!;
    private static int _batchCount = 0;

    private static Dictionary<string, UniformEntry> _uniforms = null!;
    #endregion
    
    public static Color ClearColor { get; set; } = Color.Black;  
    
    public static void Init(RenderConfig config)
    {
        Config = config;

        Driver = config.Driver ?? throw new ArgumentNullException(nameof(config.Driver),
            "RenderBackend must be provided. Use OpenGLRender for desktop or WebGLRender for web.");
        
        _maxDrawCommands = Config.MaxDrawCommands;
        _maxBatches = Config.MaxBatches;
        _sortGroupStack = new ushort[MaxSortGroups];
        _stateStack = new State[MaxStateStack];
        _sortGroupStackDepth = 0;
        _stateStackDepth = 0;
        
        Driver.Init(new RenderDriverConfig
        {
            VSync = Config.Vsync,
        });

        Camera = new Camera();
        Driver = config.Driver;
        
        InitBatcher();
        InitState();
    }

    private static void InitState()
    {
        _bones = new Matrix3x2[MaxBones];
        ResetState();
    }

    private static void ResetState()
    {
        _stateStackDepth = 0;
        CurrentState.Transform = Matrix3x2.Identity;
        CurrentState.SortGroup = 0;
        CurrentState.SortLayer = 0;
        CurrentState.Color = Color.White;
        CurrentState.Shader = null;
        CurrentState.BlendMode = default;
        for (var i = 0; i < MaxTextures; i++)
            CurrentState.Textures[i] = 0;

        var size = Application.WindowSize;
        CurrentState.ViewportX = 0;
        CurrentState.ViewportY = 0;
        CurrentState.ViewportWidth = (int)size.X;
        CurrentState.ViewportHeight = (int)size.Y;

        CurrentState.ScissorEnabled = false;
        CurrentState.ScissorX = 0;
        CurrentState.ScissorY = 0;
        CurrentState.ScissorWidth = 0;
        CurrentState.ScissorHeight = 0;

        _batchStateCount = 0;
        _batchCount = 0;
        _uniforms.Clear();
        _boneCount = 1;
        _bones[0] = Matrix3x2.Identity;
    }
    
    private static void InitBatcher()
    {
        _vertices = new MeshVertex[MaxVertices];
        _indices = new ushort[MaxIndices];
        _sortedIndices = new ushort[MaxIndices];
        _commands = new RenderCommand[_maxDrawCommands];
        _batches = new Batch[_maxBatches];
        _batchStates = new BatchState[_maxBatches];
        _uniforms = new Dictionary<string, UniformEntry>();

        _vertexBuffer = Driver.CreateVertexBuffer(
            _vertices.Length * MeshVertex.SizeInBytes,
            BufferUsage.Dynamic,
            "Render.Vertices"
        );

        _indexBuffer = Driver.CreateIndexBuffer(
            _indices.Length * sizeof(ushort),
            BufferUsage.Dynamic,
            "Render.Indices"
        );

        // Create bone UBO: 64 bones * 2 vec4s per bone (std140 padded) * 16 bytes per vec4
        _boneUbo = Driver.CreateUniformBuffer(MaxBones * 2 * 16, BufferUsage.Dynamic, "Render.BoneUBO");
    }

    public static void Shutdown()
    {
        ShutdownBatcher();
        Driver.Shutdown();
    }

    private static void ShutdownBatcher()
    {
        Driver.DestroyBuffer(_vertexBuffer);
        Driver.DestroyBuffer(_indexBuffer);
        Driver.DestroyBuffer(_boneUbo);
    }

    public static void SetShader(Shader shader)
    {
        if (shader == CurrentState.Shader) return;
        CurrentState.Shader = shader;
        _batchStateDirty = true;
    }

    public static void SetTexture(nuint texture, int slot = 0)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        if (CurrentState.Textures[slot] == texture) return;
        CurrentState.Textures[slot] = texture;
        _batchStateDirty = true;
    }
    
    public static void SetTexture(Texture texture, int slot = 0)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        var handle = texture?.Handle ?? nuint.Zero;
        if (CurrentState.Textures[slot] == handle) return;
        CurrentState.Textures[slot] = handle;
        _batchStateDirty = true;
    }

    public static void SetUniformFloat(string name, float value)
    {
        _uniforms[name] = new UniformEntry { Type = UniformType.Float, Name = name, Value = new Vector4(value, 0, 0, 0) };
        _batchStateDirty = true;
    }

    public static void SetUniformVec2(string name, Vector2 value)
    {
        _uniforms[name] = new UniformEntry { Type = UniformType.Vec2, Name = name, Value = new Vector4(value.X, value.Y, 0, 0) };
        _batchStateDirty = true;
    }

    public static void SetUniformVec4(string name, Vector4 value)
    {
        _uniforms[name] = new UniformEntry { Type = UniformType.Vec4, Name = name, Value = value };
        _batchStateDirty = true;
    }

    public static void SetUniformMatrix4x4(string name, Matrix4x4 value)
    {
        _uniforms[name] = new UniformEntry { Type = UniformType.Matrix4x4, Name = name, MatrixValue = value };
        _batchStateDirty = true;
    }

    public static void SetColor(Color color)
    {
        CurrentState.Color = color;
    }

    /// <summary>
    /// Bind a camera for rendering. Pass null to use the default screen-space camera.
    /// Sets viewport and queues projection uniform for batch execution.
    /// </summary>
    public static void SetCamera(Camera? camera)
    {
        Camera = camera;
        if (camera == null) return;

        var viewport = camera.Viewport;
        if (viewport is { Width: > 0, Height: > 0 })
            SetViewport((int)viewport.X, (int)viewport.Y, (int)viewport.Width, (int)viewport.Height);

        var view = camera.ViewMatrix;
        var projection = new Matrix4x4(
            view.M11, view.M12, 0, view.M31,
            view.M21, view.M22, 0, view.M32,
            0, 0, 1, 0,
            0, 0, 0, 1
        );

        SetUniformMatrix4x4("u_projection", projection);
        SetUniformFloat("u_time", _time);
    }

    internal static void BeginFrame()
    {
        ResetState();

        Driver.BeginFrame();
        Driver.DisableScissor();

        _time += Time.DeltaTime;

        // Ensure offscreen target matches window size
        var size = Application.WindowSize;
        Driver.ResizeOffscreenTarget((int)size.X, (int)size.Y, Config.MsaaSamples);

        _inUIPass = false;
        _inScenePass = true;

        Driver.BeginScenePass(ClearColor);
    }

    public static void BeginUI()
    {
        if (_inUIPass) return;

        ExecuteCommands();

        if (_inScenePass)
        {
            Driver.EndScenePass();
            _inScenePass = false;

            if (_compositeShader != null)
                Driver.Composite(_compositeShader.Handle);
        }

        _inUIPass = true;
    }

    internal static void EndFrame()
    {
        ExecuteCommands();

        if (_inScenePass)
        {
            Driver.EndScenePass();
            _inScenePass = false;

            if (_compositeShader != null)
                Driver.Composite(_compositeShader.Handle);
        }

        Driver.EndFrame();
    }

    internal static void ResolveAssets()
    {
        if (!string.IsNullOrEmpty(Config.CompositeShader))
        {
            _compositeShader = Asset.Get<Shader>(AssetType.Shader, Config.CompositeShader);
            if (_compositeShader == null)
                throw new ArgumentNullException(nameof(Config.CompositeShader), "Composite shader not found");
        }
    }

    public static void Clear(Color color)
    {
        Driver.Clear(color);
    }

    public static void SetViewport(int x, int y, int width, int height)
    {
        if (CurrentState.ViewportX == x && CurrentState.ViewportY == y &&
            CurrentState.ViewportWidth == width && CurrentState.ViewportHeight == height)
            return;

        CurrentState.ViewportX = x;
        CurrentState.ViewportY = y;
        CurrentState.ViewportWidth = width;
        CurrentState.ViewportHeight = height;
        _batchStateDirty = true;
    }

    public static void SetScissor(int x, int y, int width, int height)
    {
        if (CurrentState.ScissorEnabled &&
            CurrentState.ScissorX == x && CurrentState.ScissorY == y &&
            CurrentState.ScissorWidth == width && CurrentState.ScissorHeight == height)
            return;

        CurrentState.ScissorEnabled = true;
        CurrentState.ScissorX = x;
        CurrentState.ScissorY = y;
        CurrentState.ScissorWidth = width;
        CurrentState.ScissorHeight = height;
        _batchStateDirty = true;
    }

    public static void DisableScissor()
    {
        if (!CurrentState.ScissorEnabled)
            return;

        CurrentState.ScissorEnabled = false;
        _batchStateDirty = true;
    }

    #region Draw

    public static void DrawQuad(float x, float y, float width, float height, ushort order = 0)
    {
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order);
    }

    public static void DrawQuad(float x, float y, float width, float height, in Matrix3x2 transform, ushort order = 0)
    {
        CurrentState.Transform = transform;
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order);
    }

    public static void DrawQuad(float x, float y, float width, float height, float u0, float v0, float u1, float v1, ushort order = 0)
    {
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), order);
    }

    public static void DrawQuad(float x, float y, float width, float height, float u0, float v0, float u1, float v1, in Matrix3x2 transform, ushort order = 0)
    {
        CurrentState.Transform = transform;
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), order);
    }

    public static void DrawQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, ushort order = 0)
    {
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order);
    }

    #endregion

    public static void SetLayer(ushort layer)
    {
        CurrentState.SortLayer = layer;
    }

    public static void PushState()
    {
        if (_stateStackDepth >= MaxStateStack - 1)
            return;

        ref var current = ref _stateStack[_stateStackDepth];
        ref var next = ref _stateStack[++_stateStackDepth];

        next = current;
    }

    public static void PopState()
    {
        if (_stateStackDepth == 0)
            return;

        ref var current = ref _stateStack[_stateStackDepth];
        ref var prev = ref _stateStack[--_stateStackDepth];

        var shaderChanged = current.Shader != prev.Shader;
        var blendChanged = current.BlendMode != prev.BlendMode;
        var texturesChanged = false;
        for (var i = 0; i < MaxTextures && !texturesChanged; i++)
            texturesChanged = current.Textures[i] != prev.Textures[i];
        var viewportChanged = current.ViewportX != prev.ViewportX ||
                              current.ViewportY != prev.ViewportY ||
                              current.ViewportWidth != prev.ViewportWidth ||
                              current.ViewportHeight != prev.ViewportHeight;
        var scissorChanged = current.ScissorEnabled != prev.ScissorEnabled ||
                             current.ScissorX != prev.ScissorX ||
                             current.ScissorY != prev.ScissorY ||
                             current.ScissorWidth != prev.ScissorWidth ||
                             current.ScissorHeight != prev.ScissorHeight;

        if (shaderChanged || blendChanged || texturesChanged || viewportChanged || scissorChanged)
            _batchStateDirty = true;
    }

    public static void SetBlendMode(BlendMode blendMode)
    {
        CurrentState.BlendMode = blendMode;
        _batchStateDirty = true;
    }
    
    public const int MaxBoneTransforms = MaxBones;

    public static void SetBones(ReadOnlySpan<Matrix3x2> transforms)
    {
        Debug.Assert(_boneCount + transforms.Length <= MaxBones);
        CurrentState.BoneIndex = (ushort)_boneCount;
        fixed (Matrix3x2* dst = &_bones[_boneCount])
        fixed (Matrix3x2* src = transforms)
        {
            Unsafe.CopyBlock(dst, src, (uint)(transforms.Length * sizeof(Matrix3x2)));
        }
        _boneCount += transforms.Length;
    }

    private static void UploadBones()
    {
        // std140 layout requires vec4 alignment, so we pad each Matrix3x2 row to vec4
        // Matrix3x2: M11,M12,M21,M22,M31,M32 -> two vec4s: [M11,M12,M31,0], [M21,M22,M32,0]
        var size = MaxBones * 2 * 16;
        var data = stackalloc float[MaxBones * 8];
        fixed (Matrix3x2* src = _bones)
        {
            var srcPtr = (float*)src;
            var dstPtr = data;
            for (var i = 0; i < MaxBones; i++)
            {
                // Row 0: M11, M12, M31, pad
                dstPtr[0] = srcPtr[0]; // M11
                dstPtr[1] = srcPtr[1]; // M12
                dstPtr[2] = srcPtr[4]; // M31
                dstPtr[3] = 0;
                // Row 1: M21, M22, M32, pad
                dstPtr[4] = srcPtr[2]; // M21
                dstPtr[5] = srcPtr[3]; // M22
                dstPtr[6] = srcPtr[5]; // M32
                dstPtr[7] = 0;
                srcPtr += 6;
                dstPtr += 8;
            }
        }
        Driver.UpdateUniformBuffer(_boneUbo, 0, new ReadOnlySpan<byte>(data, size));
    }

    public static Matrix3x2 Transform => CurrentState.Transform;

    public static void SetTransform(in Matrix3x2 transform)
    {
        CurrentState.Transform = transform;
    }

    public static void PushSortGroup(ushort group)
    {
        _sortGroupStack[_sortGroupStackDepth++] = CurrentState.SortGroup;
        CurrentState.SortGroup = group;
    }

    public static void PopSortGroup()
    {
        if (_sortGroupStackDepth == 0)
            return;

        _sortGroupStackDepth--;
        CurrentState.SortGroup = _sortGroupStack[_sortGroupStackDepth];
    }

    private static long MakeSortKey(ushort order) =>
        (((long)CurrentState.SortLayer) << LayerShift) |
        (((long)CurrentState.SortGroup) << GroupShift) |
        (((long)order) << OrderShift) |
        (((long)_commandCount) << IndexShift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBatchState()
    {
        _batchStateDirty = false;
        ref var batchState = ref _batchStates[_batchStateCount++];
        batchState.Shader = CurrentState.Shader?.Handle ?? nuint.Zero;
        batchState.BlendMode = CurrentState.BlendMode;
        for (var i = 0; i < MaxTextures; i++)
            batchState.Textures[i] = CurrentState.Textures[i];
        batchState.ViewportX = CurrentState.ViewportX;
        batchState.ViewportY = CurrentState.ViewportY;
        batchState.ViewportWidth = CurrentState.ViewportWidth;
        batchState.ViewportHeight = CurrentState.ViewportHeight;
        batchState.ScissorEnabled = CurrentState.ScissorEnabled;
        batchState.ScissorX = CurrentState.ScissorX;
        batchState.ScissorY = CurrentState.ScissorY;
        batchState.ScissorWidth = CurrentState.ScissorWidth;
        batchState.ScissorHeight = CurrentState.ScissorHeight;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddQuad(
        in Vector2 p0,
        in Vector2 p1,
        in Vector2 p2,
        in Vector2 p3,
        in Vector2 uv0,
        in Vector2 uv1,
        in Vector2 uv2,
        in Vector2 uv3,
        ushort order)
    {
        if (CurrentState.Shader == null)
            return;

        if (_batchStateDirty)
            AddBatchState();

        if (_commandCount >= _maxDrawCommands)
            return;

        if (_vertexCount + 4 > MaxVertices ||
            _indexCount + 6 > MaxIndices)
            return;

        ref var cmd = ref _commands[_commandCount++];
        cmd.SortKey = MakeSortKey(order);
        cmd.VertexOffset = _vertexCount;
        cmd.VertexCount = 4;
        cmd.IndexOffset = _indexCount;
        cmd.IndexCount = 6;
        cmd.BatchState = (ushort)(_batchStateCount - 1);

        var t0 = Vector2.Transform(p0, CurrentState.Transform);
        var t1 = Vector2.Transform(p1, CurrentState.Transform);
        var t2 = Vector2.Transform(p2, CurrentState.Transform);
        var t3 = Vector2.Transform(p3, CurrentState.Transform);

        _vertices[_vertexCount + 0] = new MeshVertex { Position = t0, UV = uv0, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };
        _vertices[_vertexCount + 1] = new MeshVertex { Position = t1, UV = uv1, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };
        _vertices[_vertexCount + 2] = new MeshVertex { Position = t2, UV = uv2, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };
        _vertices[_vertexCount + 3] = new MeshVertex { Position = t3, UV = uv3, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };

        _indices[_indexCount + 0] = (ushort)_vertexCount;
        _indices[_indexCount + 1] = (ushort)(_vertexCount + 1);
        _indices[_indexCount + 2] = (ushort)(_vertexCount + 2);
        _indices[_indexCount + 3] = (ushort)(_vertexCount + 2);
        _indices[_indexCount + 4] = (ushort)(_vertexCount + 3);
        _indices[_indexCount + 5] = (ushort)_vertexCount;

        _vertexCount += 4;
        _indexCount += 6;
    }

    private static void AddBatch(ushort batchState, int indexOffset, int indexCount)
    {
        if (indexCount == 0) return;
        
        ref var batch = ref _batches[_batchCount++];
        batch.IndexOffset = indexOffset;
        batch.IndexCount = indexCount;
        batch.State = batchState;
    }

    private static void ApplyUniforms()
    {
        foreach (var kvp in _uniforms)
        {
            var u = kvp.Value;
            switch (u.Type)
            {
                case UniformType.Float:
                    Driver.SetUniformFloat(u.Name, u.Value.X);
                    break;
                case UniformType.Vec2:
                    Driver.SetUniformVec2(u.Name, new Vector2(u.Value.X, u.Value.Y));
                    break;
                case UniformType.Vec4:
                    Driver.SetUniformVec4(u.Name, u.Value);
                    break;
                case UniformType.Matrix4x4:
                    Driver.SetUniformMatrix4x4(u.Name, u.MatrixValue);
                    break;
            }
        }
    }

    public static void Flush()
    {
        ExecuteCommands();
    }

    public static void SetVertexBuffer<T>(nuint buffer) where T : unmanaged, IVertex
    {
        ExecuteCommands();
        Driver.BindVertexFormat(VertexFormat<T>.Handle);
        Driver.BindVertexBuffer(buffer);
    }

    public static void SetIndexBuffer(nuint buffer)
    {
        Driver.BindIndexBuffer(buffer);
    }

    public static void DrawElements(int indexCount, int indexOffset = 0)
    {
        ApplyUniforms();
        Driver.DrawElements(indexOffset, indexCount, 0);
        _stats.DrawCount++;
    }

    private static void ExecuteCommands()
    {
        if (_commandCount == 0)
            return;

        var vertexSpan = MemoryMarshal.AsBytes(_vertices.AsSpan(0, _vertexCount));
        Driver.UpdateVertexBuffer(_vertexBuffer, 0, vertexSpan);

        SortCommands();

        var batchStateIndex = _commands[0].BatchState;
        var sortedIndexCount = 0;
        var sortedIndexOffset = 0;
        for (var commandIndex = 0; commandIndex < _commandCount; commandIndex++)
        {
            ref var cmd = ref _commands[commandIndex];
            if (batchStateIndex != cmd.BatchState)
            {
                AddBatch(batchStateIndex, sortedIndexOffset, sortedIndexCount - sortedIndexOffset);
                sortedIndexOffset = sortedIndexCount;
                batchStateIndex = cmd.BatchState;
            }

            fixed (ushort* src = &_indices[cmd.IndexOffset])
            fixed (ushort* dst = &_sortedIndices[sortedIndexCount])
            {
                Unsafe.CopyBlock(dst, src, (uint)(cmd.IndexCount * sizeof(ushort)));
            }

            sortedIndexCount += cmd.IndexCount;
        }

        if (sortedIndexOffset != sortedIndexCount)
            AddBatch(batchStateIndex, sortedIndexOffset, sortedIndexCount - sortedIndexOffset);

        Driver.UpdateIndexBuffer(_indexBuffer, 0, _sortedIndices.AsSpan(0, sortedIndexCount));
        Driver.BindVertexFormat(VertexFormat<MeshVertex>.Handle);
        Driver.BindVertexBuffer(_vertexBuffer);
        Driver.BindIndexBuffer(_indexBuffer);

        UploadBones();
        Driver.BindUniformBuffer(_boneUbo, BoneUboBindingPoint);

        for (var batchIndex=0; batchIndex < _batchCount; batchIndex++)
        {
            ref var batch = ref _batches[batchIndex];
            ref var batchState = ref _batchStates[batch.State];
            Driver.SetViewport(batchState.ViewportX, batchState.ViewportY, batchState.ViewportWidth, batchState.ViewportHeight);
            if (batchState.ScissorEnabled)
                Driver.SetScissor(batchState.ScissorX, batchState.ScissorY, batchState.ScissorWidth, batchState.ScissorHeight);
            else
                Driver.DisableScissor();
            Driver.BindShader(batchState.Shader);
            ApplyUniforms();
            Driver.BindTexture((nuint)batchState.Textures[0], 0);
            Driver.BindTexture((nuint)batchState.Textures[1], 1);
            Driver.SetBlendMode(batchState.BlendMode);

            Driver.DrawElements(batch.IndexOffset, batch.IndexCount, 0);
        }

        _stats.DrawCount += _batchCount;
        _stats.VertexCount = _vertexCount;
        _stats.CommandCount = _commandCount;

        _vertexCount = 0;
        _indexCount = 0;
        _commandCount = 0;
        _batchCount = 0;
        _batchStateCount = 0;
        _batchStateDirty = true;
    }
    
    private static void SortCommands()
    {
        new Span<RenderCommand>(_commands, 0, _commandCount).Sort();
    }
}