//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Reflection;

namespace NoZ;

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

    private static Stream? LoadAssetStream(string assetName, AssetType assetType, string? libraryPath = null)
    {
        var extension = assetType == AssetType.Shader ? Graphics.Driver.ShaderExtension : "";

        var stream = Application.Platform?.OpenAssetStream(assetType, assetName, extension, libraryPath);
        if (stream != null)
            return stream;

        var typeName = assetType.ToString().ToLowerInvariant();
        var fileName = string.IsNullOrEmpty(extension) ? assetName : assetName + extension;
        return LoadEmbeddedResource($"assets.library.{typeName}.{fileName}");
    }

    private static Stream? LoadEmbeddedResource(string resourceSuffix)
    {
        // Check all loaded assemblies for embedded resources
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            if (assembly == null || assembly.IsDynamic) continue;

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
                // Skip assemblies that don't support GetManifestResourceNames
            }
        }

        return null;
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

        reader.ReadUInt16(); // version
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

public static class AssetExtensions
{
    public static void WriteAssetHeader(this BinaryWriter writer, AssetType type, ushort version, ushort flags=0)
    {
        writer.Write(Constants.AssetSignature);
        writer.Write((byte)type);
        writer.Write(version);
        writer.Write(flags);
    }
}
