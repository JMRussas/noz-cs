//
//  NoZ Hello World Example
//

using NoZ;
using NoZ.Examples;

namespace HelloWorld;

public class HelloWorldApp : IApplication
{
    private static readonly ContainerStyle RootStyle = new()
    {
        Size = new Size2(Size.Percent(1), Size.Percent(1)),
        AlignX = Align.Center,
        AlignY = Align.Center,
    };

    private static readonly ContainerStyle BoxStyle = new()
    {
        Size = Size2.Fit,
        Color = Color.FromRgb(0x2563EB),
        Padding = EdgeInsets.Symmetric(24, 48),
        Border = new BorderStyle { Radius = 8 },
    };

    private static readonly LabelStyle TitleStyle = new()
    {
        FontSize = 32,
        Color = Color.White,
        AlignX = Align.Center,
    };

    public void LoadAssets()
    {
        ExampleAssets.LoadAssets();
    }

    public void Update()
    {
        Graphics.ClearColor = Color.FromRgb(0x1E293B);
    }

    public void UpdateUI()
    {
        using (UI.BeginContainer(RootStyle))
        {
            using (UI.BeginContainer(BoxStyle))
            {
                UI.Label("Hello World!", TitleStyle);
            }
        }
    }
}
