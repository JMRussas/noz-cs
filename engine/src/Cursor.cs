//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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
    public static void Set(SystemCursor cursor)
    {
        Application.Platform.SetCursor(cursor);
    }

    public static void SetDefault() => Set(SystemCursor.Default);
    public static void SetCrosshair() => Set(SystemCursor.Crosshair);
    public static void SetMove() => Set(SystemCursor.Move);
    public static void SetWait() => Set(SystemCursor.Wait);
    public static void Hide() => Set(SystemCursor.None);
}
