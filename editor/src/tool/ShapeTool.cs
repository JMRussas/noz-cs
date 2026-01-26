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

public class ShapeTool : Tool
{
    private readonly SpriteEditor _editor;
    private readonly Shape _shape;
    private readonly byte _fillColor;
    private readonly ShapeType _shapeType;

    private Vector2 _startLocal;
    private Vector2 _currentLocal;
    private bool _isDragging;
    private bool _isSubtract;
    private float _opacity;

    public ShapeTool(
        SpriteEditor editor,
        Shape shape,
        byte fillColor,
        ShapeType shapeType,
        float opacity = 1.0f,
        bool subtract = false)
    {
        _editor = editor;
        _shape = shape;
        _fillColor = fillColor;
        _shapeType = shapeType;
        _opacity = opacity;
        _isSubtract = subtract;
    }

    public override void Begin()
    {
        Cursor.SetCrosshair();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Cancel();
            return;
        }

        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (Input.IsCtrlDown())
            mouseLocal = Grid.SnapToGrid(mouseLocal);
        else
            mouseLocal = Grid.SnapToPixelGrid(mouseLocal);

        if (!_isDragging && Input.WasButtonPressed(InputCode.MouseLeft))
        {
            _startLocal = mouseLocal;
            _currentLocal = mouseLocal;
            _isDragging = true;
            return;
        }

        if (_isDragging)
        {
            _currentLocal = mouseLocal;

            if (Input.IsShiftDown())
                _currentLocal = ConstrainToUniform(_startLocal, _currentLocal);

            if (Input.WasButtonReleased(InputCode.MouseLeft))
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
        }
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
        var pathIndex = _shape.AddPath(_fillColor, opacity: _opacity, subract: _isSubtract);
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
        var radiusX = (max.X - min.X) * 0.5f;
        var radiusY = (max.Y - min.Y) * 0.5f;

        var right = center + new Vector2(radiusX, 0);
        var bottom = center + new Vector2(0, radiusY);
        var left = center + new Vector2(-radiusX, 0);
        var top = center + new Vector2(0, -radiusY);

        var curveX = CalculateCircleCurve(radiusY);
        var curveY = CalculateCircleCurve(radiusX);

        _shape.AddAnchor(pathIndex, right, curveX);
        _shape.AddAnchor(pathIndex, bottom, curveY);
        _shape.AddAnchor(pathIndex, left, curveX);
        _shape.AddAnchor(pathIndex, top, curveY);
    }

    private static float CalculateCircleCurve(float radius)
    {
        // For a quadratic bezier circle approximation with 4 points,
        // we need the bezier to pass through the arc midpoint.
        // The curve value is the perpendicular offset from the chord midpoint.
        // For a 90-degree arc, curve ≈ 0.586 * radius (negative for CCW winding)
        return -0.586f * radius;
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
