//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ElementIdAttribute(string name, int count = 1) : Attribute
{
    public string Name { get; } = name;
    public int Count { get; } = count;
}
