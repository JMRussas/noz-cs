//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_GRAPHICS_DEBUG
// #define NOZ_GRAPHICS_DEBUG_VERBOSE

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public static unsafe class Graphics
{
    private const int MaxSortGroups = 1526;
    private const int MaxStateStack = 16;
    private const int MaxVertices = 65536;
    private const int MaxIndices = 196608;
    private const int MaxTextures = 8;
    private const int IndexShift = 0;   // bits 0-15 (16 bits)
    private const int OrderShift = 16;  // bits 16-31 (16 bits)
    private const int GroupShift = 32;  // bits 32-47 (16 bits)
    private const int LayerShift = 48;  // bits 48-59 (12 bits, mask to 0xFFF)
    private const int PassShift = 60;   // bits 60-63 (4 bits)
    private const long SortKeyMergeMask = 0x7FFFFFFFFFFF0000;

    public struct AutoState(bool pop) : IDisposable
    {
        private bool _pop = pop;
        readonly void IDisposable.Dispose() { if (_pop) PopState(); }
    }

    private struct BatchState()
    {
        public nuint Shader;
        public fixed ulong Textures[MaxTextures];
        public BlendMode BlendMode;
        public TextureFilter TextureFilter;
        public RectInt Viewport;
        public RectInt Scissor;
        public nuint Mesh;
        public bool ScissorEnabled;
        public byte Pass;
        public ushort GlobalsIndex;
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
        public TextureFilter TextureFilter;
        public RectInt Viewport;
        public bool ScissorEnabled;
        public RectInt Scissor;
        public nuint Mesh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlobalsSnapshot
    {
        public Matrix4x4 Projection;
        public float Time;
    }

    private const int MaxBonesPerEntity = 64;
    private const int MaxBoneRows = 1024;
    private const int BoneTextureWidth = 128; // 64 bones * 2 texels per bone
    private static int _boneRow;
    private static float _time;
    private static Shader? _compositeShader;
    private static Shader? _spriteShader;
    private static bool _inUIPass;
    private static GraphicsStats _stats;
    private static State[] _stateStack = null!;
    private static Matrix3x2[] _bones = null!;
    private static int _stateStackDepth = 0;
    private static bool _batchStateDirty = true;
    
    public static ApplicationConfig Config { get; private set; } = null!;
    public static GraphicsConfig RenderConfig => Config.Graphics!;
    public static IGraphicsDriver Driver { get; private set; } = null!;
    public static Camera? Camera { get; private set; }
    public static Texture? SpriteAtlas { get; set; }
    public static ref readonly Matrix3x2 Transform => ref CurrentState.Transform;
    public static Color Color => CurrentState.Color;
    public static ref readonly GraphicsStats Stats => ref _stats;
    public static float PixelsPerUnit { get; private set; }
    public static float PixelsPerUnitInv {  get; private set; }

    private static ref State CurrentState => ref _stateStack[_stateStackDepth];
    
    #region Batching
    private static nuint _mesh;
    private static nuint _boneTexture;
    private const int BoneTextureSlot = 1;
    private static byte _currentPass;
    private static byte _activeDriverPass;
    private static Matrix4x4 _sceneProjection;
    private static Matrix4x4 _uiProjection;
    private static NativeArray<float> _boneData;
    private static int _maxDrawCommands;
    private static int _maxBatches;
    private static NativeArray<MeshVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static NativeArray<ushort> _sortedIndices;
    private static NativeArray<DrawCommand> _commands;
    private static NativeArray<Batch> _batches;
    private static NativeArray<BatchState> _batchStates;
    private static NativeArray<GlobalsSnapshot> _globalsSnapshots;
    private static ushort _currentBatchState;
    #endregion
    
    public static Color ClearColor { get; set; } = Color.Black;  
    
    public static void Init(ApplicationConfig config)
    {
        Config = config;

        var graphicsConfig = config.Graphics ?? throw new ArgumentNullException(
            nameof(config.Graphics),
            "Render config must be provided.");

        Driver = graphicsConfig.Driver ?? throw new ArgumentNullException(
            nameof(graphicsConfig.Driver),
            "Driver must be provided");

        _maxDrawCommands = RenderConfig.MaxDrawCommands;
        _maxBatches = RenderConfig.MaxBatches;
        _stateStack = new State[MaxStateStack];
        _stateStackDepth = 0;

        PixelsPerUnit = graphicsConfig.PixelsPerUnit;
        PixelsPerUnitInv = 1.0f / PixelsPerUnit;

        Driver.Init(new GraphicsDriverConfig
        {
            Platform = config.Platform!,
            VSync = config.VSync,
        });

        Camera = new Camera();

         InitBatcher();
         InitState();
    }

    private static void InitState()
    {
        _bones = new Matrix3x2[MaxBonesPerEntity];
        ResetState();
    }

    private static void ResetState()
    {
        _stateStackDepth = 0;
        _currentBatchState = 0;
        CurrentState.Transform = Matrix3x2.Identity;
        CurrentState.SortGroup = 0;
        CurrentState.SortLayer = 0;
        CurrentState.Color = Color.White;
        CurrentState.Shader = null;
        CurrentState.BlendMode = default;
        CurrentState.TextureFilter = TextureFilter.Point;
        for (var i = 0; i < MaxTextures; i++)
            CurrentState.Textures[i] = 0;

        CurrentState.ScissorEnabled = false;
        CurrentState.Scissor = RectInt.Zero;
        CurrentState.Viewport = RectInt.Zero;
        CurrentState.Mesh = _mesh;

        _currentPass = 0;
        _boneRow = 1;

        var size = Application.WindowSize;
        SetViewport(0, 0, (int)size.X, (int)size.Y);
    }
    
    private static void InitBatcher()
    {
        _vertices = new NativeArray<MeshVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _sortedIndices = new NativeArray<ushort>(MaxIndices);
        _commands = new NativeArray<DrawCommand>(_maxDrawCommands);
        _batches = new NativeArray<Batch>(_maxBatches);
        _batchStates = new NativeArray<BatchState>(_maxBatches);
        _globalsSnapshots = new NativeArray<GlobalsSnapshot>(64);

        _mesh = Driver.CreateMesh<MeshVertex>(
            MaxVertices,
            MaxIndices,
            BufferUsage.Dynamic,
            "Graphics.Main"
        );

        var boneDataLength = BoneTextureWidth * MaxBoneRows * 4;
        _boneData = new NativeArray<float>(boneDataLength, boneDataLength);
        _boneData[0] = 1; _boneData[1] = 0; _boneData[2] = 0; _boneData[3] = 0;
        _boneData[4] = 0; _boneData[5] = 1; _boneData[6] = 0; _boneData[7] = 0;
        _boneTexture = Driver.CreateTexture(
            BoneTextureWidth, MaxBoneRows,
            ReadOnlySpan<byte>.Empty,
            TextureFormat.RGBA32F,
            TextureFilter.Point,
            name: "Bones");
    }

    public static void Shutdown()
    {
        _batches.Dispose();
        _vertices.Dispose();
        _commands.Dispose();
        _indices.Dispose();
        _globalsSnapshots.Dispose();

        Driver.DestroyMesh(_mesh);
        Driver.DestroyTexture(_boneTexture);

        Driver.Shutdown();

        _mesh = 0;
        _boneTexture = 0;
    }

    public static bool IsScissor => CurrentState.ScissorEnabled;

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

    public static void SetColor(Color color)
    {
        CurrentState.Color = color;
    }

    public static void SetCamera(Camera? camera)
    {
        Camera = camera;
        _batchStateDirty = true;
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

        if (_currentPass == 0)
            _sceneProjection = projection;
        else
            _uiProjection = projection;
    }

    internal static bool BeginFrame()
    {
        ResetState();

        if (!Driver.BeginFrame())
            return false;

        Driver.ClearScissor();

        _time += Time.DeltaTime;

        // Ensure offscreen target matches window size
        var size = Application.WindowSize;
        Driver.ResizeOffscreenTarget((int)size.X, (int)size.Y, RenderConfig.MsaaSamples);

        _inUIPass = false;
        _activeDriverPass = 0;

        Driver.BeginScenePass(ClearColor);

        return true;
    }

    public static void BeginUI()
    {
        if (_inUIPass) return;
        _currentPass = 1;
        _inUIPass = true;
        _batchStateDirty = true;
    }

    internal static void EndFrame()
    {
        ExecuteCommands();

        // Close whatever pass is currently active
        if (_activeDriverPass == 0)
        {
            Driver.EndScenePass();
            if (_compositeShader != null)
                Driver.Composite(_compositeShader.Handle);
        }
        else
        {
            Driver.EndUIPass();
        }

        _inUIPass = false;

        Driver.EndFrame();
    }

    internal static void ResolveAssets()
    {
        _compositeShader = Asset.Get<Shader>(AssetType.Shader, RenderConfig.CompositeShader)
            ?? throw new ArgumentNullException(nameof(RenderConfig.CompositeShader), "Composite shader not found");

        _spriteShader = Asset.Get<Shader>(AssetType.Shader, RenderConfig.SpriteShader)
            ?? throw new ArgumentNullException(nameof(RenderConfig.SpriteShader), "Sprite shader not found");
    }

    public static void Clear(Color color)
    {
        Driver.Clear(color);
    }

    public static void SetViewport(int x, int y, int width, int height) =>
        SetViewport(new RectInt(x, y, width, height));

    public static void SetViewport(in RectInt viewport)
    {
        if (CurrentState.Viewport == viewport)
            return;

        CurrentState.Viewport = viewport;
        _batchStateDirty = true;
    }

    public static void SetScissor(int x, int y, int width, int height) =>
        SetScissor(new RectInt(x, y, width, height));

    public static void SetScissor(in RectInt scissor)
    {
        if (CurrentState.ScissorEnabled && CurrentState.Scissor == scissor)
            return;

        CurrentState.ScissorEnabled = true;
        CurrentState.Scissor = scissor;
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
        Draw(rect.X, rect.Y, rect.Width, rect.Height, order: order);

    public static void Draw(in Rect rect, in Rect uv, ushort order = 0) =>
        Draw(rect.X, rect.Y, rect.Width, rect.Height, uv.Left, uv.Top, uv.Right, uv.Bottom, order:order);

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

    public static void Draw(Sprite sprite) => Draw(sprite, 0);

    public static void Draw(Sprite sprite, ushort order)
    {
        if (sprite == null || SpriteAtlas == null) return;

        var uv = sprite.UV;
        var bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);

        using (PushState())
        {
            SetTexture(SpriteAtlas);
            SetShader(_spriteShader!);
            SetTextureFilter(sprite.TextureFilter);
            AddQuad(
                p0, p1, p2, p3,
                uv.TopLeft, new Vector2(uv.Right, uv.Top),
                uv.BottomRight, new Vector2(uv.Left, uv.Bottom),
                order, atlasIndex: sprite.AtlasIndex);
        }
    }

    #endregion

    public static void SetSortGroup(ushort group)
    {
        Debug.Assert((group & 0xFFFF) == group);
        CurrentState.SortGroup = group;
    }

    public static void SetLayer(ushort layer)
    {
        Debug.Assert((layer & 0xFFF) == layer);
        CurrentState.SortLayer = layer;
    }

    public static AutoState PushState()
    {
        if (_stateStackDepth >= MaxStateStack - 1)
            return new AutoState(false);

        ref var current = ref _stateStack[_stateStackDepth];
        ref var next = ref _stateStack[++_stateStackDepth];

        next = current;
        return new AutoState(true);
    }

    public static void PopState()
    {
        if (_stateStackDepth == 0)
            return;

        ref var current = ref _stateStack[_stateStackDepth];
        ref var prev = ref _stateStack[--_stateStackDepth];

        var shaderChanged = current.Shader != prev.Shader;
        var blendChanged = current.BlendMode != prev.BlendMode;
        var filterChanged = current.TextureFilter != prev.TextureFilter;
        var texturesChanged = false;
        for (var i = 0; i < MaxTextures && !texturesChanged; i++)
            texturesChanged = current.Textures[i] != prev.Textures[i];
        var viewportChanged = current.Viewport != prev.Viewport;
        var scissorChanged = current.ScissorEnabled != prev.ScissorEnabled ||
                             current.Scissor != prev.Scissor;
        var meshChanged = current.Mesh != prev.Mesh;

        if (shaderChanged || blendChanged || filterChanged || texturesChanged || viewportChanged || scissorChanged || meshChanged)
            _batchStateDirty = true;
    }

    public static void SetBlendMode(BlendMode blendMode)
    {
        CurrentState.BlendMode = blendMode;
        _batchStateDirty = true;
    }

    public static void SetTextureFilter(TextureFilter filter)
    {
        CurrentState.TextureFilter = filter;
        _batchStateDirty = true;
    }

    public const int MaxBoneTransforms = MaxBonesPerEntity;

    public static void SetBones(ReadOnlySpan<Matrix3x2> transforms)
    {
        Debug.Assert(transforms.Length <= MaxBonesPerEntity);
        Debug.Assert(_boneRow < MaxBoneRows);

        // BoneIndex is flat index: row * 64, so vertex bone index + BoneIndex = flat index
        CurrentState.BoneIndex = (ushort)(_boneRow * MaxBonesPerEntity);

        // Write transforms to the current row in _boneData
        // Each bone is 2 texels (8 floats): [M11,M12,M31,0], [M21,M22,M32,0]
        var rowOffset = _boneRow * BoneTextureWidth * 4;
        for (var i = 0; i < transforms.Length; i++)
        {
            ref readonly var m = ref transforms[i];
            var texelOffset = rowOffset + i * 8;
            // Texel 0: M11, M12, M31, 0
            _boneData[texelOffset + 0] = m.M11;
            _boneData[texelOffset + 1] = m.M12;
            _boneData[texelOffset + 2] = m.M31;
            _boneData[texelOffset + 3] = 0;
            // Texel 1: M21, M22, M32, 0
            _boneData[texelOffset + 4] = m.M21;
            _boneData[texelOffset + 5] = m.M22;
            _boneData[texelOffset + 6] = m.M32;
            _boneData[texelOffset + 7] = 0;
        }

        _boneRow++;
    }

    private static void UploadBones()
    {
        Driver.UpdateTextureRegion(_boneTexture, new RectInt(0,0,BoneTextureWidth,_boneRow), _boneData.AsByteSpan(), BoneTextureWidth);
    }

    private static void UploadGlobals()
    {
        var count = _globalsSnapshots.Length;
        if (count == 0)
            return;

        Driver.SetGlobalsCount(count);

        var data = stackalloc byte[80];
        for (int i = 0; i < count; i++)
        {
            ref var snapshot = ref _globalsSnapshots[i];
            var transposed = Matrix4x4.Transpose(snapshot.Projection);
            Buffer.MemoryCopy(&transposed, data, 64, 64);
            *(float*)(data + 64) = snapshot.Time;
            Driver.SetGlobals(i, new ReadOnlySpan<byte>(data, 80));
        }
    }

    public static void SetTransform(in Matrix3x2 transform)
    {
        CurrentState.Transform = transform;
    }

    private static long MakeSortKey(ushort order) =>
        (((long)_currentPass) << PassShift) |
        (((long)(CurrentState.SortLayer & 0xFFF)) << LayerShift) |
        (((long)CurrentState.SortGroup) << GroupShift) |
        (((long)order) << OrderShift) |
        (((long)_commands.Length) << IndexShift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetOrAddGlobals(in Matrix4x4 projection)
    {
        // Only compare projection - time is same for all batches in a frame
        for (int i = 0; i < _globalsSnapshots.Length; i++)
            if (_globalsSnapshots[i].Projection == projection)
                return (ushort)i;

        var index = (ushort)_globalsSnapshots.Length;
        _globalsSnapshots.Add() = new GlobalsSnapshot { Projection = projection, Time = _time };
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBatchState()
    {
        _batchStateDirty = false;

        var currentProjection = _currentPass == 0 ? _sceneProjection : _uiProjection;

        var candidate = new BatchState
        {
            Pass = _currentPass,
            GlobalsIndex = GetOrAddGlobals(currentProjection),
            Shader = CurrentState.Shader?.Handle ?? nuint.Zero,
            BlendMode = CurrentState.BlendMode,
            TextureFilter = CurrentState.TextureFilter,
            Viewport = CurrentState.Viewport,
            ScissorEnabled = CurrentState.ScissorEnabled,
            Scissor = CurrentState.Scissor,
            Mesh = CurrentState.Mesh
        };

        for (int t = 0; t < MaxTextures; t++)
            candidate.Textures[t] = CurrentState.Textures[t];

        var candidateSpan = new ReadOnlySpan<byte>(&candidate, sizeof(BatchState));
        for (int i = 0; i < _batchStates.Length; i++)
        {
            var existingSpan = new ReadOnlySpan<byte>(Unsafe.AsPointer(ref _batchStates[i]), sizeof(BatchState));
            if (candidateSpan.SequenceEqual(existingSpan))
            {
                _currentBatchState = (ushort)i;
                return;
            }
        }

        _currentBatchState = (ushort)_batchStates.Length;
        _batchStates.Add() = candidate;

        LogGraphics(
            $"AddBatchState: Pass={candidate.Pass}" +
            $" Globals={candidate.GlobalsIndex}" +
            $" Shader=0x{candidate.Shader:X}" +
            $" ({Asset.Get<Shader>(AssetType.Shader, candidate.Shader)?.Name ?? "???"})" +
            $" Texture0=0x{candidate.Textures[0]:X}" +
            $" Texture1=0x{candidate.Textures[1]:X}" +
            $" BlendMode={candidate.BlendMode}" +
            $" Viewport=({candidate.Viewport})" +
            $" Scissor={(candidate.ScissorEnabled?candidate.Scissor.ToString():"None")}" +
            $" Mesh=0x{candidate.Mesh:X}");
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
        ushort order,
        int atlasIndex = 0)
    {
        Span<MeshVertex> verts = stackalloc MeshVertex[4];
        verts[0] = new MeshVertex { Position = p0, UV = uv0, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1 };
        verts[1] = new MeshVertex { Position = p1, UV = uv1, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1 };
        verts[2] = new MeshVertex { Position = p2, UV = uv2, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1 };
        verts[3] = new MeshVertex { Position = p3, UV = uv3, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1 };

        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        AddTriangles(verts, indices, order);
    }

    public static void AddTriangles(ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<ushort> indices, ushort order = 0)
    {
        if (CurrentState.Shader == null)
            return;

        SetMesh(_mesh);

        if (_batchStateDirty)
            AddBatchState();

        if (_commands.Length >= _maxDrawCommands)
            return;

        if (_vertices.Length + vertices.Length > MaxVertices ||
            _indices.Length + indices.Length > MaxIndices)
            return;

        ref var cmd = ref _commands.Add();
        cmd.SortKey = MakeSortKey(order);
        cmd.IndexOffset = _indices.Length;
        cmd.IndexCount = indices.Length;
        cmd.BatchState = _currentBatchState;

        var baseVertex = _vertices.Length;
        foreach (var v in vertices)
        {
            var transformed = v;
            transformed.Position = Vector2.Transform(v.Position, CurrentState.Transform);
            transformed.Color = CurrentState.Color;
            _vertices.Add(transformed);
        }

        foreach (var idx in indices)
            _indices.Add((ushort)(baseVertex + idx));
    }

    private static void AddBatch(ushort batchState, int indexOffset, int indexCount)
    {
        if (indexCount == 0)
            return;
        
        ref var batch = ref _batches.Add();
        batch.IndexOffset = indexOffset;
        batch.IndexCount = indexCount;
        batch.State = batchState;
    }

    public static void SetMesh(nuint vertexArray)
    {
        if (CurrentState.Mesh == vertexArray) return;
        CurrentState.Mesh = vertexArray;
        _batchStateDirty = true;
    }

    public static void DrawElements(int indexCount, int indexOffset = 0, ushort order=0)
    {
        if (_batchStateDirty)
            AddBatchState();

        var sortKey = MakeSortKey(order);
        
        if (_commands.Length > 0)
        {
            ref var lastCommand = ref _commands[^1];
            bool testa = lastCommand.BatchState == _currentBatchState;
            bool testb = (lastCommand.SortKey & SortKeyMergeMask) == (sortKey & SortKeyMergeMask);
            bool testc = lastCommand.IndexOffset + lastCommand.IndexCount == indexOffset;

            if (lastCommand.BatchState == _currentBatchState &&
                (lastCommand.SortKey & SortKeyMergeMask) == (sortKey & SortKeyMergeMask) &&
                lastCommand.IndexOffset + lastCommand.IndexCount == indexOffset)
            {
                lastCommand.IndexCount += (ushort)indexCount;
                LogGraphicsVerbose($"DrawElements (MERGE): BatchState={lastCommand.BatchState} Count={lastCommand.IndexCount} Offset={indexOffset} Order={order}");
                return;
            }

            testc = false;
        }

        ref var cmd = ref _commands.Add();
        cmd.SortKey = sortKey;
        cmd.IndexOffset = indexOffset;
        cmd.IndexCount = indexCount;
        cmd.BatchState = _currentBatchState;

        LogGraphicsVerbose($"DrawElements: BatchState={cmd.BatchState} SortKey={sortKey} Count={indexCount} Offset={indexOffset} Order={order}");
    }

    private static void CreateBatches()
    {
        _batches.Clear();

        if (_commands.Length == 0)
            return;

        _batches.Add();
        _sortedIndices.Clear();

        ref var firstBatch = ref _batches[0];
        ref var firstState = ref _batchStates[_commands[0].BatchState];
        firstBatch.IndexOffset = firstState.Mesh != _mesh ? _commands[0].IndexOffset : 0;
        firstBatch.IndexCount = _commands[0].IndexCount;
        firstBatch.State = _commands[0].BatchState;

        if (firstState.Mesh == _mesh)
            _sortedIndices.AddRange(
                _indices.AsReadonlySpan(_commands[0].IndexOffset, _commands[0].IndexCount)
            );

        for (int commandIndex = 1, commandCount = _commands.Length; commandIndex < commandCount; commandIndex++)
        {
            ref var cmd = ref _commands[commandIndex];
            ref var cmdState = ref _batchStates[cmd.BatchState];

            LogGraphicsVerbose($"  Command: Index={commandIndex}  SortKey={cmd.SortKey}  IndexOffset={cmd.IndexOffset} IndexCount={cmd.IndexCount} State={cmd.BatchState}");

            // Always break batch on external vertex arrays.
            if (cmdState.Mesh != _mesh)
            {
                ref var newBatch = ref _batches.Add();
                newBatch.IndexOffset = cmd.IndexOffset;
                newBatch.IndexCount = cmd.IndexCount;
                newBatch.State = cmd.BatchState;
                continue;
            }

            ref var currentBatch = ref _batches[^1];
            if (cmd.BatchState != currentBatch.State)
            {
                ref var newBatch = ref _batches.Add();
                newBatch.IndexOffset = _sortedIndices.Length;
                newBatch.IndexCount = cmd.IndexCount;
                newBatch.State = cmd.BatchState;
            }
            else
            {
                currentBatch.IndexCount += cmd.IndexCount;
            }

            _sortedIndices.AddRange(
                _indices.AsReadonlySpan(cmd.IndexOffset, cmd.IndexCount)
            );
        }
    }
    
    private static void ExecuteCommands()
    {
        if (_commands.Length == 0)
            return;

        TextRender.Flush();
        UIRender.Flush();

        _commands.AsSpan().Sort();

        CreateBatches();

        LogGraphics(
            $"ExecuteCommands: BatchStates={_batchStates.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");

        var lastViewport = RectInt.Zero;
        var lastScissorEnabled = false;
        var lastScissor = RectInt.Zero;
        var lastShader = nuint.Zero;
        var lastTextures = stackalloc nuint[MaxTextures];
        for (int i = 0; i < MaxTextures; i++)
            lastTextures[i] = nuint.Zero;
        var lastMesh = nuint.Zero;
        var lastBlendMode = BlendMode.None;
        var lastTextureFilter = TextureFilter.Point;
        var lastGlobalsIndex = ushort.MaxValue;

        if (_vertices.Length > 0 || _indices.Length > 0)
        {
            // Pad indices to 4-byte alignment for WebGPU
            if ((_sortedIndices.Length & 1) != 0)
                _sortedIndices.Add(0);

            Driver.BindMesh(_mesh);
            Driver.UpdateMesh(_mesh, _vertices.AsByteSpan(), _sortedIndices.AsSpan());
            Driver.SetBlendMode(BlendMode.None);
            Driver.SetTextureFilter(TextureFilter.Linear);
            lastMesh = _mesh;
        }

        Driver.SetTextureFilter(TextureFilter.Point);

        UploadBones();
        Driver.BindTexture(_boneTexture, BoneTextureSlot);

        // Upload all globals snapshots to driver
        UploadGlobals();

        LogGraphics($"ExecuteBatches: Batches={_batches.Length} BatchStates={_batchStates.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");
        LogGraphics($"  BeginPass: Scene");

        for (int batchIndex = 0, batchCount = _batches.Length; batchIndex < batchCount; batchIndex++)
        {
            ref var batch = ref _batches[batchIndex];
            ref var batchState = ref _batchStates[batch.State];

            // Handle pass transitions
            if (batchState.Pass != _activeDriverPass)
            {
                if (_activeDriverPass == 0 && batchState.Pass == 1)
                {
                    // Transition from scene to UI
                    Driver.EndScenePass();
                    if (_compositeShader != null)
                        Driver.Composite(_compositeShader.Handle);
                    Driver.BeginUIPass();
                    _activeDriverPass = 1;

                    SetBlendMode(BlendMode.Alpha);

                    // Reset all tracking state to force rebinding in new render pass
                    lastScissor = RectInt.Zero;
                    lastViewport = RectInt.Zero;
                    lastScissorEnabled = false;
                    lastShader = nuint.Zero;
                    lastMesh = nuint.Zero;
                    lastBlendMode = BlendMode.None;
                    lastGlobalsIndex = ushort.MaxValue;
                    lastTextureFilter = TextureFilter.Point;
                    for (int i = 0; i < MaxTextures; i++)
                        lastTextures[i] = nuint.Zero;

                    LogGraphics($"  BeginPass: UI");
                }
            }

            LogGraphics($"  Batch: Index={batchIndex} IndexOffset={batch.IndexOffset} IndexCount={batch.IndexCount} State={batch.State} Pass={batchState.Pass}");

            if (lastViewport != batchState.Viewport)
            {
                LogGraphics($"    SetViewport: {batchState.Viewport}");

                lastViewport = batchState.Viewport;
                Driver.SetViewport(batchState.Viewport);
            }

            if (lastScissorEnabled != batchState.ScissorEnabled || lastScissor != batchState.Scissor)
            {
                LogGraphics($"    SetScissor: {(batchState.ScissorEnabled ? batchState.Scissor.ToString() : "None")}");

                lastScissorEnabled = batchState.ScissorEnabled;
                lastScissor = batchState.Scissor;

                if (batchState.ScissorEnabled)
                    Driver.SetScissor(batchState.Scissor);
                else
                    Driver.ClearScissor();
            }

            if (lastShader != batchState.Shader)
            {
                lastShader = batchState.Shader;
                Driver.BindShader(batchState.Shader);
                LogGraphics($"    BindShader: Handle=0x{batchState.Shader:X} ({Asset.Get<Shader>(AssetType.Shader, batchState.Shader)?.Name ?? "???"})");
            }

            if (lastGlobalsIndex != batchState.GlobalsIndex)
            {
                lastGlobalsIndex = batchState.GlobalsIndex;
                Driver.BindGlobals(batchState.GlobalsIndex);
                LogGraphics($"    BindGlobals: Index={batchState.GlobalsIndex}");
            }

            for (int t = 0; t < MaxTextures; t++)
            {
                if (lastTextures[t] != (nuint)batchState.Textures[t])
                {
                    lastTextures[t] = (nuint)batchState.Textures[t];
                    if (batchState.Textures[t] != 0)
                    {
                        Driver.BindTexture((nuint)batchState.Textures[t], t);
                        LogGraphics($"    BindTexture: Slot={t} Handle=0x{batchState.Textures[t]:X} ({Asset.Get<Texture>(AssetType.Texture, (nuint)batchState.Textures[t])?.Name ?? "???"})");
                    }
                }
            }

            if (lastBlendMode != batchState.BlendMode)
            {
                lastBlendMode = batchState.BlendMode;
                Driver.SetBlendMode(batchState.BlendMode);
                LogGraphics($"    SetBlendMode: {batchState.BlendMode}");
            }

            if (lastTextureFilter != batchState.TextureFilter)
            {
                lastTextureFilter = batchState.TextureFilter;
                Driver.SetTextureFilter(batchState.TextureFilter);
                LogGraphics($"    SetTextureFilter: {batchState.TextureFilter}");
            }

            if (lastMesh != batchState.Mesh)
            {
                lastMesh = batchState.Mesh;
                Driver.BindMesh(batchState.Mesh);
                LogGraphics($"    BindMesh: Handle=0x{batchState.Mesh:X}");
            }

            LogGraphics($"    DrawElements: IndexCount={batch.IndexCount} IndexOffset={batch.IndexOffset}");
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
        _globalsSnapshots.Clear();
        
        _batchStateDirty = true;
        _currentBatchState = 0;
    }

    [Conditional("NOZ_GRAPHICS_DEBUG")]
    private static void LogGraphics(string msg)
    {
        Log.Debug($"[GRAPHICS] {msg}");
    }

    [Conditional("NOZ_GRAPHICS_DEBUG_VERBOSE")]
    private static void LogGraphicsVerbose(string msg)
    {
        Log.Debug($"[GRAPHICS] {msg}");
    }
}
