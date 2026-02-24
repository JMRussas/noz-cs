//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh-based SDF preview: tessellates vector shapes into flat-colored
//  triangle meshes for real-time editing feedback (no MSDF generation).
//

using System.Runtime.InteropServices;
using Clipper2Lib;
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

            // Internal stroke: the fill tessellation below draws the full shape with stroke color.
            // We then overlay a contracted fill on top. The stroke band is the ring between them.
            // For non-stroked slots, fillColor is used directly.
            // When fill is transparent (A==0), we only draw the stroke ring (no fill overlay).
            var hasStroke = slot.HasStroke;
            var fillColor = shape.GetPath(slot.PathIndices[0]).FillColor;
            var hasFill = fillColor.A > 0;

            if (hasStroke)
            {
                var strokeColor = slot.StrokeColor.ToColor();
                var halfStroke = slot.StrokeWidth * Shape.StrokeScale;
                var originalPaths = ShapeClipper.ShapeToPaths(msdfShape, 8);
                PathsD? contractedPaths = null;
                if (originalPaths.Count > 0)
                {
                    // Contract inward by half-stroke width (precision 6 matches ShapeClipper)
                    contractedPaths = Clipper.InflatePaths(originalPaths, -halfStroke,
                        JoinType.Round, EndType.Polygon, precision: 6);
                }

                if (hasFill)
                {
                    // Tessellate full shape as stroke background, then overlay contracted fill
                    if (!TessellatePaths(msdfShape, ref vertexOffset, ref indexOffset, strokeColor))
                        continue;
                    if (contractedPaths is { Count: > 0 })
                        TessellateClipper(contractedPaths, ref vertexOffset, ref indexOffset, fillColor.ToColor());
                }
                else
                {
                    // Stroke only — tessellate the ring (full shape minus contracted interior)
                    if (contractedPaths is { Count: > 0 })
                    {
                        var strokeRing = Clipper.BooleanOp(ClipType.Difference,
                            originalPaths, contractedPaths, FillRule.NonZero, precision: 6);
                        if (strokeRing.Count > 0)
                            TessellateClipper(strokeRing, ref vertexOffset, ref indexOffset, strokeColor);
                    }
                    else
                    {
                        // Contracted paths collapsed — stroke fills the entire shape
                        TessellatePaths(msdfShape, ref vertexOffset, ref indexOffset, strokeColor);
                    }
                }
            }
            else if (hasFill)
            {
                TessellatePaths(msdfShape, ref vertexOffset, ref indexOffset, fillColor.ToColor());
            }
        }
    }

    /// Tessellate an MSDF shape (contours with edges) into the shared mesh buffers.
    private bool TessellatePaths(Msdf.Shape msdfShape, ref int vertexOffset, ref int indexOffset, Color color)
    {
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
        return EmitTessellation(tess, ref vertexOffset, ref indexOffset, color);
    }

    /// Tessellate Clipper2 paths into the shared mesh buffers.
    private bool TessellateClipper(PathsD paths, ref int vertexOffset, ref int indexOffset, Color color)
    {
        var tess = new Tess();
        foreach (var path in paths)
        {
            if (path.Count < 3) continue;
            var verts = new ContourVertex[path.Count];
            for (int j = 0; j < path.Count; j++)
                verts[j].Position = new Vec3((float)path[j].x, (float)path[j].y, 0);
            tess.AddContour(verts);
        }

        tess.Tessellate(WindingRule.NonZero, LibTessDotNet.ElementType.Polygons, 3);
        return EmitTessellation(tess, ref vertexOffset, ref indexOffset, color);
    }

    /// Copy tessellation results into shared vertex/index buffers and add a MeshSlotData.
    private bool EmitTessellation(Tess tess, ref int vertexOffset, ref int indexOffset, Color color)
    {
        if (tess.ElementCount == 0) return false;

        var vertCount = tess.VertexCount;
        var idxCount = tess.ElementCount * 3;

        if (vertexOffset + vertCount > MaxMeshVertices ||
            indexOffset + idxCount > MaxMeshIndices)
            return false;

        for (int v = 0; v < vertCount; v++)
        {
            ref var tv = ref tess.Vertices[v];
            _meshVertices[vertexOffset + v] = new MeshVertex(
                tv.Position.X, tv.Position.Y, 0, 0, Color.White);
        }

        for (int e = 0; e < tess.ElementCount; e++)
        {
            _meshIndices[indexOffset + e * 3 + 0] = (ushort)tess.Elements[e * 3 + 0];
            _meshIndices[indexOffset + e * 3 + 1] = (ushort)tess.Elements[e * 3 + 1];
            _meshIndices[indexOffset + e * 3 + 2] = (ushort)tess.Elements[e * 3 + 2];
        }

        _meshSlots.Add(new MeshSlotData
        {
            VertexOffset = vertexOffset,
            VertexCount = vertCount,
            IndexOffset = indexOffset,
            IndexCount = idxCount,
            FillColor = color,
        });

        vertexOffset += vertCount;
        indexOffset += idxCount;
        return true;
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
