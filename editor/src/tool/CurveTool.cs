//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class CurveTool : Tool
{
    private readonly Shape _shape;
    private readonly Action _update;
    private readonly Action _commit;
    private readonly Action _cancel;
    private readonly float[] _savedCurves;
    private readonly Matrix3x2 _transform;
    private readonly Matrix3x2 _invTransform;
    private readonly List<ushort> _selectedSegments = [];

    private Vector2 _startWorld;

    public CurveTool(
        Shape shape,
        Matrix3x2 transform,
        float[] savedCurves,
        Action update,
        Action commit,
        Action cancel)
    {
        _shape = shape;
        _transform = transform;
        _savedCurves = savedCurves;
        _update = update;
        _commit = commit;
        _cancel = cancel;

        Matrix3x2.Invert(transform, out _invTransform);

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (shape.IsSegmentSelected(i))
                _selectedSegments.Add(i);
        }
    }

    public override void Begin()
    {
        _startWorld = Workspace.MouseWorldPosition;
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.KeyEnter))
        {
            _commit();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var startLocal = Vector2.Transform(_startWorld, _invTransform);
        var delta = mouseLocal - startLocal;

        foreach (var segIdx in _selectedSegments)
        {
            var a0 = _shape.GetAnchor(segIdx);
            var a1 = _shape.GetNextAnchor(segIdx);

            var p0 = a0.Position;
            var p1 = a1.Position;
            var dir = p1 - p0;
            var len = dir.Length();

            if (len < 0.0001f)
                continue;

            var perp = new Vector2(-dir.Y, dir.X) / len;
            var curveDelta = Vector2.Dot(delta, perp);

            var newCurve = _savedCurves[segIdx] + curveDelta;

            if (Input.IsCtrlDown())
                newCurve = SnapCurve(newCurve, len);

            _shape.SetAnchorCurve(segIdx, newCurve);
        }

        _shape.UpdateSamples();
        _shape.UpdateBounds();
        _update();
    }

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);

        Graphics.SetTransform(_transform);

        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.5f));
        foreach (var segIdx in _selectedSegments)
        {
            var a0 = _shape.GetAnchor(segIdx);
            var a1 = _shape.GetNextAnchor(segIdx);
            Gizmos.DrawDashedLine(a0.Position, a1.Position);
        }

        Graphics.SetTransform(Matrix3x2.Identity);

        Gizmos.SetColor(EditorStyle.Tool.PointColor);
        Gizmos.DrawCircle(_startWorld, EditorStyle.Tool.PointSize, order: 10);

        Gizmos.SetColor(EditorStyle.Tool.LineColor);
        Gizmos.DrawDashedLine(_startWorld, Workspace.MouseWorldPosition);
    }

    public override void Cancel()
    {
        _shape.RestoreAnchorCurves(_savedCurves);
        _shape.UpdateSamples();
        _shape.UpdateBounds();
        _cancel();
    }

    private static float SnapCurve(float curve, float segmentLength)
    {
        var threshold = segmentLength * 0.1f;

        if (MathF.Abs(curve) < threshold)
            return 0f;

        var circleOffset = segmentLength * 0.5f * 0.5522847498f;

        if (MathF.Abs(curve - circleOffset) < threshold)
            return circleOffset;

        if (MathF.Abs(curve + circleOffset) < threshold)
            return -circleOffset;

        return curve;
    }
}
