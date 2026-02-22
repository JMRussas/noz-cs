//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum ShapeType
{
    Rectangle,
    Circle
}

public class ShapeTool(
    SpriteEditor editor,
    Shape shape,
    Color32 fillColor,
    ShapeType shapeType,
    bool subtract = false) : Tool
{
    private readonly SpriteEditor _editor = editor;
    private readonly Shape _shape = shape;
    private readonly Color32 _fillColor = fillColor;
    private readonly ShapeType _shapeType = shapeType;
    private readonly bool _isSubtract = subtract;

    private Vector2 _startLocal;
    private Vector2 _currentLocal;
    private bool _isDragging;

    public override void Begin()
    {
        Cursor.SetCrosshair();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope))
        {
            Cancel();
            return;
        }

        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (Input.IsCtrlDown(Scope))
            mouseLocal = Grid.SnapToPixelGrid(mouseLocal);

        if (!_isDragging && Input.WasButtonPressed(InputCode.MouseLeft, Scope))
        {
            _startLocal = mouseLocal;
            _currentLocal = mouseLocal;
            _isDragging = true;
            return;
        }

        if (_isDragging)
        {
            _currentLocal = mouseLocal;

            if (Input.IsShiftDown(Scope))
                _currentLocal = ConstrainToUniform(_startLocal, _currentLocal);

            if (Input.WasButtonReleased(InputCode.MouseLeft, Scope))
            {
                Commit();
                return;
            }
        }
    }

    private static Vector2 ConstrainToUniform(Vector2 start, Vector2 current)
    {
        var delta = current - start;
        var size = MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y));
        return new Vector2(
            start.X + MathF.Sign(delta.X) * size,
            start.Y + MathF.Sign(delta.Y) * size);
    }

    public override void Draw()
    {
        if (!_isDragging)
            return;

        var min = Vector2.Min(_startLocal, _currentLocal);
        var max = Vector2.Max(_startLocal, _currentLocal);

        if (max.X - min.X < 0.001f || max.Y - min.Y < 0.001f)
            return;

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_editor.Document.Transform);

            var lineWidth = Gizmos.GetLineWidth();
            Gizmos.SetColor(EditorStyle.Tool.LineColor);

            if (_shapeType == ShapeType.Rectangle)
                DrawRectanglePreview(min, max, lineWidth);
            else
                DrawCirclePreview(min, max, lineWidth);

            DrawDimensionText(min, max);
        }
    }

    private void DrawDimensionText(Vector2 min, Vector2 max)
    {
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var widthPx = (int)MathF.Round((max.X - min.X) * dpi);
        var heightPx = (int)MathF.Round((max.Y - min.Y) * dpi);
        var text = $"{widthPx} x {heightPx}";

        var font = EditorAssets.Fonts.Seguisb;
        var fontSize = Gizmos.ZoomRefScale * 0.15f;
        var textSize = TextRender.Measure(text, font, fontSize);

        var centerX = (min.X + max.X) * 0.5f;
        var textX = centerX - textSize.X * 0.5f;
        var textY = max.Y + Gizmos.ZoomRefScale * 0.1f;

        Graphics.SetColor(EditorStyle.Tool.LineColor);
        Graphics.SetTransform(Matrix3x2.CreateTranslation(textX, textY) * _editor.Document.Transform);
        TextRender.Draw(text, font, fontSize);
    }

    private static void DrawRectanglePreview(Vector2 min, Vector2 max, float lineWidth)
    {
        var topLeft = new Vector2(min.X, min.Y);
        var topRight = new Vector2(max.X, min.Y);
        var bottomRight = new Vector2(max.X, max.Y);
        var bottomLeft = new Vector2(min.X, max.Y);

        Gizmos.DrawLine(topLeft, topRight, lineWidth);
        Gizmos.DrawLine(topRight, bottomRight, lineWidth);
        Gizmos.DrawLine(bottomRight, bottomLeft, lineWidth);
        Gizmos.DrawLine(bottomLeft, topLeft, lineWidth);
    }

    private static void DrawCirclePreview(Vector2 min, Vector2 max, float lineWidth)
    {
        var center = (min + max) * 0.5f;
        var radiusX = (max.X - min.X) * 0.5f;
        var radiusY = (max.Y - min.Y) * 0.5f;

        const int segments = 32;
        var prev = center + new Vector2(radiusX, 0);

        for (var i = 1; i <= segments; i++)
        {
            var angle = i * MathF.PI * 2f / segments;
            var point = center + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY);
            Gizmos.DrawLine(prev, point, lineWidth);
            prev = point;
        }
    }

    private void Commit()
    {
        var min = Vector2.Min(_startLocal, _currentLocal);
        var max = Vector2.Max(_startLocal, _currentLocal);

        if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f)
        {
            Finish();
            return;
        }

        Undo.Record(_editor.Document);

        _shape.ClearAnchorSelection();

        var firstAnchor = _shape.AnchorCount;
        var pathIndex = _shape.AddPath(_fillColor, subtract: _isSubtract);
        if (pathIndex == ushort.MaxValue)
        {
            Finish();
            return;
        }

        if (_shapeType == ShapeType.Rectangle)
            AddRectangleAnchors(pathIndex, min, max);
        else
            AddCircleAnchors(pathIndex, min, max);

        for (var i = firstAnchor; i < _shape.AnchorCount; i++)
            _shape.SetAnchorSelected((ushort)i, true);

        _shape.UpdateSamples();
        _shape.UpdateBounds();

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        Finish();
    }

    private void AddRectangleAnchors(ushort pathIndex, Vector2 min, Vector2 max)
    {
        _shape.AddAnchor(pathIndex, new Vector2(min.X, min.Y));
        _shape.AddAnchor(pathIndex, new Vector2(min.X, max.Y));
        _shape.AddAnchor(pathIndex, new Vector2(max.X, max.Y));
        _shape.AddAnchor(pathIndex, new Vector2(max.X, min.Y));
    }

    private void AddCircleAnchors(ushort pathIndex, Vector2 min, Vector2 max)
    {
        var center = (min + max) * 0.5f;
        var rx = (max.X - min.X) * 0.5f;
        var ry = (max.Y - min.Y) * 0.5f;

        // 8 anchor points at 45-degree intervals for much better circle approximation.
        // Quadratic bezier error drops from ~2.7% (90-degree arcs) to ~0.17% (45-degree arcs).
        const float cos45 = 0.7071067812f;
        Span<Vector2> anchors =
        [
            center + new Vector2(rx, 0),                    // 0°
            center + new Vector2(rx * cos45, ry * cos45),   // 45°
            center + new Vector2(0, ry),                    // 90°
            center + new Vector2(-rx * cos45, ry * cos45),  // 135°
            center + new Vector2(-rx, 0),                   // 180°
            center + new Vector2(-rx * cos45, -ry * cos45), // 225°
            center + new Vector2(0, -ry),                   // 270°
            center + new Vector2(rx * cos45, -ry * cos45),  // 315°
        ];

        for (var i = 0; i < 8; i++)
        {
            var p0 = anchors[i];
            var p1 = anchors[(i + 1) % 8];

            // Arc midpoint on the ellipse at the angle halfway between the two anchors
            var midAngle = (i + 0.5f) * MathF.PI * 2f / 8f;
            var arcMid = center + new Vector2(MathF.Cos(midAngle) * rx, MathF.Sin(midAngle) * ry);

            var curve = CalculateSegmentCurve(p0, p1, arcMid);
            _shape.AddAnchor(pathIndex, p0, curve);
        }
    }

    private static float CalculateSegmentCurve(Vector2 p0, Vector2 p1, Vector2 arcMidpoint)
    {
        // Find the curve value (perpendicular offset) that makes the quadratic bezier
        // pass through the arc midpoint at t=0.5.
        // At t=0.5: P(0.5) = chordMid + 0.5 * perp * curve
        // Solving: curve = 2 * dot(arcMid - chordMid, perp)
        var chordMid = (p0 + p1) * 0.5f;
        var dir = p1 - p0;
        var len = dir.Length();
        if (len < 0.0001f) return 0f;
        var perp = new Vector2(-dir.Y, dir.X) / len;
        return 2f * Vector2.Dot(arcMidpoint - chordMid, perp);
    }

    private void Finish()
    {
        _isDragging = false;
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    public override void Cancel()
    {
        Finish();
    }
}
