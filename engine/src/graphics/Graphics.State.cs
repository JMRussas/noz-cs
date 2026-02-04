//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_GRAPHICS_DEBUG
// #define NOZ_GRAPHICS_DEBUG_VERBOSE

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static unsafe partial class Graphics
{
    private struct State
    {
        public Color Color;
        public Shader? Shader;
        public Matrix3x2 Transform;
        public fixed ulong Textures[MaxTextures];
        public fixed byte TextureFilters[MaxTextures];
        public ushort SortLayer;
        public ushort SortGroup;
        public ushort SortIndex;
        public ushort BoneIndex;
        public BlendMode BlendMode;
        public RectInt Viewport;
        public bool ScissorEnabled;
        public RectInt Scissor;
        public nuint Mesh;
    }

    public struct AutoState(bool pop) : IDisposable
    {
        private readonly bool _pop = pop;
        readonly void IDisposable.Dispose() { if (_pop) PopState(); }
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
            texturesChanged = current.Textures[i] != prev.Textures[i] ||
                              current.TextureFilters[i] != prev.TextureFilters[i];
        var viewportChanged = current.Viewport != prev.Viewport;
        var scissorChanged = current.ScissorEnabled != prev.ScissorEnabled ||
                             current.Scissor != prev.Scissor;
        var meshChanged = current.Mesh != prev.Mesh;

        if (shaderChanged || blendChanged || texturesChanged || viewportChanged || scissorChanged || meshChanged)
            _batchStateDirty = true;
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
        CurrentState.BoneIndex = 0;
        for (var i = 0; i < MaxTextures; i++)
        {
            CurrentState.Textures[i] = 0;
            CurrentState.TextureFilters[i] = (byte)TextureFilter.Point;
        }

        CurrentState.ScissorEnabled = false;
        CurrentState.Scissor = RectInt.Zero;
        CurrentState.Viewport = RectInt.Zero;
        CurrentState.Mesh = _mesh;

        _currentPass = RenderPass.Scene;
        _boneRow = 1;
        Camera = null;

        // Reset all pass projections to identity to ensure clean state
        for (var i = 0; i < MaxRenderPasses; i++)
            _passProjections[i] = Matrix4x4.Identity;

        var size = Application.WindowSize;
        SetViewport(0, 0, (int)size.X, (int)size.Y);
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

        _passProjections[(int)_currentPass] = projection;
    }

    public static void SetMesh(nuint vertexArray)
    {
        if (CurrentState.Mesh == vertexArray) return;
        CurrentState.Mesh = vertexArray;
        _batchStateDirty = true;
    }

    public static void SetTransform(in Matrix3x2 transform)
    {
        CurrentState.Transform = transform;
    }

    public static void SetColor(Color color)
    {
        CurrentState.Color = color;
    }

    public static void SetShader(Shader shader)
    {
        if (shader == CurrentState.Shader) return;
        CurrentState.Shader = shader;
        _batchStateDirty = true;
    }

    public static void SetTexture(nuint texture, int slot = 0, TextureFilter filter = TextureFilter.Point)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        var filterByte = (byte)filter;
        if (CurrentState.Textures[slot] == texture && CurrentState.TextureFilters[slot] == filterByte) return;
        CurrentState.Textures[slot] = texture;
        CurrentState.TextureFilters[slot] = filterByte;
        _batchStateDirty = true;
    }

    public static void SetTexture(Texture? texture, int slot = 0, TextureFilter filter = TextureFilter.Point)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        var handle = texture?.Handle ?? nuint.Zero;
        var filterByte = (byte)filter;
        if (CurrentState.Textures[slot] == handle && CurrentState.TextureFilters[slot] == filterByte) return;
        CurrentState.Textures[slot] = handle;
        CurrentState.TextureFilters[slot] = filterByte;
        _batchStateDirty = true;
    }

    public static void SetSortGroup(int group)
    {
        Debug.Assert((group & 0xFFFF) == group);
        CurrentState.SortGroup = (ushort)group;
    }

    public static void SetLayer(ushort layer)
    {
        Debug.Assert((layer & 0xFFF) == layer);
        CurrentState.SortLayer = layer;
    }

    public static void SetTextureFilter(TextureFilter filter, int slot = 0)
    {
        Debug.Assert(slot is >= 0 and < MaxTextures);
        var filterByte = (byte)filter;
        if (CurrentState.TextureFilters[slot] == filterByte) return;
        CurrentState.TextureFilters[slot] = filterByte;
        _batchStateDirty = true;
    }

    public static void SetBlendMode(BlendMode blendMode)
    {
        CurrentState.BlendMode = blendMode;
        _batchStateDirty = true;
    }

    public static void SetBones(Animator animator) =>
        SetBones(animator.Skeleton, animator.BoneTransforms);

    public static void SetBones(Skeleton skeleton, ReadOnlySpan<Matrix3x2> transforms) =>
        SetBones(skeleton.BindPoses.AsReadonlySpan(), transforms);

    public static void SetBones(ReadOnlySpan<Matrix3x2> skeleton, ReadOnlySpan<Matrix3x2> transforms)
    {
        Debug.Assert(skeleton.Length <= Skeleton.MaxBones);
        Debug.Assert(transforms.Length == skeleton.Length);

        // BoneIndex is flat index: row * 64, so vertex bone index + BoneIndex = flat index
        CurrentState.BoneIndex = (ushort)(_boneRow * Skeleton.MaxBones);

        // Write transforms to the current row in _boneData
        // Each bone is 2 texels (8 floats): [M11,M12,M31,0], [M21,M22,M32,0]
        var rowOffset = _boneRow * BoneTextureWidth * 4;
        ref readonly var viewTransform = ref CurrentState.Transform;
        for (var i = 0; i < transforms.Length; i++)
        {
            ref readonly var mm = ref transforms[i];
            var m = skeleton[i] * mm * viewTransform;
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

    public static void ClearScissor()
    {
        if (!CurrentState.ScissorEnabled)
            return;

        CurrentState.ScissorEnabled = false;
        _batchStateDirty = true;
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

    public static void SetUniform(string name, ReadOnlySpan<byte> data)
    {
        Driver.SetUniform(name, data);
        _batchStateDirty = true;
    }

    public static void SetUniform<T>(string name, in T data) where T : unmanaged
    {
        unsafe
        {
            fixed (T* ptr = &data)
            {
                Driver.SetUniform(name, new ReadOnlySpan<byte>(ptr, sizeof(T)));
            }
        }
        _batchStateDirty = true;
    }
}
