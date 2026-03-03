//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh-based preview: tessellates vector shapes into flat-colored
//  triangle meshes for real-time editing feedback.
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
        if (slots.Count == 0) return;

        var vertexOffset = 0;
        var indexOffset = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.PathIndices.Count == 0) continue;

            // Collect subtract paths with their indices — each only affects paths below it
            List<(ushort PathIndex, PathsD Contours)>? subtractEntries = null;
            var layers = Document.DocumentLayers;
            foreach (var pi in slot.PathIndices)
            {
                ref readonly var path = ref shape.GetPath(pi);
                // Skip paths on hidden layers
                if (path.DocLayer < layers.Count && !layers[path.DocLayer].Visible)
                    continue;
                if (!path.IsSubtract || path.AnchorCount < 3) continue;

                var subShape = new Msdf.Shape();
                ShapeClipper.AppendContour(subShape, shape, pi);
                var subContours = ShapeClipper.ShapeToPaths(subShape, 8);
                if (subContours.Count > 0)
                {
                    subtractEntries ??= new();
                    subtractEntries.Add((pi, subContours));
                }
            }

            // Tessellate each non-subtract path individually (preserves per-path colors)
            // Track accumulated geometry for clip operations
            PathsD? accumulatedPaths = null;

            foreach (var pi in slot.PathIndices)
            {
                ref readonly var path = ref shape.GetPath(pi);
                // Skip paths on hidden layers
                if (path.DocLayer < layers.Count && !layers[path.DocLayer].Visible)
                    continue;
                if (path.IsSubtract || path.AnchorCount < 3) continue;

                var pathShape = new Msdf.Shape();
                ShapeClipper.AppendContour(pathShape, shape, pi);
                pathShape = ShapeClipper.Union(pathShape);
                var contours = ShapeClipper.ShapeToPaths(pathShape, 8);
                if (contours.Count == 0) continue;

                if (path.IsClip)
                {
                    // Clip: intersect with accumulated geometry below
                    if (accumulatedPaths is not { Count: > 0 }) continue;
                    contours = Clipper.BooleanOp(ClipType.Intersection,
                        contours, accumulatedPaths, FillRule.NonZero, precision: 6);
                    if (contours.Count == 0) continue;
                }
                else
                {
                    // Normal path: add fill area to accumulated geometry for future clips
                    // Use contracted contours (excluding stroke) so clip paths don't cover strokes
                    var accContours = contours;
                    if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                    {
                        var halfStroke = path.StrokeWidth * Shape.StrokeScale;
                        var contracted = Clipper.InflatePaths(contours, -halfStroke,
                            JoinType.Round, EndType.Polygon, precision: 6);
                        if (contracted.Count > 0)
                            accContours = contracted;
                    }

                    if (accumulatedPaths == null)
                        accumulatedPaths = new PathsD(accContours);
                    else
                        accumulatedPaths = Clipper.BooleanOp(ClipType.Union,
                            accumulatedPaths, accContours, FillRule.NonZero, precision: 6);
                }

                // Apply subtract paths that are above this path (higher index = on top)
                if (subtractEntries != null)
                {
                    PathsD? subtractPaths = null;
                    foreach (var (subIdx, subContours) in subtractEntries)
                    {
                        if (subIdx <= pi) continue;
                        subtractPaths ??= new PathsD();
                        subtractPaths.AddRange(subContours);
                    }

                    if (subtractPaths is { Count: > 0 })
                    {
                        contours = Clipper.BooleanOp(ClipType.Difference,
                            contours, subtractPaths, FillRule.NonZero, precision: 6);
                        if (contours.Count == 0) continue;
                    }
                }

                var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
                var fillColor = path.FillColor;
                var hasFill = fillColor.A > 0;

                if (hasStroke)
                {
                    var strokeColor = path.StrokeColor.ToColor();
                    var halfStroke = path.StrokeWidth * Shape.StrokeScale;
                    PathsD? contractedPaths = null;
                    if (contours.Count > 0)
                    {
                        contractedPaths = Clipper.InflatePaths(contours, -halfStroke,
                            JoinType.Round, EndType.Polygon, precision: 6);
                    }

                    if (hasFill)
                    {
                        // Tessellate full shape as stroke background, then overlay contracted fill
                        TessellateClipper(contours, ref vertexOffset, ref indexOffset, strokeColor);
                        if (contractedPaths is { Count: > 0 })
                            TessellateClipper(contractedPaths, ref vertexOffset, ref indexOffset, fillColor.ToColor());
                    }
                    else
                    {
                        // Stroke only — tessellate the ring
                        if (contractedPaths is { Count: > 0 })
                        {
                            var strokeRing = Clipper.BooleanOp(ClipType.Difference,
                                contours, contractedPaths, FillRule.NonZero, precision: 6);
                            if (strokeRing.Count > 0)
                                TessellateClipper(strokeRing, ref vertexOffset, ref indexOffset, strokeColor);
                        }
                        else
                        {
                            TessellateClipper(contours, ref vertexOffset, ref indexOffset, strokeColor);
                        }
                    }
                }
                else if (hasFill)
                {
                    TessellateClipper(contours, ref vertexOffset, ref indexOffset, fillColor.ToColor());
                }
            }
        }
    }

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
