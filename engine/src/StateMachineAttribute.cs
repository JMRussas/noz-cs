//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[AttributeUsage(AttributeTargets.Class)]
public class StateMachineAttribute(Type enumType) : Attribute
{
    public Type EnumType { get; init; } = enumType;
}
