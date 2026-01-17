//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ
{

    public struct ContainerStyle()
    {
        public float Width = float.MaxValue;
        public float Height = float.MaxValue;
        public float MinWidth = 0;
        public float MinHeight = 0;
        public Align Align = Align.None;
        public EdgeInsets Margin = EdgeInsets.Zero;
        public EdgeInsets Padding = EdgeInsets.Zero;
        public Color Color = Color.Transparent;
        public BorderStyle Border = BorderStyle.None;
        public float Spacing = 0;
        public bool Clip = false;

        public ContainerData ToData() => new()
        {
            Width = Width,
            Height = Height,
            MinWidth = MinWidth,
            MinHeight = MinHeight,
            Align = Align,
            Margin = Margin,
            Padding = Padding,
            Color = Color,
            Border = Border,
            Spacing = Spacing,
            Clip = Clip
        };
    }

    public struct LabelStyle()
    {
        public float FontSize = 16;
        public Color Color = Color.White;
        public Align Align = Align.None;
        public Font? Font = null;
    }

    public struct ImageStyle()
    {
        public ImageStretch Stretch = ImageStretch.Uniform;
        public Align Align = Align.None;
        public float Scale = 1.0f;
        public Color Color = Color.White;
    }

    public struct RectangleStyle()
    {
        public float Width = float.MaxValue;
        public float Height = float.MaxValue;
        public Color Color = Color.White;
    }

    public struct ExpandedStyle()
    {
        public float Flex = 1.0f;
    }

    public struct TransformStyle()
    {
        public Vector2 Origin = new(0.5f, 0.5f);
        public Vector2 Translate = Vector2.Zero;
        public float Rotate = 0;
        public Vector2 Scale = Vector2.One;
    }

    public struct ScrollableStyle()
    {
    }

    public struct GridStyle()
    {
        public float Spacing = 0;
        public int Columns = 3;
        public float CellWidth = 100;
        public float CellHeight = 100;
        public int VirtualCount = 0;
        public byte ScrollId = 0;
        public Action<int, int>? VirtualCellFunc = null;
        public Action<int, int>? VirtualRangeFunc = null;
    }

    public struct PopupStyle()
    {
        public Align Anchor = Align.TopLeft;
        public Align PopupAlign = Align.TopLeft;
        public EdgeInsets Margin = EdgeInsets.Zero;
    }

    public struct CanvasStyle()
    {
        public CanvasType Type = CanvasType.Screen;
        public Color Color = Color.Transparent;
        public Vector2Int ColorOffset = Vector2Int.Zero;
        public Vector2 WorldPosition = Vector2.Zero;
        public Vector2 WorldSize = Vector2.Zero;
        public Camera? WorldCamera = null;

        public CanvasData ToData() => new()
        {
            Type = Type,
            Color = Color,
            ColorOffset = ColorOffset,
            WorldPosition = WorldPosition,
            WorldSize = WorldSize
        };
    }

    public struct TextBoxStyle()
    {
        public float Height = 28f;
        public float FontSize = 16;
        public Color BackgroundColor = new(0.22f, 0.22f, 0.22f, 1f);
        public Color TextColor = Color.White;
        public Color PlaceholderColor = new(0.4f, 0.4f, 0.4f, 1f);
        public BorderStyle Border = BorderStyle.None;
        public BorderStyle FocusBorder = BorderStyle.None;
        public bool IsPassword = false;

        public TextBoxData ToData() => new()
        {
            Height = Height,
            FontSize = FontSize,
            BackgroundColor = BackgroundColor,
            TextColor = TextColor,
            PlaceholderColor = PlaceholderColor,
            Border = Border,
            FocusBorder = FocusBorder,
            Password = IsPassword,
            TextStart = 0,
            TextLength = 0
        };
    }

    public static class ElementStyles
    {
        public static ContainerStyle WithColor(this ContainerStyle style, Color color)
        {
            style.Color = color;
            return style;
        }

        public static LabelStyle WithColor(this LabelStyle style, Color color)
        {
            style.Color = color;
            return style;
        }
    }
}
