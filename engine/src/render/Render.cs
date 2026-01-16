//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_RENDER_DEBUG
//#define NOZ_RENDER_DEBUG_VERBOSE

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
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
        public nuint Texture0;
        public nuint Texture1;
        public BlendMode BlendMode;
        public int ViewportX;
        public int ViewportY;
        public int ViewportW;
        public int ViewportH;
        public int ScissorX;
        public int ScissorY;
        public int ScissorWidth;
        public int ScissorHeight;
        public nuint VertexFormat;
        public nuint VertexBuffer;
        public nuint IndexBuffer;
        public bool ScissorEnabled;
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
        public nuint IndexBuffer;
        public nuint VertexBuffer;
        public nuint VertexFormat;
    }

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
    private static ushort _sortGroupStackDepth = 0;
    private static int _stateStackDepth = 0;
    private static bool _batchStateDirty = true;
    
    public static RenderConfig Config { get; private set; } = null!;
    public static IRenderDriver Driver { get; private set; } = null!;
    public static Camera? Camera { get; private set; }
    public static ref readonly Matrix3x2 Transform => ref CurrentState.Transform;
    public static Color Color => CurrentState.Color;
    public static ref readonly RenderStats Stats => ref _stats;

    private static ref State CurrentState => ref _stateStack[_stateStackDepth];
    
    #region Batching
    private static nuint _vertexBuffer;
    private static nuint _indexBuffer;
    private static nuint _boneUbo;
    private const int BoneUboBindingPoint = 0;
    private static int _maxDrawCommands;
    private static int _maxBatches;
    private static NativeArray<MeshVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static NativeArray<ushort> _sortedIndices;
    private static NativeArray<RenderCommand> _commands;
    private static NativeArray<Batch> _batches;
    private static NativeArray<BatchState> _batchStates;

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

        CurrentState.VertexFormat = VertexFormat<MeshVertex>.Handle;
        CurrentState.VertexBuffer = _vertexBuffer;
        CurrentState.IndexBuffer = _indexBuffer;

        _uniforms.Clear();
        _boneCount = 1;
        _bones[0] = Matrix3x2.Identity;
    }
    
    private static void InitBatcher()
    {
        _vertices = new NativeArray<MeshVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _sortedIndices = new NativeArray<ushort>(MaxIndices);
        _commands = new NativeArray<RenderCommand>(_maxDrawCommands);
        _batches = new NativeArray<Batch>(_maxBatches);
        _batchStates = new NativeArray<BatchState>(_maxBatches);
        _uniforms = new Dictionary<string, UniformEntry>();

        _vertexBuffer = Driver.CreateVertexBuffer(
            MaxVertices * MeshVertex.SizeInBytes,
            BufferUsage.Dynamic,
            "Render.Vertices"
        );

        _indexBuffer = Driver.CreateIndexBuffer(
            MaxIndices * sizeof(ushort),
            BufferUsage.Dynamic,
            "Render.Indices"
        );

        // Create bone UBO: 64 bones * 2 vec4s per bone (std140 padded) * 16 bytes per vec4
        _boneUbo = Driver.CreateUniformBuffer(MaxBones * 2 * 16, BufferUsage.Dynamic, "Render.BoneUBO");
    }

    public static void Shutdown()
    {
        _batches.Dispose();
        _vertices.Dispose();
        _commands.Dispose();
        _indices.Dispose();

        Driver.DestroyBuffer(_vertexBuffer);
        Driver.DestroyBuffer(_indexBuffer);
        Driver.DestroyBuffer(_boneUbo);
        
        Driver.Shutdown();

        _vertexBuffer = 0;
        _indexBuffer = 0;
        _boneUbo = 0;
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

    public static void Draw(in Rect rect, ushort order = 0) =>
        Draw(rect.X, rect.Y, rect.Width, rect.Height);
        
    public static void Draw(float x, float y, float width, float height, ushort order = 0)
    {
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order);
    }

    public static void Draw(float x, float y, float width, float height, in Matrix3x2 transform, ushort order = 0)
    {
        CurrentState.Transform = transform;
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order);
    }

    public static void Draw(float x, float y, float width, float height, float u0, float v0, float u1, float v1, ushort order = 0)
    {
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), order);
    }

    public static void Draw(float x, float y, float width, float height, float u0, float v0, float u1, float v1, in Matrix3x2 transform, ushort order = 0)
    {
        CurrentState.Transform = transform;
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), order);
    }

    public static void Draw(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, ushort order = 0)
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
        (((long)_commands.Length) << IndexShift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBatchState()
    {
        _batchStateDirty = false;
        ref var batchState = ref _batchStates.Add();
        batchState.Shader = CurrentState.Shader?.Handle ?? nuint.Zero;
        batchState.BlendMode = CurrentState.BlendMode;
        batchState.Texture0 = (nuint)CurrentState.Textures[0];
        batchState.Texture1 = (nuint)CurrentState.Textures[1];
        batchState.ViewportX = CurrentState.ViewportX;
        batchState.ViewportY = CurrentState.ViewportY;
        batchState.ViewportW = CurrentState.ViewportWidth;
        batchState.ViewportH = CurrentState.ViewportHeight;
        batchState.ScissorEnabled = CurrentState.ScissorEnabled;
        batchState.ScissorX = CurrentState.ScissorX;
        batchState.ScissorY = CurrentState.ScissorY;
        batchState.ScissorWidth = CurrentState.ScissorWidth;
        batchState.ScissorHeight = CurrentState.ScissorHeight;
        batchState.VertexFormat = CurrentState.VertexFormat;
        batchState.VertexBuffer = CurrentState.VertexBuffer;
        batchState.IndexBuffer = CurrentState.IndexBuffer;
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

        if (_commands.Length >= _maxDrawCommands)
            return;

        if (_vertices.Length + 4 > MaxVertices ||
            _indices.Length + 6 > MaxIndices)
            return;

        ref var cmd = ref _commands.Add();
        cmd.SortKey = MakeSortKey(order);
        cmd.IndexOffset = _indices.Length;
        cmd.IndexCount = 6;
        cmd.BatchState = (ushort)(_batchStates.Length - 1);

        var t0 = Vector2.Transform(p0, CurrentState.Transform);
        var t1 = Vector2.Transform(p1, CurrentState.Transform);
        var t2 = Vector2.Transform(p2, CurrentState.Transform);
        var t3 = Vector2.Transform(p3, CurrentState.Transform);

        var baseVertex = _vertices.Length;
        _vertices.Add(new MeshVertex { Position = t0, UV = uv0, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });
        _vertices.Add(new MeshVertex { Position = t1, UV = uv1, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });
        _vertices.Add(new MeshVertex { Position = t2, UV = uv2, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });
        _vertices.Add(new MeshVertex { Position = t3, UV = uv3, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });

        _indices.Add((ushort)(baseVertex + 0));
        _indices.Add((ushort)(baseVertex + 1));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 3));
        _indices.Add((ushort)(baseVertex + 0));
    }

    private static void AddBatch(ushort batchState, int indexOffset, int indexCount)
    {
        if (indexCount == 0) return;
        
        ref var batch = ref _batches.Add();
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

    public static void SetVertexBuffer<T>(nuint buffer) where T : unmanaged, IVertex
    {
        if (CurrentState.VertexBuffer == buffer) return;

        CurrentState.VertexBuffer = buffer;
        CurrentState.VertexFormat = VertexFormat<T>.Handle;
        _batchStateDirty = true;
    }

    public static void SetIndexBuffer(nuint buffer)
    {
        if (CurrentState.IndexBuffer == buffer) return;
        CurrentState.IndexBuffer = buffer;
        _batchStateDirty = true;
    }

    public static void DrawElements(int indexCount, int indexOffset = 0, ushort order=0)
    {
        if (_batchStateDirty)
            AddBatchState();

        if (_commands.Length > 0)
        {
            ref var lastCommand = ref _commands[^1];
            if (lastCommand.BatchState == _batchStates.Length - 1 &&
                lastCommand.IndexOffset + lastCommand.IndexCount == indexOffset)
            {
                // Merge with last command
                lastCommand.IndexCount += (ushort)indexCount;
                LogRenderVerbose($"DrawElements (merged): Count={indexCount} Offset={indexOffset} Order={order}");
                return;
            }
        }

        ref var cmd = ref _commands.Add();
        cmd.SortKey = MakeSortKey(order);
        cmd.IndexOffset = indexOffset;
        cmd.IndexCount = indexCount;
        cmd.BatchState = (ushort)(_batchStates.Length - 1);

        LogRenderVerbose($"DrawElements: Count={indexCount} Offset={indexOffset} Order={order}");
    }

    private static void ExecuteCommands()
    {
        if (_commands.Length == 0)
            return;
        
        TextRender.Flush();
        UIRender.Flush();
        
        _commands.AsSpan().Sort();
        _sortedIndices.Clear();

        var batchStateIndex = _commands[0].BatchState;
        var sortedIndexOffset = 0;
        for (int commandIndex = 0, commandCount = _commands.Length; commandIndex < commandCount; commandIndex++)
        {
            ref var cmd = ref _commands[commandIndex];
            if (batchStateIndex != cmd.BatchState)
            {
                AddBatch(batchStateIndex, sortedIndexOffset, _sortedIndices.Length - sortedIndexOffset);
                sortedIndexOffset = _sortedIndices.Length;
                batchStateIndex = cmd.BatchState;
            }

            ref var batchState = ref _batchStates[cmd.BatchState];
            if (batchState.IndexBuffer == _indexBuffer)
            {
                _sortedIndices.AddRange(
                    _indices.AsReadonlySpan(cmd.IndexOffset, cmd.IndexCount)
                );
            }
        }

        if (sortedIndexOffset != _sortedIndices.Length)
            AddBatch(batchStateIndex, sortedIndexOffset, _sortedIndices.Length - sortedIndexOffset);

        Driver.BindVertexFormat(VertexFormat<MeshVertex>.Handle);
        Driver.BindVertexBuffer(_vertexBuffer);
        Driver.UpdateVertexBuffer(_vertexBuffer, 0, _vertices.AsByteSpan());
        Driver.BindIndexBuffer(_indexBuffer);
        Driver.UpdateIndexBuffer(_indexBuffer, 0, _sortedIndices.AsSpan());
        Driver.SetBlendMode(BlendMode.None);

        UploadBones();
        Driver.BindUniformBuffer(_boneUbo, BoneUboBindingPoint);

        var lastViewportX = ushort.MaxValue;
        var lastViewportY = ushort.MaxValue;
        var lastViewportW = ushort.MaxValue;
        var lastViewportH = ushort.MaxValue;
        var lastScissorEnabled = false;
        var lastShader = nuint.Zero;
        var lastTexture0 = nuint.Zero;
        var lastTexture1 = nuint.Zero;
        var lastVertexBuffer = _vertexBuffer;
        var lastIndexBuffer = _indexBuffer;
        var lastVertexFormat = VertexFormat<MeshVertex>.Handle;
        var lastBlendMode = BlendMode.None;

        LogRender($"ExecuteCommands: Batches={_batches.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");
        
        for (int batchIndex=0, batchCount=_batches.Length; batchIndex < batchCount; batchIndex++)
        {
            ref var batch = ref _batches[batchIndex];
            ref var batchState = ref _batchStates[batch.State];

            LogRender($"  Batch: Index={batchIndex} IndexOffset={batch.IndexOffset} IndexCount={batch.IndexCount} State={batch.State}");
            
            if (lastViewportX != batchState.ViewportX ||  
                lastViewportY != batchState.ViewportY ||
                lastViewportW != batchState.ViewportW ||
                lastViewportH != batchState.ViewportH)
            {
                lastViewportX = (ushort)batchState.ViewportX;
                lastViewportY = (ushort)batchState.ViewportY;
                lastViewportW = (ushort)batchState.ViewportW;
                lastViewportH = (ushort)batchState.ViewportH;
                Driver.SetViewport(
                    batchState.ViewportX,
                    batchState.ViewportY,
                    batchState.ViewportW,
                    batchState.ViewportH);

                LogRender($"    SetViewport: X={batchState.ViewportX} Y={batchState.ViewportY} W={batchState.ViewportW} H={batchState.ViewportH}");
            }

            if (lastScissorEnabled != batchState.ScissorEnabled)
            {
                lastScissorEnabled = batchState.ScissorEnabled;
                if (batchState.ScissorEnabled)
                {
                    Driver.SetScissor(
                        batchState.ScissorX,
                        batchState.ScissorY,
                        batchState.ScissorWidth,
                        batchState.ScissorHeight);
                    
                    LogRender($"    SetScissor: X={batchState.ScissorX} Y={batchState.ScissorY} W={batchState.ScissorWidth} H={batchState.ScissorHeight}");
                }
                else
                {
                    Driver.DisableScissor();
                    LogRender($"    DisableScissor:");
                }
            }

            if (lastShader != batchState.Shader)
            {
                lastShader = batchState.Shader;
                Driver.BindShader(batchState.Shader);
                LogRender($"    BindShader: Handle=0x{batchState.Shader:X}");
            }
            
            if (lastTexture0 != batchState.Texture0)
            {
                lastTexture0 = batchState.Texture0;
                Driver.BindTexture(batchState.Texture0, 0);
                LogRender($"    BindTexture: Slot=0 Handle=0x{batchState.Texture0:X}");
            }
            
            if (lastTexture1 != batchState.Texture1)
            {
                lastTexture1 = batchState.Texture1;
                Driver.BindTexture(batchState.Texture1, 1);
                LogRender($"    BindTexture: Slot=1 Handle=0x{batchState.Texture1:X}");
            }
            
            if (lastBlendMode != batchState.BlendMode)
            {
                lastBlendMode = batchState.BlendMode;
                Driver.SetBlendMode(batchState.BlendMode);
                LogRender($"    SetBlendMode: {batchState.BlendMode}");
            }

            if (lastVertexFormat != batchState.VertexFormat)
            {
                lastVertexFormat = batchState.VertexFormat;
                Driver.BindVertexFormat(batchState.VertexFormat);
                LogRender($"    BindVertexFormat: Handle=0x{batchState.VertexFormat:X}");
            }
            
            if (lastVertexBuffer != batchState.VertexBuffer)
            {
                lastVertexBuffer = batchState.VertexBuffer;
                Driver.BindVertexBuffer(batchState.VertexBuffer);
                LogRender($"    BindVertexBuffer: Handle=0x{batchState.VertexBuffer:X}");
            }
            
            if (lastIndexBuffer != batchState.IndexBuffer)
            {
                lastIndexBuffer = batchState.IndexBuffer;
                Driver.BindIndexBuffer(batchState.IndexBuffer);
                LogRender($"    BindIndexBuffer: Handle=0x{batchState.IndexBuffer:X}");
            }
            
            ApplyUniforms();

            LogRender($"    DrawElements: IndexCount={batch.IndexCount} IndexOffset={batch.IndexOffset}");
            Driver.DrawElements(batch.IndexOffset, batch.IndexCount, 0);
        }

        _stats.DrawCount += _batches.Length;
        _stats.VertexCount = _vertices.Length;
        _stats.CommandCount = _commands.Length;

        _commands.Clear();
        _vertices.Clear();
        _indices.Clear();
        _batches.Clear();
        _batchStates.Clear();
        
        _batchStateDirty = true;
    }

    [Conditional("NOZ_RENDER_DEBUG")]
    private static void LogRender(string msg)
    {
        Log.Debug($"[RENDER] {msg}");
    }

    [Conditional("NOZ_RENDER_DEBUG_VERBOSE")]
    private static void LogRenderVerbose(string msg)
    {
        Log.Debug($"[RENDER] {msg}");
    }
}