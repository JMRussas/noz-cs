//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ButtonStyle
{
    public struct State
    {
        public Color Color;
        public float BorderWidth;
        public float BorderRadius;
        public Color BorderColor;
        public Color ContentColor;
    }

    public Font? Font = null;
    public float FontSize = 12.0f;
    public Size Width = Size.Fit;
    public Size Height = 30.0f;    
    public State Normal;
    public State Hovered;
    public EdgeInsets Padding;

    public ButtonStyle()
    {
    }
}

public static partial class UI
{
    public static bool Button(int id, ButtonStyle style)
    {
        var wasPressed = false;
        ElementTree.BeginWidget(id);
        wasPressed = ElementTree.WasPressed();
        ElementTree.EndWidget();
        return wasPressed;
    }
}

