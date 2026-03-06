//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh-based preview: tessellates vector shapes into flat-colored
//  triangle meshes for real-time editing feedback.
//

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
    private int _meshVersion = -1;

    private struct MeshSlotData
    {
        public int VertexOffset;
        public int VertexCount;
        public int IndexOffset;
        public int IndexCount;
        public Color FillColor;
    }

    private readonly List<MeshSlotData> _meshSlots = new();

    private void UpdateMesh()
    {
        if (_meshVersion == Document.Version) return;

        _meshVersion = Document.Version;
        _meshSlots.Clear();

        var slots = Document.GetMeshSlots((ushort)_currentTimeSlot);
        if (slots.Count == 0) return;

        var vertexOffset = 0;
        var indexOffset = 0;
        var layers = Document.Layers;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];

            // Collect all subtract paths across all visible layers in this slot
            List<(int LayerIndex, ushort PathIndex, PathsD Contours)>? subtractEntries = null;
            foreach (var (layerIdx, shape) in slot.LayerShapes)
            {
                if (!layers[layerIdx].Visible) continue;
                for (ushort pi = 0; pi < shape.PathCount; pi++)
                {
                    ref readonly var path = ref shape.GetPath(pi);
                    if (!path.IsSubtract || path.AnchorCount < 3) continue;

                    var subShape = new Msdf.Shape();
                    ShapeClipper.AppendContour(subShape, shape, pi);
                    var subContours = ShapeClipper.ShapeToPaths(subShape, 8);
                    if (subContours.Count > 0)
                    {
                        subtractEntries ??= new();
                        subtractEntries.Add((layerIdx, pi, subContours));
                    }
                }
            }

            // Tessellate each layer's paths in order
            // Track accumulated geometry for clip operations across all layers
            PathsD? accumulatedPaths = null;

            foreach (var (layerIdx, shape) in slot.LayerShapes)
            {
                if (!layers[layerIdx].Visible) continue;

                // Generated layers render as a single composite image, not vector mesh
                if (layers[layerIdx].IsGenerated) continue;

                // Snapshot accumulated paths at layer start for clip (cross-layer only)
                var lowerLayerPaths = accumulatedPaths;

                for (ushort pi = 0; pi < shape.PathCount; pi++)
                {
                    ref readonly var path = ref shape.GetPath(pi);
                    if (path.IsSubtract || path.AnchorCount < 3) continue;

                    var pathShape = new Msdf.Shape();
                    ShapeClipper.AppendContour(pathShape, shape, pi);
                    pathShape = ShapeClipper.Union(pathShape);
                    var contours = ShapeClipper.ShapeToPaths(pathShape, 8);
                    if (contours.Count == 0) continue;

                    if (path.IsClip)
                    {
                        if (lowerLayerPaths is not { Count: > 0 }) continue;
                        contours = Clipper.BooleanOp(ClipType.Intersection,
                            contours, lowerLayerPaths, FillRule.NonZero, precision: 6);
                        if (contours.Count == 0) continue;
                    }
                    else
                    {
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

                    // Apply subtract paths from same layer only (higher path index subtracts from lower)
                    if (subtractEntries != null)
                    {
                        PathsD? subtractPaths = null;
                        foreach (var (subLayerIdx, subPi, subContours) in subtractEntries)
                        {
                            if (subLayerIdx != layerIdx) continue;
                            if (subPi <= pi) continue;
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
                            TessellateClipper(contours, ref vertexOffset, ref indexOffset, strokeColor);
                            if (contractedPaths is { Count: > 0 })
                                TessellateClipper(contractedPaths, ref vertexOffset, ref indexOffset, fillColor.ToColor());
                        }
                        else
                        {
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

    private void DrawMesh()
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

    /// <summary>
    /// Renders layer images at SortGroup 3 (same as mesh fill).
    /// Wireframes are still drawn at SortGroup 4 on top of these.
    /// </summary>
    private void DrawGeneratedLayers()
    {
        var texture = Document.Generation.Texture;
        if (texture == null) return;

        var ppu = EditorApplication.Config.PixelsPerUnitInv;
        var bounds = Document.RasterBounds;

        var rect = new Rect(
            bounds.X * ppu,
            bounds.Y * ppu,
            bounds.Width * ppu,
            bounds.Height * ppu);

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha));
            Graphics.Draw(rect);
        }
    }
}
