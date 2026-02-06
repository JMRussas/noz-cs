//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public enum SystemCursor
{
    None,
    Default,
    Move,
    Crosshair,
    Wait
}

public static class Cursor
{
    private static Sprite? _sprite;
    private static readonly Camera _camera = new() { FlipY = false };

    public static void Set(SystemCursor cursor)
    {
        _sprite = null;
        Application.Platform.SetCursor(cursor);
    }

    public static void Set(Sprite sprite)
    {
        _sprite = sprite;
        Application.Platform.SetCursor(SystemCursor.None);
    }

    public static void SetDefault() => Set(SystemCursor.Default);
    public static void SetCrosshair() => Set(SystemCursor.Crosshair);
    public static void SetMove() => Set(SystemCursor.Move);
    public static void SetWait() => Set(SystemCursor.Wait);
    public static void Hide() => Set(SystemCursor.None);

    internal static void Update()
    {
        if (_sprite == null || !Input.MouseInWindow) return;

        var size = Application.WindowSize;
        _camera.SetExtents(new Rect(0, 0, size.X, size.Y));
        _camera.Update();

        using var _ = Graphics.PushState();
        Graphics.SetCamera(_camera);
        Graphics.SetLayer(Graphics.MaxLayer);
        Graphics.SetBlendMode(BlendMode.Alpha);
        Graphics.SetTransform(
            Matrix3x2.CreateScale(_sprite.PixelsPerUnit) *
            Matrix3x2.CreateTranslation(Input.MousePosition));
        Graphics.DrawFlat(_sprite);
    }
}
