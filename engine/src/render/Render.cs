//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using NoZ.Platform;

namespace NoZ;

public static unsafe class Render
{
    private const int MaxSortGroups = 16;
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
        public ushort UniformIndex;
        public ushort UniformCount;
        public fixed ulong Textures[MaxTextures];
        public BlendMode BlendMode;
    }

    private struct Batch
    {
        public int IndexOffset;
        public int IndexCount;
        public ushort State;
    }
    
    private struct State()
    {
        public Color Color = default;
        public Shader? Shader = null!;
        public Matrix3x2[]? Bones = null!;
        public Matrix3x2 Transform;
        public int BoneCount = 0;
        public nuint[] Textures = null!;
        public ushort Layer = 0;
        public ushort Group = 0;
        public ushort Index = 0;
        public BlendMode BlendMode;
    }
    
    public static RenderConfig Config { get; private set; } = null!;
    public static IRenderDriver Driver { get; private set; } = null!;
    public static Camera? Camera { get; private set; }

    public static ref readonly RenderStats Stats => ref _stats;

    private const int MaxBones = 64;
    private static float _time;
    private static Shader? _compositeShader;
    private static bool _inUIPass;
    private static bool _inScenePass;
    private static RenderStats _stats;
    private static ushort[] _sortGroupStack = null!;
    private static ushort[] _layerStack = null!;
    private static BatchState[] _batchStates = null!;
    private static ushort _sortGroupStackDepth = 0;
    private static ushort _layerStackDepth = 0;
    private static bool _batchStateDirty = true;
    private static int _batchStateCount = 0;

    private static State _state;

    #region Batching
    private static nuint _vertexBuffer;
    private static nuint _indexBuffer;
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

    private const int MaxUniforms = 1024;
    private static UniformEntry[] _uniforms = null!;
    private static int _uniformCount = 0;
    private static int _uniformBatchStart = 0;
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
        _layerStack = new ushort[MaxSortGroups];
        _sortGroupStackDepth = 0;
        _layerStackDepth = 0;
        
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
        _state.Bones = new Matrix3x2[MaxBones];
        _state.Textures = new nuint[MaxTextures];
        ResetState();
    }

    private static void ResetState()
    {
        Debug.Assert(_state.Bones != null);

        _state.Transform = Matrix3x2.Identity;
        _state.BoneCount = 0;
        _state.Group = 0;
        _state.Layer = 0;
        _state.Index = 0;
        _state.Color = Color.White;
        _batchStateCount = 0;
        _batchCount = 0;
        _uniformCount = 0;
        _uniformBatchStart = 0;
        for (var boneIndex = 0; boneIndex < MaxBones; boneIndex++)
            _state.Bones[boneIndex] = Matrix3x2.Identity;
    }
    
    private static void InitBatcher()
    {
        _vertices = new MeshVertex[MaxVertices];
        _indices = new ushort[MaxIndices];
        _sortedIndices = new ushort[MaxIndices];
        _commands = new RenderCommand[_maxDrawCommands];
        _batches = new Batch[_maxBatches];
        _batchStates = new BatchState[_maxBatches];
        _uniforms = new UniformEntry[MaxUniforms];

        _vertexBuffer = Driver.CreateVertexBuffer(
            _vertices.Length * MeshVertex.SizeInBytes,
            BufferUsage.Dynamic
        );

        _indexBuffer = Driver.CreateIndexBuffer(
            _indices.Length * sizeof(ushort),
            BufferUsage.Dynamic
        );
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
    }

    public static void SetShader(Shader shader)
    {
        if (shader == _state.Shader) return;
        _state.Shader = shader;
        _batchStateDirty = true;
    }

    public static void SetTexture(nuint texture, int slot = 0)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        if (_state.Textures[slot] == texture) return;
        _state.Textures[slot] = texture;
        _batchStateDirty = true;
    }
    
    public static void SetTexture(Texture texture, int slot = 0)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        nuint handle = texture?.Handle ?? nuint.Zero;
        if (_state.Textures[slot] == handle) return;
        _state.Textures[slot] = handle;
        _batchStateDirty = true;
    }

    public static void SetUniformFloat(string name, float value)
    {
        ref var u = ref _uniforms[_uniformCount++];
        u.Type = UniformType.Float;
        u.Name = name;
        u.Value = new Vector4(value, 0, 0, 0);
        _batchStateDirty = true;
    }

    public static void SetUniformVec2(string name, Vector2 value)
    {
        ref var u = ref _uniforms[_uniformCount++];
        u.Type = UniformType.Vec2;
        u.Name = name;
        u.Value = new Vector4(value.X, value.Y, 0, 0);
        _batchStateDirty = true;
    }

    public static void SetUniformVec4(string name, Vector4 value)
    {
        ref var u = ref _uniforms[_uniformCount++];
        u.Type = UniformType.Vec4;
        u.Name = name;
        u.Value = value;
        _batchStateDirty = true;
    }

    public static void SetUniformMatrix4x4(string name, Matrix4x4 value)
    {
        ref var u = ref _uniforms[_uniformCount++];
        u.Type = UniformType.Matrix4x4;
        u.Name = name;
        u.MatrixValue = value;
        _batchStateDirty = true;
    }

    public static void SetColor(Color color)
    {
        _state.Color = color;
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
            Driver.SetViewport((int)viewport.X, (int)viewport.Y, (int)viewport.Width, (int)viewport.Height);

        // Convert camera's 3x2 view matrix to 4x4 for the shader
        // Translation goes in column 4 (M14, M24) so after transpose it's in the right place
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
        Driver.SetViewport(x, y, width, height);
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
        _state.Transform = transform;
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
        _state.Transform = transform;
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

    public static void BindLayer(ushort layer)
    {
        _state.Layer = layer;
    }

    public static void PushLayer(ushort layer)
    {
        _layerStack[_layerStackDepth++] = _state.Layer;
        _state.Layer = layer;
    }

    public static void PopLayer()
    {
        if (_layerStackDepth == 0)
            return;

        _layerStackDepth--;
        _state.Layer = _layerStack[_layerStackDepth];
    }

    public static void SetBlendMode(BlendMode blendMode)
    {
        _state.BlendMode = blendMode;
        _batchStateDirty = true;
    }
    
    public static void SetBones(ReadOnlySpan<Matrix3x2> transforms)
    {
        Debug.Assert(_state.Bones != null);
        Debug.Assert(transforms.Length < MaxBones);
        for (int i = 0, c = transforms.Length; i < c; i++)
            _state.Bones[i + 1] = transforms[i];

        _state.BoneCount = 0;
    }

    public static Matrix3x2 Transform => _state.Transform;

    public static void SetTransform(in Matrix3x2 transform)
    {
        _state.Transform = transform;
    }

    public static void PushSortGroup(ushort group)
    {
        _sortGroupStack[_sortGroupStackDepth++] = _state.Group;
        _state.Group = group;
    }

    public static void PopSortGroup()
    {
        if (_sortGroupStackDepth == 0)
            return;

        _sortGroupStackDepth--;
        _state.Group = _sortGroupStack[_sortGroupStackDepth];
    }

    private static long MakeSortKey(ushort order) =>
        (((long)_state.Layer) << LayerShift) |
        (((long)_state.Group) << GroupShift) |
        (((long)order) << OrderShift) |
        (((long)_state.Index) << IndexShift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBatchState()
    {
        _batchStateDirty = false;
        ref var batchState = ref _batchStates[_batchStateCount++];
        batchState.Shader = _state.Shader?.Handle ?? nuint.Zero;
        batchState.BlendMode = _state.BlendMode;
        for (var i = 0; i < MaxTextures; i++)
            batchState.Textures[i] = _state.Textures[i];

        batchState.UniformIndex = (ushort)_uniformBatchStart;
        batchState.UniformCount = (ushort)(_uniformCount - _uniformBatchStart);
        _uniformBatchStart = _uniformCount;
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
        if (_state.Shader == null)
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

        var t0 = Vector2.Transform(p0, _state.Transform);
        var t1 = Vector2.Transform(p1, _state.Transform);
        var t2 = Vector2.Transform(p2, _state.Transform);
        var t3 = Vector2.Transform(p3, _state.Transform);

        _vertices[_vertexCount + 0] = new MeshVertex { Position = t0, UV = uv0, Normal = Vector2.Zero, Color = _state.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };
        _vertices[_vertexCount + 1] = new MeshVertex { Position = t1, UV = uv1, Normal = Vector2.Zero, Color = _state.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };
        _vertices[_vertexCount + 2] = new MeshVertex { Position = t2, UV = uv2, Normal = Vector2.Zero, Color = _state.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };
        _vertices[_vertexCount + 3] = new MeshVertex { Position = t3, UV = uv3, Normal = Vector2.Zero, Color = _state.Color, Bone = 0, Atlas = 0, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 };

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

    private static void ApplyBatchUniforms(ref BatchState state)
    {
        for (var i = state.UniformIndex; i < state.UniformIndex + state.UniformCount; i++)
        {
            ref var u = ref _uniforms[i];
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

    private static void ExecuteCommands()
    {
        if (_commandCount == 0)
            return;

        Driver.UpdateVertexBuffer(
            _vertexBuffer,
            0,
            _vertices.AsSpan(0, _vertexCount)
        );
        
        SortCommands();
        
        ushort batchStateIndex = 0xFFFF;
        var sortedIndexCount = 0;   
        var sortedIndexOffset = 0;
        for (var commandIndex = 0; commandIndex < _commandCount; commandIndex++)
        {
            ref var cmd = ref _commands[commandIndex];
            if (batchStateIndex != cmd.BatchState)
            {
                AddBatch(cmd.BatchState, sortedIndexOffset, sortedIndexCount - sortedIndexOffset);
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
        Driver.BindVertexBuffer(_vertexBuffer);
        Driver.BindIndexBuffer(_indexBuffer);
        
        for (var batchIndex=0; batchIndex < _batchCount; batchIndex++)
        {
            ref var batch = ref _batches[batchIndex];
            ref var batchState = ref _batchStates[batch.State];
            Driver.BindShader(batchState.Shader);
            Driver.SetBoneTransforms(_state.Bones);
            ApplyBatchUniforms(ref batchState);
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