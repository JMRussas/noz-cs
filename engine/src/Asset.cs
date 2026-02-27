//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Reflection;

namespace NoZ;

public class Asset : IDisposable {
    internal AssetDef Def { get; }
    protected internal nuint Handle { get; protected set; }
    public string Name { get; private set; }
    public StringId Id { get; private set; }
    private static readonly Dictionary<AssetType, AssetDef> Defs = new();
    private static readonly Dictionary<(AssetType, string), Asset> _registry = new();

    protected internal Asset(AssetType type, string name)
    {
        Id = StringId.Get(name);
        Name = name;
        Def = GetDef(type) ?? throw new InvalidOperationException($"No AssetDef registered for type {type}");
    }

    public static Asset? Load(AssetType type, string name, bool useRegistry=true, string? libraryPath = null)
    {
        if (_registry.TryGetValue((type, name), out var cached))
            return cached;

        var def = GetDef(type);
        if (def == null)
        {
            Log.Error($"No asset def registered for type {type}");
            return null;
        }

        var stream = LoadAssetStream(name, type, libraryPath);
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

        if (useRegistry && asset != null)
            _registry[(type, name)] = asset;

        return asset;
    }

    public static T? Get<T>(AssetType type, string name) where T : Asset
    {
        if (string.IsNullOrEmpty(name))
            return null;
        
        _registry.TryGetValue((type, name), out var asset);
        return asset as T;
    }

    public static T? Get<T>(AssetType type, nuint handle) where T : Asset
    {
        foreach (var asset in _registry.Values)
            if (asset.Def.Type == type && asset.Handle == handle)
                return asset as T;

        return null;
    }

    private static Stream? LoadAssetStream(string assetName, AssetType assetType, string? libraryPath = null)
    {
        var extension = assetType == AssetType.Shader ? Graphics.Driver.ShaderExtension : "";

        var stream = Application.Platform?.OpenAssetStream(assetType, assetName, extension, libraryPath);
        if (stream != null)
            return stream;

        var typeName = GetDef(assetType)?.Name.ToLowerInvariant() ?? assetType.ToString().ToLowerInvariant();
        var fileName = string.IsNullOrEmpty(extension) ? assetName : assetName + extension;
        return LoadEmbeddedResource($"assets.library.{typeName}.{fileName}");
    }

    private static Stream? LoadEmbeddedResource(string resourceSuffix)
    {
        var assembly = Application.ResourceAssembly;
        if (assembly == null)
            return null;

        try
        {
            var names = assembly.GetManifestResourceNames();
            foreach (var name in names)
            {
                if (name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var stream = assembly.GetManifestResourceStream(name);
                    if (stream != null)
                        return stream;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool ValidateAssetHeader(Stream stream, AssetType expectedType)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var sig = reader.ReadUInt32();
        if (sig != Constants.AssetSignature)
            return false;

        var type = new AssetType(reader.ReadUInt32());
        if (type != expectedType)
            return false;

        reader.ReadUInt16(); // version
        reader.ReadUInt16(); // flags

        return true;
    }

    public static ushort ReadAssetVersion(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12) return 0;
            reader.ReadUInt32(); // signature
            reader.ReadUInt32(); // type FourCC
            return reader.ReadUInt16();
        }
        catch
        {
            return 0;
        }
    }

    public static void RegisterDef(AssetDef def)
    {
        Debug.Assert(!Defs.ContainsKey(def.Type));
        Defs[def.Type] = def;
    }

    public static AssetDef? GetDef(AssetType type)
        => Defs.TryGetValue(type, out var def) ? def : null;

    public static IEnumerable<AssetDef> GetAllDefs() => Defs.Values;

    protected void Register() => _registry[(Def.Type, Name)] = this;
    protected void Unregister() => _registry.Remove((Def.Type, Name));

    public virtual void PostLoad()
    {
    }

    public static void PostLoadAll()
    {
        foreach (var asset in _registry.Values)
            asset.PostLoad();
    }

    internal static Asset? LoadFromStream(AssetType type, Stream stream, string name, bool useRegistry = true)
    {
        var def = GetDef(type);
        if (def == null)
        {
            Log.Error($"No asset def registered for type {type}");
            return null;
        }

        if (!ValidateAssetHeader(stream, type))
        {
            Log.Error($"Invalid asset header: {type}/{name}");
            return null;
        }

        var asset = def.Load(stream, name);

        if (useRegistry && asset != null)
            _registry[(type, name)] = asset;

        return asset;
    }

    public virtual void Dispose()
    {
    }
}

public static class AssetExtensions
{
    public static void WriteAssetHeader(this BinaryWriter writer, AssetType type, ushort version, ushort flags=0)
    {
        writer.Write(Constants.AssetSignature);
        writer.Write(type.Value);
        writer.Write(version);
        writer.Write(flags);
    }
}
