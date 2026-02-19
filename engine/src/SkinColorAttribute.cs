//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class SkinColorAttribute : Attribute
{
    public string ColorName { get; }
    public string PropertyName { get; }

    public SkinColorAttribute(string colorName, string propertyName = "Color")
    {
        ColorName = colorName;
        PropertyName = propertyName;
    }
}
