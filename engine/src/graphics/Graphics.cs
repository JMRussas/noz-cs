//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_RENDER_DEBUG
#define NOZ_RENDER_DEBUG_VERBOSE

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using NoZ.Platform;

namespace NoZ;

public static unsafe class Graphics
{
    private const int MaxSortGroups = 1526;
    private const int MaxStateStack = 16;
    private const int MaxVertices = 65536;
    private const int MaxIndices = 196608;
    private const int MaxTextures = 8;
    private const int IndexShift = 0;
    private const int OrderShift = 16;
    private const int GroupShift = 32;
    private const int LayerShift = 48;
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
        public int ViewportX;
        public int ViewportY;
        public int ViewportW;
        public int ViewportH;
        public int ScissorX;
        public int ScissorY;
        public int ScissorWidth;
        public int ScissorHeight;
        public nuint Mesh;
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
        public nuint Mesh;
    }

    private const int MaxBonesPerEntity = 64;
    private const int MaxBoneRows = 1024;
    private const int BoneTextureWidth = 128; // 64 bones * 2 texels per bone
    private static int _boneRow;
    private static float _time;
    private static Shader? _compositeShader;
    private static Shader? _spriteShader;
    private static bool _inUIPass;
    private static bool _inScenePass;
    private static GraphicsStats _stats;
    private static ushort[] _sortGroupStack = null!;
    private static State[] _stateStack = null!;
    private static Matrix3x2[] _bones = null!;
    private static ushort _sortGroupStackDepth = 0;
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
    private static nuint _globalsUbo;
    private static nuint _boneTexture;
    private const int GlobalsUboBindingPoint = 0;
    private const int BoneTextureSlot = 1;
    private static Matrix4x4 _projection;
    private static NativeArray<float> _boneData;
    private static int _maxDrawCommands;
    private static int _maxBatches;
    private static NativeArray<MeshVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static NativeArray<ushort> _sortedIndices;
    private static NativeArray<DrawCommand> _commands;
    private static NativeArray<Batch> _batches;
    private static NativeArray<BatchState> _batchStates;
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
        _sortGroupStack = new ushort[MaxSortGroups];
        _stateStack = new State[MaxStateStack];
        _sortGroupStackDepth = 0;
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
        for (var i = 0; i < MaxTextures; i++)
            CurrentState.Textures[i] = 0;

        CurrentState.ScissorEnabled = false;
        CurrentState.ScissorX = 0;
        CurrentState.ScissorY = 0;
        CurrentState.ScissorWidth = 0;
        CurrentState.ScissorHeight = 0;

        CurrentState.Mesh = _mesh;

        _boneRow = 1; // Row 0 is identity, start from row 1

        CurrentState.ViewportX = -1;
        CurrentState.ViewportY = -1;
        CurrentState.ViewportWidth = -1;
        CurrentState.ViewportHeight = -1;
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

        _mesh = Driver.CreateMesh<MeshVertex>(
            MaxVertices,
            MaxIndices,
            BufferUsage.Dynamic,
            "Render.Main"
        );

        _globalsUbo = Driver.CreateUniformBuffer(80, BufferUsage.Dynamic, "Globals");

        var boneDataLength = BoneTextureWidth * MaxBoneRows * 4;
        _boneData = new NativeArray<float>(boneDataLength, boneDataLength);
        _boneData[0] = 1; _boneData[1] = 0; _boneData[2] = 0; _boneData[3] = 0;
        _boneData[4] = 0; _boneData[5] = 1; _boneData[6] = 0; _boneData[7] = 0;
        _boneTexture = Driver.CreateTexture(
            BoneTextureWidth, MaxBoneRows,
            ReadOnlySpan<byte>.Empty,
            TextureFormat.RGBA32F,
            TextureFilter.Nearest);
    }

    public static void Shutdown()
    {
        _batches.Dispose();
        _vertices.Dispose();
        _commands.Dispose();
        _indices.Dispose();

        Driver.DestroyMesh(_mesh);
        Driver.DestroyBuffer(_globalsUbo);
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
        _projection = new Matrix4x4(
            view.M11, view.M12, 0, view.M31,
            view.M21, view.M22, 0, view.M32,
            0, 0, 1, 0,
            0, 0, 0, 1
        );
    }

    internal static void BeginFrame()
    {
        ResetState();

        Driver.BeginFrame();
        Driver.DisableScissor();

        _time += Time.DeltaTime;

        // Ensure offscreen target matches window size
        var size = Application.WindowSize;
        Driver.ResizeOffscreenTarget((int)size.X, (int)size.Y, RenderConfig.MsaaSamples);

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
        _compositeShader = Asset.Get<Shader>(AssetType.Shader, RenderConfig.CompositeShader)
            ?? throw new ArgumentNullException(nameof(RenderConfig.CompositeShader), "Composite shader not found");

        _spriteShader = Asset.Get<Shader>(AssetType.Shader, RenderConfig.SpriteShader)
            ?? throw new ArgumentNullException(nameof(RenderConfig.SpriteShader), "Sprite shader not found");
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

    public static void Draw(Sprite? sprite, ushort order = 0)
    {
        if (sprite == null || SpriteAtlas == null) return;

        var uv = sprite.UV;
        var bounds = sprite.Bounds.ToRect().Scale(Graphics.PixelsPerUnitInv);
        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);

        SetTexture(SpriteAtlas);
        SetShader(_spriteShader!);
        AddQuad(
            p0, p1, p2, p3,
            uv.TopLeft, new Vector2(uv.Right, uv.Top),
            uv.BottomRight, new Vector2(uv.Left, uv.Bottom),
            order, atlasIndex: sprite.AtlasIndex);
    }

    #endregion

    public static void SetLayer(ushort layer)
    {
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
        Driver.UpdateTexture(_boneTexture, BoneTextureWidth, MaxBoneRows, _boneData.AsByteSpan());
    }

    private static void UploadGlobals()
    {
        // Globals UBO layout (std140):
        // offset 0: mat4 u_projection (64 bytes, column-major)
        // offset 64: float u_time (4 bytes, padded to 16)
        // Total: 80 bytes
        var data = stackalloc byte[80];
        var transposed = Matrix4x4.Transpose(_projection);
        Buffer.MemoryCopy(&transposed, data, 64, 64);
        *(float*)(data + 64) = _time;
        Driver.UpdateUniformBuffer(_globalsUbo, 0, new ReadOnlySpan<byte>(data, 80));
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

        var shader = CurrentState.Shader?.Handle ?? nuint.Zero;
        var blendMode = CurrentState.BlendMode;
        var viewportX = CurrentState.ViewportX;
        var viewportY = CurrentState.ViewportY;
        var viewportW = CurrentState.ViewportWidth;
        var viewportH = CurrentState.ViewportHeight;
        var scissorEnabled = CurrentState.ScissorEnabled;
        var scissorX = CurrentState.ScissorX;
        var scissorY = CurrentState.ScissorY;
        var scissorWidth = CurrentState.ScissorWidth;
        var scissorHeight = CurrentState.ScissorHeight;
        var vertexArray = CurrentState.Mesh;

        for (int i = 0; i < _batchStates.Length; i++)
        {
            ref var existing = ref _batchStates[i];
            if (existing.Shader != shader ||
                existing.BlendMode != blendMode ||
                existing.ViewportX != viewportX ||
                existing.ViewportY != viewportY ||
                existing.ViewportW != viewportW ||
                existing.ViewportH != viewportH ||
                existing.ScissorEnabled != scissorEnabled ||
                existing.ScissorX != scissorX ||
                existing.ScissorY != scissorY ||
                existing.ScissorWidth != scissorWidth ||
                existing.ScissorHeight != scissorHeight ||
                existing.Mesh != vertexArray)
                continue;

            bool texturesMatch = true;
            for (int t = 0; t < MaxTextures; t++)
            {
                if (existing.Textures[t] != CurrentState.Textures[t])
                {
                    texturesMatch = false;
                    break;
                }
            }

            if (texturesMatch)
            {
                _currentBatchState = (ushort)i;
                return;
            }
        }

        _currentBatchState = (ushort)_batchStates.Length;
        ref var batchState = ref _batchStates.Add();
        batchState.Shader = shader;
        batchState.BlendMode = blendMode;
        for (int t = 0; t < MaxTextures; t++)
            batchState.Textures[t] = CurrentState.Textures[t];
        batchState.ViewportX = viewportX;
        batchState.ViewportY = viewportY;
        batchState.ViewportW = viewportW;
        batchState.ViewportH = viewportH;
        batchState.ScissorEnabled = scissorEnabled;
        batchState.ScissorX = scissorX;
        batchState.ScissorY = scissorY;
        batchState.ScissorWidth = scissorWidth;
        batchState.ScissorHeight = scissorHeight;
        batchState.Mesh = vertexArray;

        LogRender($"AddBatchState: Shader=0x{batchState.Shader:X} Texture0=0x{batchState.Textures[0]:X} Texture1=0x{batchState.Textures[1]:X} BlendMode={batchState.BlendMode} Viewport=({batchState.ViewportX},{batchState.ViewportY},{batchState.ViewportW},{batchState.ViewportH}) ScissorEnabled={batchState.ScissorEnabled} Scissor=({batchState.ScissorX},{batchState.ScissorY},{batchState.ScissorWidth},{batchState.ScissorHeight}) Mesh=0x{batchState.Mesh:X}");
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
        if (CurrentState.Shader == null)
            return;

        if (_batchStates.Length == 0 ||
            _batchStates[^1].Mesh != _mesh)
        {
            SetMesh(_mesh);
        }

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
        cmd.BatchState = _currentBatchState;

        var t0 = Vector2.Transform(p0, CurrentState.Transform);
        var t1 = Vector2.Transform(p1, CurrentState.Transform);
        var t2 = Vector2.Transform(p2, CurrentState.Transform);
        var t3 = Vector2.Transform(p3, CurrentState.Transform);

        var baseVertex = _vertices.Length;
        _vertices.Add(new MeshVertex { Position = t0, UV = uv0, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = atlasIndex, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });
        _vertices.Add(new MeshVertex { Position = t1, UV = uv1, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = atlasIndex, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });
        _vertices.Add(new MeshVertex { Position = t2, UV = uv2, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = atlasIndex, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });
        _vertices.Add(new MeshVertex { Position = t3, UV = uv3, Normal = Vector2.Zero, Color = CurrentState.Color, Bone = 0, Atlas = atlasIndex, FrameCount = 1, FrameWidth = 0, FrameRate = 0, AnimStartTime = 0 });

        _indices.Add((ushort)(baseVertex + 0));
        _indices.Add((ushort)(baseVertex + 1));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 3));
        _indices.Add((ushort)(baseVertex + 0));
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
                LogRenderVerbose($"DrawElements (MERGE): BatchState={lastCommand.BatchState} Count={lastCommand.IndexCount} Offset={indexOffset} Order={order}");
                return;
            }

            testc = false;
        }

        ref var cmd = ref _commands.Add();
        cmd.SortKey = sortKey;
        cmd.IndexOffset = indexOffset;
        cmd.IndexCount = indexCount;
        cmd.BatchState = _currentBatchState;

        LogRenderVerbose($"DrawElements: BatchState={cmd.BatchState} SortKey={sortKey} Count={indexCount} Offset={indexOffset} Order={order}");
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
            
            LogRenderVerbose($"  Command: Index={commandIndex}  SortKey={cmd.SortKey}  IndexOffset={cmd.IndexOffset} IndexCount={cmd.IndexCount} State={cmd.BatchState}");

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

        LogRender(
            $"ExecuteCommands: BatchStates={_batchStates.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");

        var lastViewportX = ushort.MaxValue;
        var lastViewportY = ushort.MaxValue;
        var lastViewportW = ushort.MaxValue;
        var lastViewportH = ushort.MaxValue;
        var lastScissorEnabled = false;
        var lastShader = nuint.Zero;
        var lastTextures = stackalloc nuint[MaxTextures];
        for (int i = 0; i < MaxTextures; i++)
            lastTextures[i] = nuint.Zero;
        var lastMesh = nuint.Zero;
        var lastBlendMode = BlendMode.None;

        if (_vertices.Length > 0 || _indices.Length > 0)
        {
            Driver.BindMesh(_mesh);
            Driver.UpdateMesh(_mesh, _vertices.AsByteSpan(), _sortedIndices.AsSpan());
            Driver.SetBlendMode(BlendMode.None);
            lastMesh = _mesh;
        }

        UploadGlobals();
        Driver.BindUniformBuffer(_globalsUbo, GlobalsUboBindingPoint);

        UploadBones();
        Driver.BindTexture(_boneTexture, BoneTextureSlot);

        LogRender($"ExecuteBatches: Batches={_batches.Length} BatchStates={_batchStates.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");
        
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

            for (int t = 0; t < MaxTextures; t++)
            {
                if (lastTextures[t] != (nuint)batchState.Textures[t])
                {
                    lastTextures[t] = (nuint)batchState.Textures[t];
                    if (batchState.Textures[t] != 0)
                    {
                        Driver.BindTexture((nuint)batchState.Textures[t], t);
                        LogRender($"    BindTexture: Slot={t} Handle=0x{batchState.Textures[t]:X}");
                    }
                }
            }

            if (lastBlendMode != batchState.BlendMode)
            {
                lastBlendMode = batchState.BlendMode;
                Driver.SetBlendMode(batchState.BlendMode);
                LogRender($"    SetBlendMode: {batchState.BlendMode}");
            }

            if (lastMesh != batchState.Mesh)
            {
                lastMesh = batchState.Mesh;
                Driver.BindMesh(batchState.Mesh);
                LogRender($"    BindMesh: Handle=0x{batchState.Mesh:X}");
            }

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
        _currentBatchState = 0;
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
