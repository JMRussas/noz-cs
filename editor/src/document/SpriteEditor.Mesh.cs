//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh-based SDF preview: tessellates vector shapes into flat-colored
//  triangle meshes for real-time editing feedback (no MSDF generation).
//

using System.Runtime.InteropServices;
using LibTessDotNet;
using NoZ.Editor.Msdf;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    // Shared buffers for tessellated mesh data — allocated once, sliced per slot.
    // After Clipper2 flattening, max vertices ≈ MaxAnchors * 16 (curve segments).
    // Tessellation can add a few vertices at intersections but stays bounded.
    private const int MaxMeshVertices = Shape.MaxAnchors * 16;
    private const int MaxMeshIndices = MaxMeshVertices * 3;

    private readonly MeshVertex[] _meshVertices = new MeshVertex[MaxMeshVertices];
    private readonly ushort[] _meshIndices = new ushort[MaxMeshIndices];

    private struct MeshSlotData
    {
        public int VertexOffset;
        public int VertexCount;
        public int IndexOffset;
        public int IndexCount;
        public Color FillColor;
    }

    private readonly List<MeshSlotData> _meshSlots = new();

    private void UpdateMeshSDF(Shape shape)
    {
        _meshSlots.Clear();

        var slots = Document.GetMeshSlots(_currentFrame);
        var slotBounds = Document.GetMeshSlotBounds(_currentFrame);
        if (slots.Count == 0) return;

        var vertexOffset = 0;
        var indexOffset = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.PathIndices.Count == 0) continue;

            // Build the clean shape (Clipper2 union, all-linear contours)
            // Contour points are in anchor-position space (world units).
            var msdfShape = MsdfSprite.BuildShape(
                shape,
                CollectionsMarshal.AsSpan(slot.PathIndices));
            if (msdfShape == null) continue;

            // Tessellate
            var tess = new Tess();

            foreach (var contour in msdfShape.contours)
            {
                if (contour.edges.Count < 3) continue;

                var verts = new ContourVertex[contour.edges.Count];
                for (int e = 0; e < contour.edges.Count; e++)
                {
                    var p = contour.edges[e].Point(0);
                    verts[e].Position = new Vec3((float)p.x, (float)p.y, 0);
                }

                tess.AddContour(verts);
            }

            tess.Tessellate(WindingRule.NonZero, LibTessDotNet.ElementType.Polygons, 3);

            if (tess.ElementCount == 0) continue;

            var vertCount = tess.VertexCount;
            var idxCount = tess.ElementCount * 3;

            // Bounds check — skip slot if it would overflow shared buffers
            if (vertexOffset + vertCount > MaxMeshVertices ||
                indexOffset + idxCount > MaxMeshIndices)
                continue;

            // Copy vertices into shared buffer
            for (int v = 0; v < vertCount; v++)
            {
                ref var tv = ref tess.Vertices[v];
                _meshVertices[vertexOffset + v] = new MeshVertex(
                    tv.Position.X, tv.Position.Y, 0, 0, Color.White);
            }

            // Copy indices into shared buffer
            for (int e = 0; e < tess.ElementCount; e++)
            {
                _meshIndices[indexOffset + e * 3 + 0] = (ushort)tess.Elements[e * 3 + 0];
                _meshIndices[indexOffset + e * 3 + 1] = (ushort)tess.Elements[e * 3 + 1];
                _meshIndices[indexOffset + e * 3 + 2] = (ushort)tess.Elements[e * 3 + 2];
            }

            // Get fill color directly from path
            var firstPath = shape.GetPath(slot.PathIndices[0]);
            var fillColor = firstPath.FillColor.ToColor();

            _meshSlots.Add(new MeshSlotData
            {
                VertexOffset = vertexOffset,
                VertexCount = vertCount,
                IndexOffset = indexOffset,
                IndexCount = idxCount,
                FillColor = fillColor,
            });

            vertexOffset += vertCount;
            indexOffset += idxCount;
        }
    }

    private void DrawMeshSDF()
    {
        if (_meshSlots.Count == 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(Graphics.WhiteTexture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);

            foreach (var slot in _meshSlots)
            {
                Graphics.SetColor(slot.FillColor.WithAlpha(
                    slot.FillColor.A * Workspace.XrayAlpha));
                Graphics.Draw(
                    _meshVertices.AsSpan(slot.VertexOffset, slot.VertexCount),
                    _meshIndices.AsSpan(slot.IndexOffset, slot.IndexCount));
            }
        }
    }
}
