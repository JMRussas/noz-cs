//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public class PropertySet
{
    private readonly Dictionary<string, Dictionary<string, string>> _properties = new();

    public void Clear()
    {
        _properties.Clear();
    }

    public void ClearGroup(string group)
    {
        if (_properties.TryGetValue(group, out var g))
            g.Clear();
    }

    public void SetString(string group, string key, string value)
    {
        GetOrAddGroup(group)[key] = value;
    }

    public void SetInt(string group, string key, int value)
    {
        SetString(group, key, value.ToString());
    }

    public void SetFloat(string group, string key, float value)
    {
        SetString(group, key, value.ToString("F6"));
    }

    public void SetBool(string group, string key, bool value)
    {
        SetString(group, key, value ? "true" : "false");
    }

    public void SetVec2(string group, string key, Vector2 value)
    {
        SetString(group, key, $"({value.X:F6},{value.Y:F6})");
    }

    public void SetVec3(string group, string key, Vector3 value)
    {
        SetString(group, key, $"({value.X:F6},{value.Y:F6},{value.Z:F6})");
    }

    public void SetColor(string group, string key, Color value)
    {
        SetString(group, key, $"rgba({value.R * 255:F0},{value.G * 255:F0},{value.B * 255:F0},{value.A:F3})");
    }

    public void AddKey(string group, string key)
    {
        GetOrAddGroup(group)[key] = "";
    }

    public bool HasKey(string group, string key) =>
        _properties.TryGetValue(group, out var g) && g.ContainsKey(key);

    public bool HasGroup(string group) => _properties.ContainsKey(group);

    public string GetString(string group, string key, string defaultValue) =>
        _properties.TryGetValue(group, out var g)
            ? g.GetValueOrDefault(key, defaultValue)
            : defaultValue;
    
    public int GetInt(string group, string key, int defaultValue)
    {
        var str = GetString(group, key, "");
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        if (!tk.ExpectInt(out int result))
            return defaultValue;

        return result;
    }

    public float GetFloat(string group, string key, float defaultValue)
    {
        var str = GetString(group, key, "");
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        if (!tk.ExpectFloat(out float result))
            return defaultValue;

        return result;
    }

    public bool GetBool(string group, string key, bool defaultValue)
    {
        var str = GetString(group, key, "");
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        return str.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public Vector2 GetVector2(string group, string key, Vector2 defaultValue)
    {
        var str = GetString(group, key, "");
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        if (!tk.ExpectVec2(out Vector2 result))
            return defaultValue;

        return result;
    }

    public Vector3 GetVector3(string group, string key, Vector3 defaultValue)
    {
        var str = GetString(group, key, "");
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        if (!tk.ExpectVec3(out Vector3 result))
            return defaultValue;

        return result;
    }

    public Color GetColor(string group, string key, Color defaultValue)
    {
        var str = GetString(group, key, "");
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        if (!tk.ExpectColor(out Color result))
            return defaultValue;

        return result;
    }

    public IEnumerable<string> GetKeys(string group)
    {
        if (!_properties.TryGetValue(group, out var g))
            return Enumerable.Empty<string>();
        return g.Keys;
    }

    public IEnumerable<string> GetGroups()
    {
        return _properties.Keys;
    }

    private Dictionary<string, string> GetOrAddGroup(string group)
    {
        if (!_properties.TryGetValue(group, out var g))
        {
            g = new Dictionary<string, string>();
            _properties[group] = g;
        }
        return g;
    }

    public static PropertySet? Load(string content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        var props = new PropertySet();
        var tk = new Tokenizer(content);

        var groupName = "";

        while (!tk.IsEOF)
        {
            if (!tk.ExpectLine(out string line))
                break;

            if (string.IsNullOrEmpty(line))
                continue;

            // Group header: [groupname]
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                groupName = line.Substring(1, line.Length - 2);
                continue;
            }

            // Parse key=value
            var lineTk = new Tokenizer(line);

            if (!lineTk.ExpectToken(out Token keyToken))
                continue;

            string key = lineTk.GetString(keyToken);
            if (string.IsNullOrEmpty(key))
                continue;

            string value = "";
            if (lineTk.ExpectDelimiter('='))
            {
                if (lineTk.ExpectLine(out string lineValue))
                    value = lineValue;
            }

            props.SetString(groupName, key, value);
        }

        return props;
    }

    public static PropertySet? LoadFile(string path) =>
        File.Exists(path) ? Load(File.ReadAllText(path)) : null;
    
    public void Save(string path)
    {
        using var writer = new StreamWriter(path);

        foreach (var groupName in GetGroups())
        {
            writer.WriteLine($"[{groupName}]");

            foreach (var key in GetKeys(groupName))
            {
                var value = GetString(groupName, key, "");
                if (string.IsNullOrEmpty(value))
                    writer.WriteLine(key);
                else
                    writer.WriteLine($"{key} = {value}");
            }

            writer.WriteLine();
        }
    }
}
