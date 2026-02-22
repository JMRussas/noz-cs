//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class EditorUI
{
    [ElementId("ColorPickerPopup")]
    [ElementId("ColorPickerSVArea")]
    [ElementId("ColorPickerHueBar")]
    [ElementId("ColorPickerAlphaBar")]
    [ElementId("ColorPickerHexBox")]
    [ElementId("ColorPickerNoneButton")]
    [ElementId("ColorPickerSwatchItem", count: 64)]
    private static partial class ColorPickerIds { }

    /// <summary>
    /// Returns true on commit (popup closed or swatch clicked).
    /// Calls onPreview each frame the color changes during drag.
    /// </summary>
    public static bool ColorPickerButton(
        int id,
        ref Color32 color,
        Action<Color32>? onPreview = null,
        Color[]? swatches = null,
        int swatchCount = 0,
        Sprite? icon = null)
    {
        var oldColor = color;
        ColorPicker._buttonColor = _popupId == id ? ColorPicker.CurrentColor : oldColor;
        ColorPicker._buttonIcon = icon;
        ColorPicker._committed = false;

        if (Control(id, ColorPicker.ButtonContent, selected: false, disabled: false, toolbar: false))
        {
            TogglePopup(id);
            if (IsPopupOpen(id))
                ColorPicker.Open(color, swatches, swatchCount);
        }

        ColorPicker.Popup(id, ref color);

        if (color != oldColor)
        {
            if (ColorPicker._committed)
                return true;
            onPreview?.Invoke(color);
        }

        return false;
    }

    internal static class ColorPicker
    {
        private const int SVSize = 160;
        private const int HueBarWidth = 20;
        private const int HueBarHeight = SVSize;
        private const int AlphaBarWidth = SVSize + HueBarWidth + 6;
        private const int AlphaBarHeight = 14;
        private const int Spacing = 6;
        private const int SwatchCellSize = 28;
        private const int SwatchColumns = 8;

        // HSV state
        private static float _hue;
        private static float _sat;
        private static float _val;
        private static float _alpha;
        private static Color32 _originalColor;
        private static Color[]? _swatches;
        private static int _swatchCount;

        // Textures
        private static Texture? _svTexture;
        private static Texture? _hueTexture;
        private static float _svTextureHue = -1;

        // Reusable pixel buffers (native memory, no GC pressure)
        private static PixelData<Color32>? _svPixels;
        private static PixelData<Color32>? _huePixels;

        // Cached button state to avoid closure allocation
        internal static Color32 _buttonColor;
        internal static Sprite? _buttonIcon;

        // True when the color change is a final commit (popup closed or swatch click),
        // false when it's a live preview during drag. Callers should only record undo on commit.
        internal static bool _committed;
        internal static Color32 CurrentColor => HsvToColor32(_hue, _sat, _val, _alpha);

        // Hex string cache
        private static string _hexCache = "";
        private static Color32 _hexCacheColor;

        internal static void Open(Color32 color, Color[]? swatches, int swatchCount)
        {
            _originalColor = color;
            RgbToHsv(color, out _hue, out _sat, out _val);
            _alpha = color.A / 255f;
            _swatches = swatches;
            _swatchCount = swatchCount > 0 ? swatchCount : (swatches?.Length ?? 0);
        }

        internal static void ButtonContent()
        {
            if (_buttonIcon != null)
                ControlIcon(_buttonIcon);

            using var _ = UI.BeginContainer(new ContainerStyle
            {
                Width = EditorStyle.Control.ContentHeight,
                Padding = EdgeInsets.All(2)
            });

            if (_buttonColor.A == 0)
            {
                UI.Image(EditorAssets.Sprites.IconNofill, ImageStyle.Center);
            }
            else if (_buttonColor.A < 255)
            {
                UI.Image(EditorAssets.Sprites.IconOpacity, style: EditorStyle.Control.Icon);
                UI.Image(
                    EditorAssets.Sprites.IconOpacity,
                    EditorStyle.Control.Icon with { Color = _buttonColor.ToColor() });
            }
            else
            {
                UI.Container(new ContainerStyle
                {
                    Color = _buttonColor.ToColor(),
                    BorderRadius = 5
                });
            }
        }

        internal static void Popup(int id, ref Color32 color)
        {
            if (_popupId != id) return;

            _nextPopupItemId = ColorPickerIds.ColorPickerSwatchItem;

            var anchorRect = UI.GetElementWorldRect(id);
            using var popup = UI.BeginPopup(
                ColorPickerIds.ColorPickerPopup,
                new PopupStyle
                {
                    AnchorX = Align.Min,
                    AnchorY = Align.Min,
                    PopupAlignX = Align.Min,
                    PopupAlignY = Align.Max,
                    Spacing = 2,
                    ClampToScreen = true,
                    AnchorRect = anchorRect,
                });

            if (UI.IsClosed())
            {
                _popupId = -1;
                color = HsvToColor32(_hue, _sat, _val, _alpha);
                if (color != _originalColor)
                    _committed = true;
                return;
            }

            using var col = UI.BeginColumn(EditorStyle.Popup.Root with { Spacing = Spacing, Padding = EdgeInsets.All(8) });

            using (UI.BeginRow(new ContainerStyle { Spacing = Spacing }))
            {
                SaturationValueArea();
                HueBar();
            }

            AlphaBar();
            PreviewRow();
            HexInput();
            NoneButton();

            if (_swatches != null && _swatchCount > 0)
                SwatchGrid();

            color = HsvToColor32(_hue, _sat, _val, _alpha);
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

        private static void SaturationValueArea()
        {
            EnsureSVTexture();

            using (UI.BeginContainer(ColorPickerIds.ColorPickerSVArea, new ContainerStyle
            {
                Width = SVSize,
                Height = SVSize,
                BorderWidth = 1,
                BorderColor = Color.Black20Pct,
                BorderRadius = 3,
                Clip = true
            }))
            {
                if (_svTexture != null)
                {
                    UI.Image(_svTexture, new ImageStyle
                    {
                        Stretch = ImageStretch.Fill,
                        Size = new Size2(SVSize, SVSize)
                    });
                }

                DrawCrosshair(_sat * SVSize, (1 - _val) * SVSize);

                if (UI.IsDown())
                {
                    var norm = GetNormalizedMouseInElement(ColorPickerIds.ColorPickerSVArea);
                    _sat = norm.X;
                    _val = 1 - norm.Y;
                }
            }
        }

        private static void HueBar()
        {
            EnsureHueTexture();

            using (UI.BeginContainer(ColorPickerIds.ColorPickerHueBar, new ContainerStyle
            {
                Width = HueBarWidth,
                Height = HueBarHeight,
                BorderWidth = 1,
                BorderColor = Color.Black20Pct,
                BorderRadius = 3,
                Clip = true
            }))
            {
                if (_hueTexture != null)
                {
                    UI.Image(_hueTexture, new ImageStyle
                    {
                        Stretch = ImageStretch.Fill,
                        Size = new Size2(HueBarWidth, HueBarHeight)
                    });
                }

                DrawIndicatorH(_hue / 360f * HueBarHeight, HueBarWidth);

                if (UI.IsDown())
                {
                    var norm = GetNormalizedMouseInElement(ColorPickerIds.ColorPickerHueBar);
                    _hue = norm.Y * 360f;
                    InvalidateSVTexture();
                }
            }
        }

        private static void AlphaBar()
        {
            using (UI.BeginContainer(ColorPickerIds.ColorPickerAlphaBar, new ContainerStyle
            {
                Width = AlphaBarWidth,
                Height = AlphaBarHeight,
                BorderWidth = 1,
                BorderColor = Color.Black20Pct,
                BorderRadius = 3,
                Clip = true
            }))
            {
                UI.Container(new ContainerStyle { Color = Color.White });

                var solidColor = HsvToColor(_hue, _sat, _val);
                UI.Container(new ContainerStyle
                {
                    Color = solidColor.WithAlpha(_alpha),
                    Margin = EdgeInsets.All(-1)
                });

                DrawIndicatorV(_alpha * AlphaBarWidth, AlphaBarHeight);

                if (UI.IsDown())
                {
                    var norm = GetNormalizedMouseInElement(ColorPickerIds.ColorPickerAlphaBar);
                    _alpha = norm.X;
                }
            }
        }

        private static void PreviewRow()
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4, Height = 24 }))
            {
                UI.Container(new ContainerStyle
                {
                    Width = Size.Percent(0.5f),
                    Color = _originalColor.ToColor(),
                    BorderRadius = 3
                });

                var newColor = HsvToColor(_hue, _sat, _val).WithAlpha(_alpha);
                UI.Container(new ContainerStyle
                {
                    Width = Size.Percent(0.5f),
                    Color = newColor,
                    BorderRadius = 3
                });
            }
        }

        private static void HexInput()
        {
            var currentColor = HsvToColor32(_hue, _sat, _val, _alpha);
            var hex = Color32ToHex(currentColor);

            using (UI.BeginRow(new ContainerStyle { Spacing = 4, Height = EditorStyle.Control.Height }))
            {
                UI.Label("#", EditorStyle.Control.Text);

                using (UI.BeginContainer(ContainerStyle.Default))
                {
                    if (!UI.IsFocused(ColorPickerIds.ColorPickerHexBox))
                        UI.SetTextBoxText(ColorPickerIds.ColorPickerHexBox, hex);

                    if (UI.TextBox(ColorPickerIds.ColorPickerHexBox, EditorStyle.Inspector.TextBox, "FFFFFF"))
                    {
                        var text = UI.GetTextBoxText(ColorPickerIds.ColorPickerHexBox).ToString();
                        if (TryParseHexToColor32(text, out var parsed))
                        {
                            RgbToHsv(parsed, out _hue, out _sat, out _val);
                            _alpha = parsed.A / 255f;
                            InvalidateSVTexture();
                        }
                    }
                }
            }
        }

        private static void NoneButton()
        {
            if (Button(ColorPickerIds.ColorPickerNoneButton, EditorAssets.Sprites.IconNofill, selected: _alpha <= 0f))
            {
                _alpha = 0f;
                _committed = true;
            }
        }

        private static void SwatchGrid()
        {
            UI.Container(new ContainerStyle
            {
                Height = 1,
                Color = Color.White5Pct,
                Margin = EdgeInsets.TopBottom(2)
            });

            using var _ = UI.BeginGrid(new GridStyle
            {
                CellHeight = SwatchCellSize,
                CellWidth = SwatchCellSize,
                Columns = SwatchColumns
            });

            for (int i = 0; i < _swatchCount; i++)
            {
                var swatchColor = _swatches![i];
                if (swatchColor.A <= float.Epsilon) continue;

                var itemId = _nextPopupItemId++;
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
                        _committed = true;
                    }
                }
            }
        }

        private static bool IsSwatchSelected(Color32 swatch)
        {
            var current = HsvToColor32(_hue, _sat, _val, _alpha);
            return swatch.R == current.R && swatch.G == current.G && swatch.B == current.B;
        }

        // --- Indicators ---

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

        private static void DrawIndicatorH(float y, float width)
        {
            UI.Container(new ContainerStyle
            {
                Width = width,
                Height = 3,
                Color = Color.White,
                BorderWidth = 0.5f,
                BorderColor = Color.Black,
                Margin = new EdgeInsets(y - 1.5f, 0, 0, 0)
            });
        }

        private static void DrawIndicatorV(float x, float height)
        {
            UI.Container(new ContainerStyle
            {
                Width = 3,
                Height = height,
                Color = Color.White,
                BorderWidth = 0.5f,
                BorderColor = Color.Black,
                Margin = new EdgeInsets(0, x - 1.5f, 0, 0)
            });
        }

        // --- Texture generation ---

        private static void EnsureHueTexture()
        {
            if (_hueTexture != null) return;

            const int w = 1;
            const int h = 256;
            _huePixels ??= new PixelData<Color32>(w, h);

            for (int y = 0; y < h; y++)
                _huePixels[0, y] = HsvToColor32(y / (float)h * 360f, 1f, 1f, 1f);

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

        // --- Hex helpers ---

        private static string Color32ToHex(Color32 c)
        {
            if (c == _hexCacheColor && _hexCache.Length > 0)
                return _hexCache;

            _hexCacheColor = c;
            _hexCache = c.A == 255
                ? $"{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
            return _hexCache;
        }

        private static bool TryParseHexToColor32(string text, out Color32 color)
        {
            color = Color32.White;
            text = text.Trim();
            if (text.StartsWith('#'))
                text = text[1..];

            if (text.Length == 6)
            {
                if (byte.TryParse(text[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                    byte.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                    byte.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    color = new Color32(r, g, b);
                    return true;
                }
            }
            else if (text.Length == 8)
            {
                if (byte.TryParse(text[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                    byte.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                    byte.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b) &&
                    byte.TryParse(text[6..8], System.Globalization.NumberStyles.HexNumber, null, out var a))
                {
                    color = new Color32(r, g, b, a);
                    return true;
                }
            }
            else if (text.Length == 3)
            {
                if (byte.TryParse(text[0..1], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                    byte.TryParse(text[1..2], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                    byte.TryParse(text[2..3], System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    color = new Color32((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                    return true;
                }
            }

            return false;
        }
    }
}
