//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[System.Diagnostics.DebuggerDisplay("{(Value == 0 ? \"None\" : ToString())} ({Value})")]
public readonly struct StringId : IEquatable<StringId>
{
    public static StringId None => default;

    private const int MaxNames = 1024;
    private static readonly string[] _names = new string[MaxNames];
    private static readonly Dictionary<string, int> _nameDict = new(MaxNames);
    private static int _nextId = 1;  // Start at 1, 0 = None

    public readonly int Value { get; init; }

    public bool IsNone => Value == 0;

    public static StringId Get(string name)
    {
        if (_nameDict.TryGetValue(name, out var id))
            return new StringId { Value = id };
        if (_nextId >= MaxNames)
            throw new InvalidOperationException("Maximum number of names exceeded");
        id = _nextId++;
        _nameDict[name] = id;
        _names[id] = name;
        return new StringId { Value = id };
    }

    public bool Equals(StringId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StringId other && Equals(other);
    public override int GetHashCode() => Value;

    public static bool operator ==(StringId left, StringId right) => left.Equals(right);
    public static bool operator !=(StringId left, StringId right) => !(left == right);

    public readonly override string ToString() => Value > 0 ? _names[Value] : "";

    public ReadOnlySpan<char> AsReadOnlySpan() => Value > 0 ? _names[Value].AsSpan() : ReadOnlySpan<char>.Empty;
}
