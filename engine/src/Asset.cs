//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace noz;

public class Asset : IDisposable {
    internal AssetDef Def { get; }
    public string Name { get; private set; }
    private static readonly AssetDef[] Defs = new AssetDef[Constants.AssetTypeCount];
    private static readonly Dictionary<(AssetType, string), Asset> _registry = new();

    internal Asset(AssetType type, string name)
    {
        Name = name;
        Def = Defs[(int)type];
        Debug.Assert(Def != null);
    }

    public static Asset? Load(AssetType type, string name)
    {
        if (_registry.TryGetValue((type, name), out var cached))
            return cached;

        var def = GetDef(type);
        if (def == null)
        {
            Log.Error($"No asset def registered for type {type}");
            return null;
        }

        var stream = LoadAssetStream(name, type);
        if (stream == null)
        {
            Log.Error($"Asset not found: {type}/{name}");
            return null;
        }

        Asset? asset;
        using (stream)
        {
            if (ValidateAssetHeader(stream, type))
                asset = def.Load(stream, name);
            else
            {
                Log.Error($"Invalid asset header: {type}/{name}");
                return null;
            }
        }

        if (asset != null)
            _registry[(type, name)] = asset;

        return asset;
    }

    public static T? Get<T>(AssetType type, string name) where T : Asset
    {
        _registry.TryGetValue((type, name), out var asset);
        return asset as T;
    }

    private static Stream? LoadAssetStream(string assetName, AssetType assetType)
    {
        var typeName = assetType.ToString().ToLowerInvariant();
        var fileName = assetType == AssetType.Shader
            ? assetName + Application.RenderBackend.ShaderExtension
            : assetName;

        var fullPath = Path.Combine(Application.AssetPath, typeName, fileName);
        return File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
    }

    private static bool ValidateAssetHeader(Stream stream, AssetType expectedType)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var signature = reader.ReadUInt32();
        if (signature != Constants.AssetSignature)
            return false;

        var type = (AssetType)reader.ReadByte();
        if (type != expectedType)
            return false;

        reader.ReadByte();   // version
        reader.ReadUInt16(); // flags

        return true;
    }

    internal static void RegisterDef(AssetDef def)
    {
        Debug.Assert(Defs[(int)def.Type] == null);
        Defs[(int)def.Type] = def;
    }

    public static AssetDef? GetDef(AssetType type)
    {
        var index = (int)type;
        return index >= 0 && index < Defs.Length ? Defs[index] : null;
    }

    public virtual void Dispose()
    {
    }
}
