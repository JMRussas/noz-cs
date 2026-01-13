//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using noz.Platform;

namespace noz;

public class MeshBatcher
{
    // Configuration
    private const int VerticesPerSegment = 65536;   // ~1.5MB per segment at 24 bytes/vertex
    private const int IndicesPerSegment = 196608;   // 3x vertices (worst case: all triangles)
    private const int BufferSegments = 3;           // Triple buffering

    private readonly int _maxDrawCommands;
    private readonly int _maxBatches;

    // Ring buffer state
    private readonly MeshVertex[] _vertices;
    private readonly ushort[] _indices;
    private int _vertexWriteOffset;
    private int _indexWriteOffset;
    private int _currentSegment;

    // Per-segment tracking for sync
    private readonly int[] _segmentVertexStart;
    private readonly int[] _segmentIndexStart;
    private readonly FenceHandle[] _segmentFences;

    // Draw commands (unsorted)
    private readonly DrawCommand[] _commands;
    private int _commandCount;

    // Sorted command indices
    private readonly int[] _sortedIndices;

    // Output batches
    private readonly RenderBatch[] _batches;
    private int _batchCount;

    // Current state
    private ushort _currentSortGroup;
    private ushort _sortGroupBaseDepth;

    // Platform backend reference
    private IRender _backend = null!;

    // GPU resources
    private BufferHandle _vertexBuffer;
    private BufferHandle _indexBuffer;
    private RenderStats _stats;

    // Stats
    public ref readonly RenderStats Stats => ref _stats;

    public MeshBatcher(RenderConfig config)
    {
        _maxDrawCommands = config.MaxDrawCommands;
        _maxBatches = config.MaxBatches;

        _vertices = new MeshVertex[VerticesPerSegment * BufferSegments];
        _indices = new ushort[IndicesPerSegment * BufferSegments];
        _commands = new DrawCommand[_maxDrawCommands];
        _sortedIndices = new int[_maxDrawCommands];
        _batches = new RenderBatch[_maxBatches];

        _segmentVertexStart = new int[BufferSegments];
        _segmentIndexStart = new int[BufferSegments];
        _segmentFences = new FenceHandle[BufferSegments];

        for (var i = 0; i < BufferSegments; i++)
        {
            _segmentVertexStart[i] = i * VerticesPerSegment;
            _segmentIndexStart[i] = i * IndicesPerSegment;
        }
    }

    public void Init(IRender backend)
    {
        _backend = backend;

        // Create GPU buffers
        _vertexBuffer = _backend.CreateVertexBuffer(
            _vertices.Length * MeshVertex.SizeInBytes,
            BufferUsage.Dynamic
        );

        _indexBuffer = _backend.CreateIndexBuffer(
            _indices.Length * sizeof(ushort),
            BufferUsage.Dynamic
        );
    }

    public void Shutdown()
    {
        for (int i = 0; i < BufferSegments; i++)
        {
            if (_segmentFences[i].IsValid)
                _backend.DeleteFence(_segmentFences[i]);
        }

        _backend.DestroyBuffer(_vertexBuffer);
        _backend.DestroyBuffer(_indexBuffer);
    }

    public void BeginBatch()
    {
        // Move to next segment
        _currentSegment = (_currentSegment + 1) % BufferSegments;

        // Wait for this segment's previous usage to complete
        if (_segmentFences[_currentSegment].IsValid)
        {
            _backend.WaitFence(_segmentFences[_currentSegment]);
            _backend.DeleteFence(_segmentFences[_currentSegment]);
            _segmentFences[_currentSegment] = FenceHandle.Invalid;
        }

        // Reset write positions to segment start
        _vertexWriteOffset = _segmentVertexStart[_currentSegment];
        _indexWriteOffset = _segmentIndexStart[_currentSegment];

        _commandCount = 0;
        _batchCount = 0;
        _currentSortGroup = 0;
        _sortGroupBaseDepth = 0;
    }

    public void BeginSortGroup(ushort groupDepth)
    {
        _currentSortGroup++;
        _sortGroupBaseDepth = groupDepth;
    }

    public void EndSortGroup()
    {
        // Group ID stays incremented; next submissions use new group
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SubmitQuad(
        float x, float y, float width, float height,
        float u0, float v0, float u1, float v1,
        in Matrix3x2 transform,
        TextureHandle texture,
        BlendMode blend,
        byte layer,
        ushort depth,
        Color32 tint,
        int atlas = 0)
    {
        if (_commandCount >= _maxDrawCommands)
            return;

        int segmentVertexEnd = _segmentVertexStart[_currentSegment] + VerticesPerSegment;
        int segmentIndexEnd = _segmentIndexStart[_currentSegment] + IndicesPerSegment;

        if (_vertexWriteOffset + 4 > segmentVertexEnd ||
            _indexWriteOffset + 6 > segmentIndexEnd)
            return;

        // Record command
        ref var cmd = ref _commands[_commandCount++];
        cmd.Key = new SortKey(
            _currentSortGroup,
            layer,
            ShaderHandle.Sprite.Id,
            (byte)blend,
            texture.Id,
            (ushort)(_sortGroupBaseDepth + depth)
        );
        cmd.VertexOffset = _vertexWriteOffset;
        cmd.VertexCount = 4;
        cmd.IndexOffset = _indexWriteOffset;
        cmd.IndexCount = 6;
        cmd.Texture = texture;
        cmd.Shader = ShaderHandle.Sprite;

        // Transform and write vertices
        var p0 = Vector2.Transform(new Vector2(x, y), transform);
        var p1 = Vector2.Transform(new Vector2(x + width, y), transform);
        var p2 = Vector2.Transform(new Vector2(x + width, y + height), transform);
        var p3 = Vector2.Transform(new Vector2(x, y + height), transform);

        // Convert Color32 to Vector4 color
        var color = new Vector4(tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f);
        float depthF = depth / 4095f; // Normalize depth to 0-1 range

        _vertices[_vertexWriteOffset + 0] = new MeshVertex { Position = p0, UV = new Vector2(u0, v0), Normal = Vector2.Zero, Color = color, Opacity = 1.0f, Depth = depthF, Bone = 0, Atlas = atlas };
        _vertices[_vertexWriteOffset + 1] = new MeshVertex { Position = p1, UV = new Vector2(u1, v0), Normal = Vector2.Zero, Color = color, Opacity = 1.0f, Depth = depthF, Bone = 0, Atlas = atlas };
        _vertices[_vertexWriteOffset + 2] = new MeshVertex { Position = p2, UV = new Vector2(u1, v1), Normal = Vector2.Zero, Color = color, Opacity = 1.0f, Depth = depthF, Bone = 0, Atlas = atlas };
        _vertices[_vertexWriteOffset + 3] = new MeshVertex { Position = p3, UV = new Vector2(u0, v1), Normal = Vector2.Zero, Color = color, Opacity = 1.0f, Depth = depthF, Bone = 0, Atlas = atlas };

        // Write indices relative to segment start (so they fit in ushort)
        int segmentVertexStart = _segmentVertexStart[_currentSegment];
        ushort relativeVertex = (ushort)(_vertexWriteOffset - segmentVertexStart);
        _indices[_indexWriteOffset + 0] = relativeVertex;
        _indices[_indexWriteOffset + 1] = (ushort)(relativeVertex + 1);
        _indices[_indexWriteOffset + 2] = (ushort)(relativeVertex + 2);
        _indices[_indexWriteOffset + 3] = (ushort)(relativeVertex + 2);
        _indices[_indexWriteOffset + 4] = (ushort)(relativeVertex + 3);
        _indices[_indexWriteOffset + 5] = relativeVertex;

        _vertexWriteOffset += 4;
        _indexWriteOffset += 6;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SubmitMesh(
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        in Matrix3x2 transform,
        TextureHandle texture,
        ShaderHandle shader,
        BlendMode blend,
        byte layer,
        ushort depth,
        Color32 tint)
    {
        if (_commandCount >= _maxDrawCommands)
            return;

        var segmentVertexEnd = _segmentVertexStart[_currentSegment] + VerticesPerSegment;
        var segmentIndexEnd = _segmentIndexStart[_currentSegment] + IndicesPerSegment;

        if (_vertexWriteOffset + vertices.Length > segmentVertexEnd ||
            _indexWriteOffset + indices.Length > segmentIndexEnd)
            return;

        // Record command
        ref var cmd = ref _commands[_commandCount++];
        cmd.Key = new SortKey(
            _currentSortGroup,
            layer,
            shader.Id,
            (byte)blend,
            texture.Id,
            (ushort)(_sortGroupBaseDepth + depth)
        );
        cmd.VertexOffset = _vertexWriteOffset;
        cmd.VertexCount = vertices.Length;
        cmd.IndexOffset = _indexWriteOffset;
        cmd.IndexCount = indices.Length;
        cmd.Texture = texture;
        cmd.Shader = shader;

        // Convert tint to float color for multiplication
        var tintF = new Vector4(tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f);

        // Transform and copy vertices (indices will be relative to segment start)
        var segmentVertexStart = _segmentVertexStart[_currentSegment];
        var relativeBaseVertex = (ushort)(_vertexWriteOffset - segmentVertexStart);
        for (int i = 0; i < vertices.Length; i++)
        {
            ref readonly var src = ref vertices[i];
            ref var dst = ref _vertices[_vertexWriteOffset + i];

            // Transform position
            dst.Position = Vector2.Transform(src.Position, transform);
            dst.UV = src.UV;
            dst.Normal = src.Normal;

            // Apply tint (multiply colors)
            dst.Color = src.Color * tintF;
            dst.Opacity = src.Opacity;
            dst.Depth = src.Depth;
            dst.Bone = src.Bone;
            dst.Atlas = src.Atlas;
        }
        _vertexWriteOffset += vertices.Length;

        // Copy indices (offset by relative base vertex within segment)
        for (var i = 0; i < indices.Length; i++)
            _indices[_indexWriteOffset + i] = (ushort)(indices[i] + relativeBaseVertex);
        _indexWriteOffset += indices.Length;
    }

    public void BuildBatches()
    {
        if (_commandCount == 0)
            return;

        // Initialize sort indices
        for (int i = 0; i < _commandCount; i++)
            _sortedIndices[i] = i;

        // Sort by key
        SortCommands();

        // Index offset relative to segment start (for drawing)
        int segmentIndexStart = _segmentIndexStart[_currentSegment];

        // Build batches by coalescing adjacent commands with same state
        _batchCount = 0;
        ref var firstCmd = ref _commands[_sortedIndices[0]];

        _batches[0] = new RenderBatch
        {
            FirstIndex = firstCmd.IndexOffset - segmentIndexStart,  // Relative to segment
            IndexCount = firstCmd.IndexCount,
            Texture = firstCmd.Texture,
            Shader = firstCmd.Shader,
            Blend = (BlendMode)firstCmd.Key.Blend
        };
        _batchCount = 1;

        for (int i = 1; i < _commandCount; i++)
        {
            ref var prevCmd = ref _commands[_sortedIndices[i - 1]];
            ref var currCmd = ref _commands[_sortedIndices[i]];

            if (prevCmd.Key.CanBatchWith(currCmd.Key))
            {
                // Extend current batch
                _batches[_batchCount - 1].IndexCount += currCmd.IndexCount;
            }
            else
            {
                // Start new batch
                if (_batchCount >= _maxBatches)
                    break;

                _batches[_batchCount++] = new RenderBatch
                {
                    FirstIndex = currCmd.IndexOffset - segmentIndexStart,  // Relative to segment
                    IndexCount = currCmd.IndexCount,
                    Texture = currCmd.Texture,
                    Shader = currCmd.Shader,
                    Blend = (BlendMode)currCmd.Key.Blend
                };
            }
        }
    }

    public void FlushBatches()
    {
        if (_batchCount == 0)
            return;

        // Upload vertex data for current segment
        int vertexStart = _segmentVertexStart[_currentSegment];
        int vertexCount = _vertexWriteOffset - vertexStart;
        if (vertexCount > 0)
        {
            _backend.UpdateVertexBufferRange(
                _vertexBuffer,
                vertexStart * MeshVertex.SizeInBytes,
                _vertices.AsSpan(vertexStart, vertexCount)
            );
        }

        // Upload index data for current segment
        int indexStart = _segmentIndexStart[_currentSegment];
        int indexCount = _indexWriteOffset - indexStart;
        if (indexCount > 0)
        {
            _backend.UpdateIndexBufferRange(
                _indexBuffer,
                indexStart * sizeof(ushort),
                _indices.AsSpan(indexStart, indexCount)
            );
        }

        // Bind buffers
        _backend.BindVertexBuffer(_vertexBuffer);
        _backend.BindIndexBuffer(_indexBuffer);

        // Base vertex offset for this segment (indices are relative to segment start)
        int baseVertex = _segmentVertexStart[_currentSegment];

        // Index offset in the GPU buffer (batch.FirstIndex is relative to segment, need absolute)
        int segmentIndexStart = _segmentIndexStart[_currentSegment];

        // Execute batches
        TextureHandle currentTexture = TextureHandle.Invalid;
        ShaderHandle currentShader = ShaderHandle.Invalid;
        BlendMode currentBlend = (BlendMode)255; // Invalid initial value

        for (int i = 0; i < _batchCount; i++)
        {
            ref var batch = ref _batches[i];

            // State changes
            if (batch.Shader != currentShader)
            {
                _backend.BindShader(batch.Shader);
                currentShader = batch.Shader;
            }

            if (batch.Texture != currentTexture)
            {
                _backend.BindTexture(0, batch.Texture);
                currentTexture = batch.Texture;
            }

            if (batch.Blend != currentBlend)
            {
                _backend.SetBlendMode(batch.Blend);
                currentBlend = batch.Blend;
            }

            _backend.DrawIndexedRange(segmentIndexStart + batch.FirstIndex, batch.IndexCount, baseVertex);
        }

        _segmentFences[_currentSegment] = _backend.CreateFence();

        _stats.DrawCount = _batchCount;
        _stats.VertexCount = _vertexWriteOffset - _segmentVertexStart[_currentSegment];
        _stats.CommandCount = _commandCount;
    }

    private void SortCommands()
    {
        for (var i = 1; i < _commandCount; i++)
        {
            var key = _sortedIndices[i];
            var keyValue = _commands[key].Key;
            var j = i - 1;

            while (j >= 0 && _commands[_sortedIndices[j]].Key > keyValue)
            {
                _sortedIndices[j + 1] = _sortedIndices[j];
                j--;
            }
            _sortedIndices[j + 1] = key;
        }
    }
}
