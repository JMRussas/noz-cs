//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz.editor;

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
    
    public static void Init(string[] sourcePaths, string outputPath)
    {
        _sourcePaths.Clear();
        _sourcePaths.AddRange(sourcePaths);
        _outputPath = outputPath;

        Directory.CreateDirectory(outputPath);

        InitDocuments();
        LoadAll();
        PostLoadAll();
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
    
    public static Document? CreateDocument(string path)
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

    public static Document? FindDocument(AssetType type, string name)
    {
        foreach (var doc in _documents)
        {
            if ((type == default || doc.Def.Type == type) && doc.Name == name)
                return doc;
        }
        return null;
    }

    public static Document? FindDocument(string name)
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

    private static void PostLoadAll()
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
        foreach (var doc in _documents)
        {
            if (doc.IsModified)
            {
                doc.Save(doc.Path);
                doc.IsModified = false;
            }

            if (doc.IsMetaModified)
            {
                SaveMetadata(doc);
                doc.IsMetaModified = false;
            }
        }
    }

    public static void LoadMetadata(Document doc)
    {
        var props = PropertySet.LoadFile(doc.Path + ".meta");
        if (props == null)
            return;

        doc.Position = props.GetVector2("editor", "position", default);
        doc.LoadMetadata(props);
    }

    public static void SaveMetadata(Document doc)
    {
        var metaPath = doc.Path + ".meta";
        var props = PropertySet.LoadFile(metaPath) ?? new PropertySet();

        props.SetVec2("editor", "position", doc.Position);
        doc.SaveMetadata(props);

        props.Save(metaPath);
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
                if (FindDocument(name) != null)
                    continue;

                var doc = CreateDocument(filePath);
                if (doc != null)
                    LoadMetadata(doc);
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
}
