//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;

namespace NoZ.Editor;

public class BundleDocument : Document
{
    public override bool CanSave => true;

    public bool IsRemote { get; set; }
    public List<BundleEntryDef> Entries { get; } = [];

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Bundle,
            Name = "AssetBundle",
            Extension = ".bundle",
            Factory = () => new BundleDocument(),
        });
    }

    public override void Load()
    {
        IsRemote = false;
        Entries.Clear();

        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("remote"))
            {
                IsRemote = true;
            }
            else if (tk.ExpectIdentifier("entry"))
            {
                if (tk.ExpectIdentifier(out var typeName) &&
                    tk.ExpectQuotedString(out var assetName))
                {
                    var assetType = ResolveTypeName(typeName);
                    if (assetType == AssetType.Unknown)
                    {
                        Log.Error($"BundleDocument.Load: Unknown asset type '{typeName}'");
                        continue;
                    }

                    // Optional entry name at end, defaults to asset name
                    tk.ExpectQuotedString(out var entryName);

                    Entries.Add(new BundleEntryDef
                    {
                        EntryName = string.IsNullOrEmpty(entryName) ? assetName : entryName,
                        AssetType = assetType,
                        AssetName = assetName,
                    });
                }
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"BundleDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }

        Loaded = true;
    }

    public override void Save(StreamWriter writer)
    {
        if (IsRemote)
            writer.WriteLine("remote");

        foreach (var entry in Entries)
        {
            var typeName = Asset.GetDef(entry.AssetType)?.Name ?? entry.AssetType.ToString();
            if (entry.EntryName != entry.AssetName)
            {
                writer.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "entry {0} \"{1}\" \"{2}\"",
                    typeName, entry.AssetName, entry.EntryName));
            }
            else
            {
                writer.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "entry {0} \"{1}\"",
                    typeName, entry.AssetName));
            }
        }
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Bundle, AssetBundle.Version);
        writer.Write(Entries.Count);

        foreach (var entry in Entries)
        {
            writer.Write(entry.EntryName);
            writer.Write(entry.AssetType.Value);

            var doc = DocumentManager.Find(entry.AssetType, entry.AssetName);
            if (doc == null)
            {
                Log.Error($"BundleDocument.Import: Asset not found: {entry.AssetType}/{entry.AssetName}");
                writer.Write(0);
                continue;
            }

            var tempPath = System.IO.Path.GetTempFileName();
            try
            {
                doc.Import(tempPath, new PropertySet());
                var data = File.ReadAllBytes(tempPath);
                writer.Write(data.Length);
                writer.Write(data);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }
    }

    private static AssetType ResolveTypeName(string name)
    {
        foreach (var def in Asset.GetAllDefs())
        {
            if (string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase))
                return def.Type;
        }
        return AssetType.Unknown;
    }
}

public class BundleEntryDef
{
    public string EntryName = string.Empty;
    public AssetType AssetType;
    public string AssetName = string.Empty;
}
