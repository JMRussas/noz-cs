//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

//#define NOZ_GRAPHICS_DEBUG
//#define NOZ_GRAPHICS_DEBUG_VERBOSE

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public enum RenderPass : byte
{
    RenderTexture = 0,
    Scene = 1,
}

public static unsafe partial class Graphics
{
    private const int MaxRenderPasses = 3;
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

    private struct BatchState()
    {
        public nuint Shader;
        public fixed ulong Textures[MaxTextures];
        public fixed byte TextureFilters[MaxTextures];
        public BlendMode BlendMode;
        public RectInt Viewport;
        public RectInt Scissor;
        public nuint Mesh;
        public bool ScissorEnabled;
        public byte Pass;
        public ushort GlobalsIndex;
        public nuint RenderTextureHandle;  // 0 = scene pass, otherwise RT handle
        public Color ClearColor;
    }

    private struct Batch
    {
        public int IndexOffset;
        public int IndexCount;
        public ushort State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlobalsSnapshot
    {
        public Matrix4x4 Projection;
        public float Time;
    }

    private const int MaxBoneRows = 1024;
    private const int BoneTextureWidth = 128;
    private static int _boneRow;
    private static float _time;
    private static Shader? _spriteShader;
    private static Shader? _spriteSdfShader;

    public static event Action? AfterEndFrame;

    private static State[] _stateStack = null!;
    private static int _stateStackDepth = 0;
    private static bool _batchStateDirty = true;
    
    public static ApplicationConfig Config { get; private set; } = null!;
    public static GraphicsConfig RenderConfig => Config.Graphics!;
    public static IGraphicsDriver Driver { get; private set; } = null!;
    public static Camera? Camera { get; private set; }
    public static Texture? SpriteAtlas { get; set; }
    public static Texture WhiteTexture { get; private set; } = null!;
    public static ref readonly Matrix3x2 Transform => ref CurrentState.Transform;
    public static Color Color => CurrentState.Color.WithAlpha(CurrentState.Color.A * CurrentState.Opacity);
    public static float PixelsPerUnit { get; private set; }
    public static float PixelsPerUnitInv {  get; private set; }
    public static bool IsScissor => CurrentState.ScissorEnabled;

    private static ref State CurrentState => ref _stateStack[_stateStackDepth];
    
    private static RenderMesh _mesh;
    private static nuint _boneTexture;
    private const int BoneTextureSlot = 1;
    private static RenderPass _currentPass;
    private static byte _rtPassIndex;
    private static Matrix4x4[] _passProjections = new Matrix4x4[MaxRenderPasses];
    private static RenderTexture _activeRenderTexture;
    private static int _rtPassCount;
    private static (nuint Handle, Color ClearColor)[] _rtPasses = new (nuint, Color)[MaxRenderPasses];
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
    private static int _globalsBaseIndex; // Base offset for globals buffers to prevent RTT overwriting main frame
    private static ushort _currentBatchState;
    
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

        _vertices = new NativeArray<MeshVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _sortedIndices = new NativeArray<ushort>(MaxIndices);
        _commands = new NativeArray<DrawCommand>(_maxDrawCommands);
        _batches = new NativeArray<Batch>(_maxBatches);
        _batchStates = new NativeArray<BatchState>(_maxBatches);
        _globalsSnapshots = new NativeArray<GlobalsSnapshot>(64);

        _mesh = CreateMesh<MeshVertex>(
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
            [],
            TextureFormat.RGBA32F,
            TextureFilter.Point,
            name: "Bones");

        ResetState();
    }

    public static void Shutdown()
    {
        _batches.Dispose();
        _vertices.Dispose();
        _commands.Dispose();
        _indices.Dispose();
        _globalsSnapshots.Dispose();

        Driver.DestroyMesh(_mesh.Handle);
        Driver.DestroyTexture(_boneTexture);
        WhiteTexture?.Dispose();

        Driver.Shutdown();

        _mesh = default;
        _boneTexture = 0;
    }

    internal static bool BeginFrame()
    {
        ResetState();

        if (WhiteTexture == null)
            WhiteTexture = Texture.Create(1, 1, [255, 255, 255, 255], name: "White");

        if (!Driver.BeginFrame())
            return false;

        RenderTexturePool.FlushPendingReleases();

        _time += Time.DeltaTime;

        return true;
    }

    public static void BeginPass(RenderTexture rt, Color? clearColor = null)
    {
        if (!rt.IsValid)
            throw new InvalidOperationException("Cannot begin pass with invalid render texture");

        if (_activeRenderTexture.IsValid)
            throw new InvalidOperationException("Cannot nest render texture passes - call EndPass first");

        PushState();

        CurrentState.ClearColor = clearColor ?? Color.Transparent;
        _currentPass = RenderPass.RenderTexture;
        _rtPassIndex++;
        _activeRenderTexture = rt;
        _rtPasses[_rtPassCount++] = (rt.Handle, CurrentState.ClearColor);
        SetViewport(0, 0, rt.Width, rt.Height);
        ClearScissor();
        _batchStateDirty = true;
    }

    public static void EndPass()
    {
        if (!_activeRenderTexture.IsValid)
            throw new InvalidOperationException("No render texture pass is active - call BeginPass first");

        _currentPass = RenderPass.Scene;
        _activeRenderTexture = default;

        PopState();
        _batchStateDirty = true;
    }

    internal static void EndFrame()
    {
        ExecuteCommands();

        AfterEndFrame?.Invoke();
        AfterEndFrame = null;

        Driver.EndFrame();
    }

    internal static void ResolveAssets()
    {
        _spriteShader = Asset.Get<Shader>(AssetType.Shader, RenderConfig.SpriteShader)
            ?? throw new ArgumentNullException(nameof(RenderConfig.SpriteShader), "Sprite shader not found");
        _spriteSdfShader = Asset.Get<Shader>(AssetType.Shader, "sprite_sdf");
    }

    private static void UploadBones()
    {
        Driver.UpdateTextureRegion(
            _boneTexture,
            new RectInt(0,0,BoneTextureWidth,_boneRow),
            _boneData.AsByteSpan(),
            BoneTextureWidth);
    }

    private static void UploadGlobals()
    {
        var count = _globalsSnapshots.Length;
        if (count == 0)
            return;

        // Ensure driver has enough buffers for base + count
        Driver.SetGlobalsCount(_globalsBaseIndex + count);

        var data = stackalloc byte[80];
        for (int i = 0; i < count; i++)
        {
            ref var snapshot = ref _globalsSnapshots[i];
            var transposed = Matrix4x4.Transpose(snapshot.Projection);
            Buffer.MemoryCopy(&transposed, data, 64, 64);
            *(float*)(data + 64) = snapshot.Time;
            Driver.SetGlobals(_globalsBaseIndex + i, new ReadOnlySpan<byte>(data, 80));
        }
    }

    private static long MakeSortKey(ushort order)
    {
        byte passSortValue = _currentPass == RenderPass.RenderTexture ? _rtPassIndex : (byte)0x7;
        return
            (((long)passSortValue) << PassShift) |
            (((long)(CurrentState.SortLayer & 0xFFF)) << LayerShift) |
            (((long)CurrentState.SortGroup) << GroupShift) |
            (((long)order) << OrderShift) |
            (((long)(_commands.Length & 0xFFFF)) << IndexShift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetOrAddGlobals(in Matrix4x4 projection)
    {
        // Only compare projection - time is same for all batches in a frame
        for (int i = 0; i < _globalsSnapshots.Length; i++)
            if (_globalsSnapshots[i].Projection == projection)
                return (ushort)(_globalsBaseIndex + i);

        var index = (ushort)(_globalsBaseIndex + _globalsSnapshots.Length);
        _globalsSnapshots.Add() = new GlobalsSnapshot { Projection = projection, Time = _time };
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBatchState()
    {
        _batchStateDirty = false;
        ValidateBatchState();

        var currentProjection = _passProjections[(int)_currentPass];

        var candidate = new BatchState
        {
            Pass = (byte)_currentPass,
            GlobalsIndex = GetOrAddGlobals(currentProjection),
            Shader = CurrentState.Shader?.Handle ?? nuint.Zero,
            BlendMode = CurrentState.BlendMode,
            Viewport = CurrentState.Viewport,
            ScissorEnabled = CurrentState.ScissorEnabled,
            Scissor = CurrentState.Scissor,
            Mesh = CurrentState.Mesh.Handle,
            RenderTextureHandle = _activeRenderTexture.Handle,
            ClearColor = CurrentState.ClearColor
        };

        for (int t = 0; t < MaxTextures; t++)
        {
            candidate.Textures[t] = CurrentState.Textures[t];
            candidate.TextureFilters[t] = CurrentState.TextureFilters[t];
        }

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
            $"AddBatchState: " +
            $" {_batchStates.Length - 1}" +
            $" Pass={candidate.Pass}" +
            $" Globals={candidate.GlobalsIndex}" +
            $" Shader=0x{candidate.Shader:X}" +
            $" ({Asset.Get<Shader>(AssetType.Shader, candidate.Shader)?.Name ?? "???"})" +
            $" Texture0=0x{candidate.Textures[0]:X}" +
            $" Texture1=0x{candidate.Textures[1]:X}" +
            $" BlendMode={candidate.BlendMode}" +
            $" Viewport={candidate.Viewport}" +
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
        int atlasIndex = 0,
        int bone = -1)
    {
        Span<MeshVertex> verts =
        [
            new MeshVertex { Position = p0, UV = uv0, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p1, UV = uv1, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p2, UV = uv2, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p3, UV = uv3, Normal = Vector2.Zero, Atlas = atlasIndex, FrameCount = 1, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        AddTriangles(verts, indices, order: order, bone: bone);
    }

    private static void AddTriangles(
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        ushort order,
        int bone)
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

        var sortKey = MakeSortKey(order);
        ref var cmd = ref _commands.Add();
        cmd.SortKey = sortKey;
        cmd.IndexOffset = _indices.Length;
        cmd.IndexCount = indices.Length;
        cmd.BatchState = _currentBatchState;

        LogGraphics($"AddQuad: BatchState={_currentBatchState} SortKey={cmd.SortKey} Count={cmd.IndexCount} Offset={cmd.IndexOffset} Order={order}");

        var baseVertex = _vertices.Length;
        var color = Color;

        if (bone == -1)
        {
            foreach (var v in vertices)
            {
                _vertices.Add(v with
                {
                    Position = Vector2.Transform(v.Position, CurrentState.Transform),
                    Color = v.Color * color,
                    Bone = 0
                });
            }
        }
        else
        {
            bone += CurrentState.BoneIndex;
            foreach (var v in vertices)
            {
                _vertices.Add(v with
                {
                    Color = v.Color * color,
                    Bone = bone
                });
            }
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

    public static void DrawElements(int indexCount, int indexOffset = 0, ushort order=0)
    {
        if (_batchStateDirty)
            AddBatchState();

        var sortKey = MakeSortKey(order);
        
        if (_commands.Length > 0)
        {
            ref var lastCommand = ref _commands[^1];

            if (lastCommand.BatchState == _currentBatchState &&
                (lastCommand.SortKey & SortKeyMergeMask) == (sortKey & SortKeyMergeMask) &&
                lastCommand.IndexOffset + lastCommand.IndexCount == indexOffset)
            {
                lastCommand.IndexCount += (ushort)indexCount;
                LogGraphicsVerbose($"DrawElements (MERGE): BatchState={lastCommand.BatchState} Count={lastCommand.IndexCount} Offset={indexOffset} Order={order}");
                return;
            }
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
        firstBatch.IndexOffset = firstState.Mesh != _mesh.Handle ? _commands[0].IndexOffset : 0;
        firstBatch.IndexCount = _commands[0].IndexCount;
        firstBatch.State = _commands[0].BatchState;

        if (firstState.Mesh == _mesh.Handle)
            _sortedIndices.AddRange(
                _indices.AsReadonlySpan(_commands[0].IndexOffset, _commands[0].IndexCount)
            );

        for (int commandIndex = 1, commandCount = _commands.Length; commandIndex < commandCount; commandIndex++)
        {
            ref var cmd = ref _commands[commandIndex];
            ref var cmdState = ref _batchStates[cmd.BatchState];

            LogGraphicsVerbose($"  Command: Index={commandIndex}  SortKey={cmd.SortKey}  IndexOffset={cmd.IndexOffset} IndexCount={cmd.IndexCount} State={cmd.BatchState}");

            // External mesh: merge if same state and contiguous indices, otherwise new batch.
            if (cmdState.Mesh != _mesh.Handle)
            {
                ref var prevBatch = ref _batches[^1];
                if (cmd.BatchState == prevBatch.State &&
                    cmd.IndexOffset == prevBatch.IndexOffset + prevBatch.IndexCount)
                {
                    prevBatch.IndexCount += cmd.IndexCount;
                }
                else
                {
                    ref var newBatch = ref _batches.Add();
                    newBatch.IndexOffset = cmd.IndexOffset;
                    newBatch.IndexCount = cmd.IndexCount;
                    newBatch.State = cmd.BatchState;
                }
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
        // If no commands, just clear the screen and return early
        if (_commands.Length == 0)
        {
            Driver.BeginScenePass(ClearColor);
            Driver.EndScenePass();
            return;
        }

        TextRender.Flush();
        UI.Flush();

        _commands.AsSpan().Sort();

        CreateBatches();

        LogGraphics(
            $"ExecuteCommands: BatchStates={_batchStates.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");

        if (_vertices.Length > 0 || _indices.Length > 0)
        {
            // Pad indices to 4-byte alignment for WebGPU
            if ((_sortedIndices.Length & 1) != 0)
                _sortedIndices.Add(0);

            Driver.BindMesh(_mesh.Handle);
            Driver.UpdateMesh(_mesh.Handle, _vertices.AsByteSpan(), _sortedIndices.AsSpan());
        }

        UploadBones();
        Driver.BindTexture(_boneTexture, BoneTextureSlot);

        // Upload all globals snapshots to driver
        UploadGlobals();

        LogGraphics($"ExecuteBatches: Batches={_batches.Length} BatchStates={_batchStates.Length} Commands={_commands.Length} Vertices={_vertices.Length} Indices={_indices.Length}");

        // Track current render target for pass switching (0 = scene pass, non-zero = RT pass)
        nuint currentRT = nuint.MaxValue;  // Invalid value to force first pass begin
        bool scenePassStarted = false;
        Span<bool> rtVisited = stackalloc bool[_rtPassCount];

        for (int batchIndex = 0, batchCount = _batches.Length; batchIndex < batchCount; batchIndex++)
        {
            ref var batch = ref _batches[batchIndex];
            ref var batchState = ref _batchStates[batch.State];

            // Handle pass switching based on render target
            if (currentRT != batchState.RenderTextureHandle)
            {
                // End previous pass (if any)
                if (currentRT == 0)
                {
                    Driver.EndScenePass();
                    LogGraphics($"  EndPass: Scene");
                }
                else if (currentRT != nuint.MaxValue)
                {
                    Driver.EndRenderTexturePass();
                    LogGraphics($"  EndPass: RT 0x{currentRT:X}");
                }

                currentRT = batchState.RenderTextureHandle;

                // Begin new pass
                if (currentRT == 0)
                {
                    // Scene pass - clear if first time, resume if returning after RT
                    if (!scenePassStarted)
                    {
                        Driver.BeginScenePass(ClearColor);
                        scenePassStarted = true;
                        LogGraphics($"  BeginPass: Scene (clear)");
                    }
                    else
                    {
                        Driver.ResumeScenePass();
                        LogGraphics($"  BeginPass: Scene (load)");
                    }
                }
                else
                {
                    // RT pass - get clear color from batch state
                    Driver.BeginRenderTexturePass(currentRT, batchState.ClearColor);
                    LogGraphics($"  BeginPass: RT 0x{currentRT:X}");

                    // Mark this RT as visited
                    for (int r = 0; r < _rtPassCount; r++)
                    {
                        if (_rtPasses[r].Handle == currentRT)
                        {
                            rtVisited[r] = true;
                            break;
                        }
                    }
                }

                // Re-bind bone texture — driver state was reset by BeginPass
                Driver.BindTexture(_boneTexture, BoneTextureSlot);
            }

            LogGraphics($"  Batch: Mesh={batchState.Mesh:X}  Shader={Asset.Get<Shader>(AssetType.Shader, batchState.Shader)!.Name}  Index={batchIndex} IndexOffset={batch.IndexOffset} IndexCount={batch.IndexCount} State={batch.State} Pass={batchState.Pass} RT=0x{batchState.RenderTextureHandle:X}");

            // Apply all state unconditionally — driver early-exits handle optimization
            Driver.SetViewport(batchState.Viewport);
            if (batchState.ScissorEnabled)
                Driver.SetScissor(batchState.Scissor);
            else
                Driver.ClearScissor();
            Driver.BindShader(batchState.Shader);
            Driver.BindGlobals(batchState.GlobalsIndex);
            for (int t = 0; t < MaxTextures; t++)
            {
                if (batchState.Textures[t] != 0)
                    Driver.BindTexture((nuint)batchState.Textures[t], t, (TextureFilter)batchState.TextureFilters[t]);
            }
            Driver.SetBlendMode(batchState.BlendMode);
            Driver.BindMesh(batchState.Mesh);

            Driver.DrawElements(batch.IndexOffset, batch.IndexCount, 0);
        }

        // Clear scissor before ending the final pass
        Driver.ClearScissor();

        // End the final pass
        if (currentRT == 0)
        {
            Driver.EndScenePass();
            LogGraphics($"  EndPass: Scene");
        }
        else if (currentRT != nuint.MaxValue)
        {
            Driver.EndRenderTexturePass();
            LogGraphics($"  EndPass: RT 0x{currentRT:X}");
        }

        // Clear any RT passes that had no draw commands (e.g. empty workspace with grid hidden)
        for (int r = 0; r < _rtPassCount; r++)
        {
            if (!rtVisited[r])
            {
                Driver.BeginRenderTexturePass(_rtPasses[r].Handle, _rtPasses[r].ClearColor);
                Driver.EndRenderTexturePass();
                LogGraphics($"  ClearOnly RT 0x{_rtPasses[r].Handle:X}");
            }
        }

        LogGraphics($"Clearing buffers");
        _commands.Clear();
        _vertices.Clear();
        _indices.Clear();
        _batches.Clear();
        _batchStates.Clear();

        // Advance base index so subsequent ExecuteCommands calls (like RTT) use different buffer slots
        // This prevents RTT from overwriting globals that main frame draw commands still reference
        LogGraphics($"Clearing Snapshots:  baseIndex={_globalsBaseIndex}");
        _globalsBaseIndex += _globalsSnapshots.Length;
        _globalsSnapshots.Clear();

        LogGraphics($"ExecuteCommands Done");
        _batchStateDirty = true;
        _currentBatchState = 0;
    }

    [Conditional("DEBUG")]
    private static void ValidateBatchState()
    {
        var shader = CurrentState.Shader;
        if (shader == null) return;

        foreach (var binding in shader.Bindings)
        {
            switch (binding.Type)
            {
                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                {
                    int slot = FindTextureSlotForBinding(binding, shader);

                    // Bone texture slot is bound globally in ExecuteCommands, not per draw call
                    if (slot == BoneTextureSlot)
                        break;

                    var textureHandle = slot >= 0 ? (nuint)CurrentState.Textures[slot] : 0;
                    Debug.Assert(textureHandle != 0,
                        $"Shader '{shader.Name}' binding {binding.Binding} ('{binding.Name}') expects a texture but none is bound");

                    if (textureHandle != 0)
                    {
                        var texture = Asset.Get<Texture>(AssetType.Texture, textureHandle);
                        if (texture != null)
                        {
                            if (binding.Type == ShaderBindingType.Texture2DArray)
                                Debug.Assert(texture.IsArray,
                                    $"Shader '{shader.Name}' binding {binding.Binding} ('{binding.Name}') expects texture_2d_array but got texture_2d");
                            else
                                Debug.Assert(!texture.IsArray,
                                    $"Shader '{shader.Name}' binding {binding.Binding} ('{binding.Name}') expects texture_2d but got texture_2d_array");
                        }
                    }
                    break;
                }
            }
        }

        if (CurrentState.Mesh.Handle != 0 && shader.VertexFormatHash != 0)
        {
            var meshHash = CurrentState.Mesh.VertexHash;
            Debug.Assert(meshHash == 0 || meshHash == shader.VertexFormatHash,
                $"Vertex format mismatch: shader '{shader.Name}' (hash 0x{shader.VertexFormatHash:X8}) " +
                $"is incompatible with bound mesh (hash 0x{meshHash:X8})");
        }
    }

    private static int FindTextureSlotForBinding(ShaderBinding target, Shader shader)
    {
        int slotIndex = 0;
        foreach (var binding in shader.Bindings)
        {
            if (binding.Type is ShaderBindingType.Texture2D
                or ShaderBindingType.Texture2DArray
                or ShaderBindingType.Texture2DUnfilterable)
            {
                if (binding.Binding == target.Binding)
                    return slotIndex;
                slotIndex++;
            }
        }
        return -1;
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
