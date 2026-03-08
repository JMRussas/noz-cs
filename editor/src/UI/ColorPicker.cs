//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class ColorPicker
{
    private const float SVSize = EditorStyle.ColorPicker.SVSize;
    private const float SwatchCellSize = 28;
    private const float SwatchColumns = 8;

    [ElementId("Hue")]
    [ElementId("Alpha")]
    [ElementId("Close")]
    [ElementId("Popup")]
    [ElementId("SaturationAndValue")]    
    [ElementId("ModeNone")]
    [ElementId("ModeColor")]
    [ElementId("ModePalette")]
    [ElementId("ColorPickerPaletteScroll")]
    [ElementId("ColorPickerPaletteItem", count: 256)]
    private static partial class ElementId { }

    private enum ColorMode
    {
        None,
        Color,
        Palette
    }

    private static int _popupId;
    private static float _hue;
    private static float _sat;
    private static float _val;
    private static float _alpha;
    private static Color32 _originalColor;

    // Textures
    private static Texture? _svTexture;
    private static Texture? _hueTexture;
    private static Texture? _checkerTexture;
    private static float _svTextureHue = -1;

    // Reusable pixel buffers (native memory, no GC pressure)
    private static PixelData<Color32>? _svPixels;
    private static PixelData<Color32>? _huePixels;
    private static PixelData<Color32>? _checkerPixels;

    private static Color32 _prevColor;

    private static ColorMode _paletteMode = ColorMode.None;

        public static bool IsOpen(int id) => _popupId == id;

    internal static void Open(int id, Color32 color)
    {
        _popupId = id;
        _originalColor = color;
        _prevColor = color;
        UI.SetHot<Color32>(id, color);
        RgbToHsv(color, out _hue, out _sat, out _val);
        _alpha = color.A / 255f;

        if (color.A == 0)
            _paletteMode = ColorMode.None;
        else
        {
            _paletteMode = ColorMode.Color;

            foreach (var palette in PaletteManager.Palettes)
            {
                for (int i = 0; i < palette.Count; i++)
                {
                    var paletteColor = palette.Colors[i];
                    if (paletteColor.ToColor32() == color)
                    {
                        _paletteMode = ColorMode.Palette;
                        return;
                    }
                }
            }
        }
    }

    private static void PopupInternal(int id, ref Color32 color)
    {
        var anchorRect = UI.GetElementWorldRect(id);
        using var popup = UI.BeginPopup(
            ElementId.Popup,
            new PopupStyle
            {
                AnchorX = Align.Min,
                AnchorY = Align.Min,
                PopupAlignX = Align.Max,
                PopupAlignY = Align.Min,
                Spacing = 2,
                ClampToScreen = true,
                AnchorRect = anchorRect,
            });

        if (UI.IsClosed())
        {
            color = _paletteMode != ColorMode.None ? HsvToColor32(_hue, _sat, _val, _alpha) : Color.Transparent;
            if (color != _prevColor)
                UI.NotifyChanged(color.GetHashCode());
            _popupId = -1;
            return;
        }

        UI.SetHot(id);

        using var col = UI.BeginColumn(EditorStyle.ColorPicker.Root);

        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing, Height = EditorStyle.Control.Height }))
        {
            UI.Spacer(EditorStyle.Control.Spacing);

            if (EditorUI.ToggleButton(ElementId.ModeNone, EditorAssets.Sprites.IconNofill, _paletteMode == ColorMode.None))
                _paletteMode = ColorMode.None;

            if (EditorUI.ToggleButton(ElementId.ModeColor, EditorAssets.Sprites.IconFill, _paletteMode == ColorMode.Color))
            {
                _paletteMode = ColorMode.Color;
                if (_alpha == 0)
                    _alpha = 1;
            }

            if (PaletteManager.Palettes.Count > 0)
                if (EditorUI.ToggleButton(ElementId.ModePalette, EditorAssets.Sprites.IconPalette, _paletteMode == ColorMode.Palette))
                    _paletteMode = ColorMode.Palette;

            UI.Flex();

            if (EditorUI.Button(ElementId.Close, EditorAssets.Sprites.IconClose))
            {
                // Cancel: reset hash to original so IsChanged() returns false
                UI.NotifyChanged(_originalColor.GetHashCode());
                _popupId = -1;
                return;
            }

            UI.Spacer(EditorStyle.Control.Spacing);
        }

        if (_paletteMode == ColorMode.Color)
        {
            SaturationAndValue();
            Hue();
            Alpha();
            color = HsvToColor32(_hue, _sat, _val, _alpha);
        }
        else if (_paletteMode == ColorMode.None)
        {
            color = Color.Transparent;
        }
        else
        {
            PaletteUI();
            color = HsvToColor32(_hue, _sat, _val, _alpha);
        }

        if (color != _prevColor)
        {
            UI.NotifyChanged(color.GetHashCode());
            _prevColor = color;
        }
    }

    internal static void Popup(int id, ref Color32 color)
    {
        if (_popupId == id)
            PopupInternal(id, ref color);
    }

    private static Vector2 GetNormalizedMouseInElement(int elementId)
    {
        var rect = UI.GetElementWorldRect(elementId);
        var mouse = UI.MouseWorldPosition;

        if (rect.Width <= 0 || rect.Height <= 0)
            return new Vector2(0.5f, 0.5f);

        return new Vector2(
            MathEx.Clamp01((mouse.X - rect.X) / rect.Width),
            MathEx.Clamp01((mouse.Y - rect.Y) / rect.Height)
        );
    }

    private static void SaturationAndValue()
    {
        EnsureSVTexture();

        using (UI.BeginContainer(ElementId.SaturationAndValue, EditorStyle.ColorPicker.SaturationAndValue))
        {
            if (_svTexture != null)
                UI.Image(_svTexture, ImageStyle.Fill);

            DrawCrosshair(
                _sat * EditorStyle.ColorPicker.SVSize,
                (1 - _val) * EditorStyle.ColorPicker.SVSize);

            if (UI.WasPressed())
                UI.SetCapture();

            if (UI.HasCapture())
            {
                var norm = GetNormalizedMouseInElement(ElementId.SaturationAndValue);
                _sat = norm.X;
                _val = 1 - norm.Y;
            }
        }
    }

    private static void Hue()
    {
        EnsureHueTexture();

        using (UI.BeginContainer(ElementId.Hue, EditorStyle.ColorPicker.Slider))
        {
            var color = HsvToColor(_hue, 1f, 1f);
            Slider(_hue / 360f, color, Color.Black, _hueTexture);

            if (UI.WasPressed())
                UI.SetCapture();

            if (UI.HasCapture())
            {
                var norm = GetNormalizedMouseInElement(ElementId.Hue);
                _hue = norm.X * 360f;
                InvalidateSVTexture();
            }
        }
    }

    private static void Alpha()
    {
        EnsureCheckerTexture();

        using (UI.BeginContainer(ElementId.Alpha, EditorStyle.ColorPicker.Slider))
        {
            Slider(
                _alpha,
                Color.Mix(Color.Black, Color.White, _alpha),
                Color.Mix(Color.White, Color.Black, _alpha),
                _checkerTexture);

            if (UI.WasPressed())
                UI.SetCapture();

            if (UI.HasCapture())
            {
                var norm = GetNormalizedMouseInElement(ElementId.Alpha);
                _alpha = norm.X;
            }
        }
    }

    private static UI.AutoContainer BeginColorContainer(int id, bool selected)
    {
        var container = UI.BeginContainer(id, new ContainerStyle
        {
            Padding = EdgeInsets.All(3),
            Color = selected ? EditorStyle.Palette.Primary : Color.Transparent,
            BorderRadius = EditorStyle.Control.BorderRadius
        });

        if (!selected && UI.IsHovered())
            UI.Container(EditorStyle.Control.HoverFill with
            {
                Margin = EdgeInsets.All(-3)
            });

        return container;
    }

    private static void PaletteUI()
    {
        var nextPaletteItemId = ElementId.ColorPickerPaletteItem;

        foreach (var palette in PaletteManager.Palettes)
        {
            UI.Text(palette.Label, EditorStyle.Control.Text);

            using var grid = UI.BeginGrid(new GridStyle
            {
                CellHeight = SwatchCellSize,
                CellWidth = SwatchCellSize,
                Columns = 16
            });

            for (int i = 0; i < palette.Count; i++)
            {
                var swatchColor = palette.Colors[i];
                if (swatchColor.A <= float.Epsilon) continue;

                var itemId = nextPaletteItemId++;
                var c32 = swatchColor.ToColor32();
                var isSelected = IsSwatchSelected(c32);

                using (BeginColorContainer(itemId, isSelected))
                {
                    UI.Container(new ContainerStyle
                    {
                        Color = swatchColor,
                        BorderRadius = EditorStyle.Control.BorderRadius - 2
                    });

                    if (UI.WasPressed())
                    {
                        RgbToHsv(c32, out _hue, out _sat, out _val);
                        _alpha = c32.A / 255f;
                        InvalidateSVTexture();
                    }
                }
            }
        }
    }

    private static bool IsSwatchSelected(Color32 swatch)
    {
        var current = HsvToColor32(_hue, _sat, _val, _alpha);
        return swatch.R == current.R && swatch.G == current.G && swatch.B == current.B;
    }

    private static void DrawCrosshair(float x, float y)
    {
        const float size = 6;
        UI.Container(new ContainerStyle
        {
            Width = size * 2,
            Height = size * 2,
            BorderWidth = 1.5f,
            BorderColor = Color.White,
            BorderRadius = size,
            Color = Color.Transparent,
            Margin = new EdgeInsets(y - size, x - size, 0, 0)
        });
    }

    private static void Slider(float t, Color color, Color borderColor, Texture? texture)
    {
        if (texture != null)
            UI.Image(texture, EditorStyle.ColorPicker.SliderImage);

        var sliderWidth = EditorStyle.ColorPicker.SliderWidth;
        sliderWidth -= EditorStyle.ColorPicker.SliderHeight;
        sliderWidth -= 2;

        UI.Container(new ContainerStyle
        {
            Size = EditorStyle.ColorPicker.SliderHeight - 2,
            Color = color,
            BorderRadius = (EditorStyle.ColorPicker.SliderHeight - 2) / 2,
            BorderColor = borderColor,
            BorderWidth = 1.5f,
            Margin = EdgeInsets.TopLeft(
                1,
                1 +
                t * sliderWidth)
        });
    }

    private static void EnsureCheckerTexture()
    {
        if (_checkerTexture != null) return;

        // Create a checkerboard pattern sized so cells appear roughly square
        // when stretched to the alpha bar (~280 x 20).  Each texel = one cell.
        const int w = 40;
        const int h = 3;
        var light = new Color32(255, 255, 255);
        var dark = new Color32(204, 204, 204);

        _checkerPixels ??= new PixelData<Color32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                _checkerPixels[x, y] = (x + y) % 2 == 0 ? light : dark;

        _checkerTexture = Texture.Create(w, h, _checkerPixels.AsByteSpan(),
            TextureFormat.RGBA8, TextureFilter.Point, "EditorCheckerboard");
    }

    private static void EnsureHueTexture()
    {
        if (_hueTexture != null) return;

        const int w = 256;
        const int h = 1;
        _huePixels ??= new PixelData<Color32>(w, h);

        for (int x = 0; x < w; x++)
            _huePixels[x, 0] = HsvToColor32(x / (float)w * 360f, 1f, 1f, 1f);

        _hueTexture = Texture.Create(w, h, _huePixels.AsByteSpan(), TextureFormat.RGBA8, TextureFilter.Linear, "EditorHueBar");
    }

    private static void EnsureSVTexture()
    {
        if (_svTexture != null && MathEx.Approximately(_svTextureHue, _hue))
            return;

        const int size = 64;
        _svPixels ??= new PixelData<Color32>(size, size);

        for (int y = 0; y < size; y++)
        {
            float v = 1f - y / (float)(size - 1);
            for (int x = 0; x < size; x++)
                _svPixels[x, y] = HsvToColor32(_hue, x / (float)(size - 1), v, 1f);
        }

        if (_svTexture == null)
            _svTexture = Texture.Create(size, size, _svPixels.AsByteSpan(), TextureFormat.RGBA8, TextureFilter.Linear, "EditorSVGradient");
        else
            _svTexture.Update(_svPixels.AsByteSpan());

        _svTextureHue = _hue;
    }

    private static void InvalidateSVTexture()
    {
        _svTextureHue = -1;
    }

    // --- HSV <-> RGB ---

    private static Color HsvToColor(float h, float s, float v)
    {
        return HsvToColor32(h, s, v, 1f).ToColor();
    }

    private static Color32 HsvToColor32(float h, float s, float v, float a)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;

        float r, g, b;
        if (h < 60f) { r = c; g = x; b = 0; }
        else if (h < 120f) { r = x; g = c; b = 0; }
        else if (h < 180f) { r = 0; g = c; b = x; }
        else if (h < 240f) { r = 0; g = x; b = c; }
        else if (h < 300f) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new Color32(
            (byte)((r + m) * 255f + 0.5f),
            (byte)((g + m) * 255f + 0.5f),
            (byte)((b + m) * 255f + 0.5f),
            (byte)(a * 255f + 0.5f)
        );
    }

    private static void RgbToHsv(Color32 c, out float h, out float s, out float v)
    {
        float r = c.R / 255f;
        float g = c.G / 255f;
        float b = c.B / 255f;

        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        v = max;
        s = max > 0f ? delta / max : 0f;

        if (delta <= float.Epsilon)
        {
            h = 0f;
        }
        else if (max == r)
        {
            h = 60f * ((g - b) / delta % 6f);
        }
        else if (max == g)
        {
            h = 60f * ((b - r) / delta + 2f);
        }
        else
        {
            h = 60f * ((r - g) / delta + 4f);
        }

        if (h < 0f) h += 360f;
    }
}
