//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz.editor;

public static class Grid
{
    private const float MaxAlpha = 0.6f;
    private const float TargetGridScreenSpacing = 64f;

    private static float _snapSpacing;
    private static bool _pixelGridVisible;
    private static int _dpi = 16;

    public static bool PixelGridVisible => _pixelGridVisible;
    public static float SnapSpacing => _snapSpacing;

    public static void Init()
    {
        _snapSpacing = 1f;
        _pixelGridVisible = false;
    }

    public static void Shutdown()
    {
    }

    public static void SetDpi(int dpi)
    {
        _dpi = dpi > 0 ? dpi : 16;
    }

    public static void Update(Camera camera)
    {
        var pixelSize = 1f / _dpi;
        var bounds = camera.WorldBounds;
        var worldWidth = bounds.Width;
        var screenWidth = camera.ScreenSize.X;

        var world = CalculateGridLevels(worldWidth, screenWidth, 1f, _dpi);

        var screenPixelsPerWorldPixel = screenWidth / (worldWidth / pixelSize);
        var pixelGridAlpha = 0f;
        if (screenPixelsPerWorldPixel > 8f)
            pixelGridAlpha = MathF.Min((screenPixelsPerWorldPixel - 8f) / 32f, 1f);

        _pixelGridVisible = pixelGridAlpha > float.Epsilon;

        if (_pixelGridVisible)
            _snapSpacing = pixelSize;
        else
            _snapSpacing = world.FineSpacing * 0.5f;
    }

    public static void Draw(Camera camera)
    {
        var pixelSize = 1f / _dpi;
        var bounds = camera.WorldBounds;
        var worldWidth = bounds.Width;
        var screenWidth = camera.ScreenSize.X;

        var world = CalculateGridLevels(worldWidth, screenWidth, 1f, _dpi);

        var screenPixelsPerWorldPixel = screenWidth / (worldWidth / pixelSize);
        var pixelGridAlpha = 0f;
        if (screenPixelsPerWorldPixel > 8f)
            pixelGridAlpha = MathF.Min((screenPixelsPerWorldPixel - 8f) / 32f, 1f);

        var spacing1 = world.CoarseSpacing;
        var alpha1 = world.CoarseAlpha * MaxAlpha;

        var spacing2 = world.FineSpacing;
        var alpha2 = world.FineAlpha * MaxAlpha;

        var spacing3 = pixelSize;
        var alpha3 = pixelGridAlpha * MaxAlpha * 0.5f;

        var gridColor = EditorStyle.GridColor.ToColor32();

        // Draw coarse grid
        DrawGridLines(camera, spacing1, gridColor.WithAlpha(alpha1));
        DrawGridLines(camera, spacing2, gridColor.WithAlpha(alpha2));

        // Draw zero lines (origin)
        DrawZeroLines(camera, gridColor);

        // Draw pixel grid when visible
        if (pixelGridAlpha > float.Epsilon)
            DrawGridLines(camera, spacing3, gridColor.WithAlpha(alpha3 * MaxAlpha * 0.5f));
    }

    public static void DrawPixelGrid(Camera camera)
    {
        if (!_pixelGridVisible) return;

        var pixelSize = 1f / _dpi;
        var bounds = camera.WorldBounds;
        var worldWidth = bounds.Width;
        var screenWidth = camera.ScreenSize.X;

        var screenPixelsPerWorldPixel = screenWidth / (worldWidth / pixelSize);
        var pixelGridAlpha = 0f;
        if (screenPixelsPerWorldPixel > 8f)
            pixelGridAlpha = MathF.Min((screenPixelsPerWorldPixel - 8f) / 32f, 1f);

        var gridColor = EditorStyle.GridColor.ToColor32();
        DrawGridLines(camera, pixelSize, gridColor.WithAlpha(pixelGridAlpha * MaxAlpha * 0.5f));
    }

    private static void DrawGridLines(Camera camera, float spacing, Color32 color)
    {
        if (color.A <= 0) return;

        var bounds = camera.WorldBounds;
        var left = bounds.Min.X;
        var right = bounds.Max.X;
        var bottom = bounds.Min.Y;
        var top = bounds.Max.Y;

        var screenSize = camera.ScreenSize;
        var worldHeight = top - bottom;
        var pixelsPerWorldUnit = screenSize.Y / worldHeight;
        var lineThickness = 1f / pixelsPerWorldUnit;

        // Vertical lines
        var startX = MathF.Floor(left / spacing) * spacing;
        for (var x = startX; x <= right + spacing; x += spacing)
        {
            Render.DrawQuad(
                x - lineThickness, bottom,
                lineThickness * 2f, top - bottom,
                color,
                layer: 10
            );
        }

        // Horizontal lines
        var startY = MathF.Floor(bottom / spacing) * spacing;
        for (var y = startY; y <= top + spacing; y += spacing)
        {
            Render.DrawQuad(
                left, y - lineThickness,
                right - left, lineThickness * 2f,
                color,
                layer: 10
            );
        }
    }

    private static void DrawZeroLines(Camera camera, Color32 color)
    {
        var bounds = camera.WorldBounds;
        var left = bounds.Min.X;
        var right = bounds.Max.X;
        var bottom = bounds.Min.Y;
        var top = bounds.Max.Y;

        var screenSize = camera.ScreenSize;
        var worldHeight = top - bottom;
        var pixelsPerWorldUnit = screenSize.Y / worldHeight;
        var lineThickness = 1.5f / pixelsPerWorldUnit;

        // Vertical zero line (Y axis)
        Render.DrawQuad(
            -lineThickness, bottom,
            lineThickness * 2f, top - bottom,
            color,
            layer: 11
        );

        // Horizontal zero line (X axis)
        Render.DrawQuad(
            left, -lineThickness,
            right - left, lineThickness * 2f,
            color,
            layer: 11
        );
    }

    private static GridLevels CalculateGridLevels(float worldWidth, float screenWidth, float baseUnit, float targetGridScreenSpacing)
    {
        var worldPerPixel = worldWidth / screenWidth;
        var idealWorldSpacing = worldPerPixel * targetGridScreenSpacing;

        var idealInBaseUnits = idealWorldSpacing / baseUnit;

        var logSpacing = MathF.Log10(idealInBaseUnits);
        var clampedLog = MathF.Max(logSpacing, 0f);
        var floorLog = MathF.Floor(clampedLog);

        var t = clampedLog - floorLog;

        var multiplier = MathF.Round(MathF.Pow(10f, floorLog));

        var fineSpacing = baseUnit * multiplier;
        var coarseSpacing = fineSpacing * 10f;

        var fineAlpha = 1f - t;
        var coarseAlpha = 1f;

        return new GridLevels(fineSpacing, fineAlpha, coarseSpacing, coarseAlpha);
    }

    public static Vector2 SnapToPixelGrid(Vector2 position)
    {
        var spacing = 1f / _dpi;
        return new Vector2(
            MathF.Round(position.X / spacing) * spacing,
            MathF.Round(position.Y / spacing) * spacing
        );
    }

    public static Vector2 SnapToGrid(Vector2 position)
    {
        var spacing = _snapSpacing > 0f ? _snapSpacing : 0.1f;
        return new Vector2(
            MathF.Round(position.X / spacing) * spacing,
            MathF.Round(position.Y / spacing) * spacing
        );
    }

    public static float SnapAngle(float angle)
    {
        const float angleStep = 15f;
        return MathF.Round(angle / angleStep) * angleStep;
    }

    private readonly struct GridLevels(float fineSpacing, float fineAlpha, float coarseSpacing, float coarseAlpha)
    {
        public readonly float FineSpacing = fineSpacing;
        public readonly float FineAlpha = fineAlpha;
        public readonly float CoarseSpacing = coarseSpacing;
        public readonly float CoarseAlpha = coarseAlpha;
    }
}
