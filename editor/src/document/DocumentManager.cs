//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class DocumentManager
{
    private static readonly List<Document> _documents = new();
    private static readonly List<string> _sourcePaths = new();
    private static string _outputPath = "";

    public static IReadOnlyList<Document> Documents => _documents;
    public static IReadOnlyList<string> SourcePaths => _sourcePaths;
    public static string OutputPath => _outputPath;

    private static readonly Dictionary<AssetType, DocumentDef> _defsByType = new();
    private static readonly Dictionary<string, DocumentDef> _defsByExtension = new();

    public static int Count => _documents.Count;

    public static void Init(string[] sourcePaths, string outputPath)
    {
        Log.Info("DocumentManager.Init");

        _sourcePaths.Clear();
        _sourcePaths.AddRange(sourcePaths);
        _outputPath = outputPath;

        Log.Info($"Asset output path: {Path.GetFullPath(outputPath)}");
        foreach (var path in sourcePaths)
            Log.Info($"Asset source path: {Path.GetFullPath(path)}");

        Directory.CreateDirectory(outputPath);

        InitDocuments();
        LoadAll();
    }

    public static void Shutdown()
    {
        _documents.Clear();
        _sourcePaths.Clear();
    }

    public static void RegisterDef(DocumentDef def)
    {
        _defsByType[def.Type] = def;
        _defsByExtension[def.Extension] = def;
    }

    public static DocumentDef? GetDef(AssetType type)
    {
        return _defsByType.TryGetValue(type, out var def) ? def : null;
    }

    public static DocumentDef? GetDef(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return _defsByExtension.TryGetValue(ext, out var def) ? def : null;
    }
    
    public static Document? LoadDocument(string path)
    {
        string ext = Path.GetExtension(path);
        var def = GetDef(ext);
        if (def == null)
            return null;

        var doc = def.Factory();
        doc.Def = def;
        doc.Path = Path.GetFullPath(path).ToLowerInvariant();
        doc.Name = MakeCanonicalName(path);
        doc.Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
        
        // Find which source path this belongs to
        for (int i = 0; i < _sourcePaths.Count; i++)
        {
            if (doc.Path.StartsWith(_sourcePaths[i].ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                doc.SourcePathIndex = i;
                break;
            }
        }

        _documents.Add(doc);
        return doc;
    }

    public static Document? NewDocument(AssetType assetType, string name)
    {
        var def = GetDef(assetType);
        if (def == null || def.NewFile == null)
            return null;

        if (_sourcePaths.Count == 0)
            return null;

        var canonicalName = MakeCanonicalName(name);
        if (Find(assetType, canonicalName) != null)
            return null;

        var typeName = assetType.ToString().ToLowerInvariant();
        var path = Path.Combine(_sourcePaths[0], typeName, canonicalName + def.Extension);
        if (File.Exists(path))
            return null;

        var directory = Path.GetDirectoryName(path);
        if (directory != null)
            Directory.CreateDirectory(directory);

        using (var writer = new StreamWriter(path))
        {
            def.NewFile(writer);
        }

        var doc = LoadDocument(path);
        doc?.LoadMetadata();
        doc?.Load();
        return doc;
    }

    public static Document? Find(AssetType type, string name)
    {
        foreach (var doc in _documents)
        {
            if ((type == default || doc.Def.Type == type) && doc.Name == name)
                return doc;
        }
        return null;
    }

    public static Document? Find(string name)
    {
        foreach (var doc in _documents)
        {
            if (doc.Name == name)
                return doc;
        }
        return null;
    }

    private static void LoadAll()
    {
        foreach (var doc in _documents)
        {
            if (!doc.Loaded)
            {
                doc.Loaded = true;
                doc.Load();
            }
        }
    }

    internal static void PostLoad()
    {
        foreach (var doc in _documents)
        {
            if (doc.Loaded && !doc.PostLoaded)
            {
                doc.PostLoaded = true;
                doc.PostLoad();
            }
        }
    }

    public static void SaveAll()
    {
        var count = 0;
        foreach (var doc in _documents)
        {
            if (doc.IsModified || doc.IsMetaModified)
                count++;

            if (doc.IsModified)
                doc.Save();

            if (doc.IsMetaModified)
                doc.SaveMetadata();
        }

        if (count > 0)
            Notifications.Add($"saved {count} asset(s)");
    }

    private static void InitDocuments()
    {
        foreach (var sourcePath in _sourcePaths)
        {
            if (!Directory.Exists(sourcePath))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(filePath);

                // Skip meta files
                if (ext == ".meta")
                    continue;

                // Skip Luau definition files
                string filename = Path.GetFileName(filePath);
                if (filename.EndsWith(".d.luau") || filename.EndsWith(".d.lua"))
                    continue;

                // Skip if no document def for this extension
                if (GetDef(ext) == null)
                    continue;

                // Skip if document with this name already exists
                string name = MakeCanonicalName(filePath);
                if (Find(name) != null)
                    continue;

                LoadDocument(filePath)?.LoadMetadata();
            }
        }
    }

    public static string GetTargetPath(Document doc)
    {
        var typeName = doc.Def.Type.ToString().ToLowerInvariant();
        var filename = Path.GetFileNameWithoutExtension(doc.Path);
        var safeName = MakeCanonicalName(filename);
        return Path.Combine(_outputPath, typeName, safeName);
    }

    public static string MakeCanonicalName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.ToLowerInvariant()
            .Replace('/', '_')
            .Replace('.', '_')
            .Replace(' ', '_')
            .Replace('-', '_');
    }

    public static Document Get(int index) => _documents[index];
}
