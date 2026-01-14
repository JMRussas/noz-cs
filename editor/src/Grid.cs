//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class Grid
{
    private const float MaxAlpha = 0.6f;
    private const float MaxPixelAlpha = 0.5f;
    private const ushort PixelGridLayer = 200;

    public static float SnapSpacing { get; private set; }
    public static bool IsPixelGridVisible { get; private set; }
    
    public static void Draw(Camera camera)
    {
        var dpi = (float)EditorApplication.Config!.AtlasDpi;
        var pixelSize = 1.0f / dpi;
        var bounds = camera.WorldBounds;
        var worldWidth = bounds.Width;
        var screenWidth = Application.WindowSize.X;
        var world = CalculateGridLevels(worldWidth, screenWidth, 1.0f, dpi);

        var screenPixelsPerWorldPixel = screenWidth / (worldWidth / pixelSize);
        var pixelGridAlpha = 0f;
        if (screenPixelsPerWorldPixel > 8f)
            pixelGridAlpha = MathF.Min((screenPixelsPerWorldPixel - 8f) / 32f, 1f) * MaxPixelAlpha;

        IsPixelGridVisible = pixelGridAlpha > float.Epsilon;

        if (IsPixelGridVisible)
            SnapSpacing = pixelSize;
        else
            SnapSpacing = world.FineSpacing * 0.5f;
        
        SnapSpacing = IsPixelGridVisible ? pixelSize : world.FineSpacing;

        DrawZeroLines(camera, EditorStyle.GridColor);
        

        if (IsPixelGridVisible)
        {
            Render.PushLayer(EditorRender.GridLayer);
            EditorRender.SetColor(EditorStyle.GridColor.WithAlpha(world.CoarseAlpha));
            DrawHorizontalLines(camera, world.FineSpacing);
            DrawVerticalLines(camera, world.FineSpacing);
            Render.PopLayer();
            
            Render.PushLayer(EditorRender.PixelGridLayer);
            EditorRender.SetColor(EditorStyle.GridColor.WithAlpha(pixelGridAlpha));
            DrawHorizontalLines(camera, pixelSize);
            DrawVerticalLines(camera, pixelSize);
            Render.PopLayer();
        } else {
            Render.PushLayer(EditorRender.GridLayer);
            EditorRender.SetColor(EditorStyle.GridColor.WithAlpha(world.CoarseAlpha * MaxAlpha));
            DrawHorizontalLines(camera, world.CoarseSpacing);
            DrawVerticalLines(camera, world.CoarseSpacing);

            EditorRender.SetColor(EditorStyle.GridColor.WithAlpha(world.FineAlpha));
            DrawHorizontalLines(camera, world.FineSpacing);
            DrawVerticalLines(camera, world.FineSpacing);
            Render.PopLayer();
        }
    }

    private static void DrawHorizontalLines(Camera camera, float spacing)
    {
        if (spacing <= 0.0001f) return;

        var bounds = camera.WorldBounds;
        var left = bounds.Min.X;
        var right = bounds.Max.X;
        var bottom = bounds.Min.Y;
        var top = bounds.Max.Y;

        // Find grid lines that intersect the view
        var startY = MathF.Floor(bottom / spacing) * spacing;
        var endY = MathF.Ceiling(top / spacing) * spacing;
        var lineCount = (int)((endY - startY) / spacing) + 1;

        if (lineCount > 500)
            return;

        var screenSize = camera.ScreenSize;
        var worldHeight = top - bottom;
        var pixelsPerWorldUnit = screenSize.Y / worldHeight;
        var lineThickness = 1f / pixelsPerWorldUnit;

        for (var y = startY; y <= endY; y += spacing)
        {
            if (y < bottom - lineThickness || y > top + lineThickness)
                continue;
            Render.DrawQuad(
                left, y - lineThickness,
                right - left, lineThickness * 2f
            );
        }
    }

    private static void DrawVerticalLines(Camera camera, float spacing)
    {
        if (spacing <= 0.0001f) return;

        var bounds = camera.WorldBounds;
        var left = bounds.Min.X;
        var right = bounds.Max.X;
        var bottom = bounds.Min.Y;
        var top = bounds.Max.Y;
        var startX = MathF.Floor(left / spacing) * spacing;
        var endX = MathF.Ceiling(right / spacing) * spacing;
        var lineCount = (int)((endX - startX) / spacing) + 1;

        if (lineCount > 500)
            return;

        var screenSize = camera.ScreenSize;
        var worldHeight = top - bottom;
        var pixelsPerWorldUnit = screenSize.Y / worldHeight;
        var lineThickness = 1f / pixelsPerWorldUnit;

        for (var x = startX; x <= endX; x += spacing)
        {
            if (x < left - lineThickness || x > right + lineThickness)
                continue;

            Render.DrawQuad(
                x - lineThickness, bottom,
                lineThickness * 2f, top - bottom
            );
        }
    }

    private static void DrawZeroLines(Camera camera, Color color)
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

        Render.SetColor(color);
        
        if (left < lineThickness && right > -lineThickness)
            Render.DrawQuad(
                -lineThickness, bottom,
                lineThickness * 2f, top - bottom
            );

        if (top < lineThickness && bottom > -lineThickness)
            Render.DrawQuad(
                left, -lineThickness,
                right - left, lineThickness * 2f
            );
    }

    private static GridLevels CalculateGridLevels(
        float worldWidth,
        float screenWidth,
        float baseUnit,
        float targetGridScreenSpacing)
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
        return new GridLevels(fineSpacing, 1f - t, fineSpacing * 10f, 1.0f);
    }

    public static Vector2 SnapToPixelGrid(Vector2 position)
    {
        var spacing = 1f / (float)EditorApplication.Config!.AtlasDpi;
        return new Vector2(
            MathF.Round(position.X / spacing) * spacing,
            MathF.Round(position.Y / spacing) * spacing
        );
    }

    public static Vector2 SnapToGrid(Vector2 position)
    {
        var spacing = SnapSpacing > 0f ? SnapSpacing : 0.1f;
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
