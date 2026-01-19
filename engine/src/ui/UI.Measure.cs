//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Engine.UI;

public static partial class UI
{
    private static float ResolveSize(ref readonly Element e, ref readonly Element p, in Size size, int axis)
    {
        Size resolveSize = size;
        if (size.Mode == SizeMode.Default)
            resolveSize = Size.Inherit();
        
        if (resolveSize.Mode == SizeMode.Fit)
        {
            // todo iterate children and determine size
        }

        return resolveSize.Mode switch
        {
            SizeMode.Fixed => size.Value,
            SizeMode.Inherit => p.Rect.GetSize(axis) * resolveSize.Value,
            _ => 0.0f,
        };
    }

    private static Vector2 MeasureContainer(ref readonly Element e, ref readonly Element p) => new Vector2(
        ResolveSize(in e, in p, e.Data.Container.Width, 0),
        ResolveSize(in e, in p, e.Data.Container.Height, 1));

    private static Vector2 MeasureElement(ref readonly Element e, ref readonly Element p) => e.Type switch
    {
        ElementType.Container => MeasureContainer(in e, in p),
        _ => new Vector2(
            ResolveSize(in e, in p, Size.Default, 0),
            ResolveSize(in e, in p, Size.Default, 1))
    };
}
