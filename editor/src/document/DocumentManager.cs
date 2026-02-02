//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class DocumentManager
{
    private static readonly List<Document> _documents = [];
    private static readonly List<string> _sourcePaths = [];
    private static string _outputPath = "";

    public static IReadOnlyList<Document> Documents => _documents;
    public static IReadOnlyList<string> SourcePaths => _sourcePaths;
    public static string OutputPath => _outputPath;

    public static IEnumerable<Document> SelectedDocuments =>
        _documents.Where(d => d.IsSelected);

    public delegate void DocumentAddedDelegate(Document doc);

    public static event DocumentAddedDelegate? DocumentAdded;

    private static readonly Dictionary<AssetType, DocumentDef> _defsByType = new();
    private static readonly Dictionary<string, DocumentDef> _defsByExtension = new();

    public static int Count => _documents.Count;

    public static void Init(string[] sourcePaths, string outputPath)
    {
        _sourcePaths.Clear();
        _sourcePaths.AddRange(sourcePaths);
        _outputPath = outputPath;

        Log.Info($"DocumentOutputPath: {Path.GetFullPath(outputPath)}");
        foreach (var path in sourcePaths)
            Log.Info($"DocumentSourcePath: {Path.GetFullPath(path)}");

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

    public static IEnumerable<DocumentDef> GetCreatableDefs()
    {
        foreach (var def in _defsByType.Values)
            if (def.NewFile != null)
                yield return def;
    }
    
    public static Document? Create(string path)
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
        _documents.Add(doc);
        return doc;
    }

    private static string GenerateUniqueName(AssetType assetType, string baseName)
    {
        var name = baseName;
        var index = 1;
        while (Find(assetType, name) != null)
        {
            name = $"{baseName}{index:D3}";
            index++;
        }
        return name;
    }

    public static Document? New(AssetType assetType, string? name, System.Numerics.Vector2? position = null)
    {
        var def = GetDef(assetType);
        if (def == null || def.NewFile == null)
            return null;

        if (_sourcePaths.Count == 0)
            return null;

        name = name ?? MakeCanonicalName($"new_{assetType.ToString()}");
        name = GenerateUniqueName(assetType, name);

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

        var doc = Create(path);

        using (var writer = new StreamWriter(path))
            def.NewFile(writer);

        if (doc == null) return null;

        doc.LoadMetadata();
        doc.Load();

        if (position.HasValue)
        {
            doc.Position = position.Value;
            doc.MarkMetaModified();
        }

        doc.PostLoad();
        doc.PostLoaded = true;

        DocumentAdded?.Invoke(doc);

        AssetManifest.IsModified = true;

        return doc;
    }

    public static Document? Find(AssetType type, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var doc in _documents)
        {
            if ((type == default || doc.Def.Type == type) && doc.Name == name)
                return doc;
        }
        return null;
    }

    private static void LoadAll()
    {
        foreach (var doc in _documents)
        {
            if (doc.Loaded) continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            doc.Loaded = true;
            doc.Load();
            var elapsed = sw.ElapsedMilliseconds;
            if (elapsed > 500)
                Log.Info($"Loaded {doc.Name} in {elapsed} ms");
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
            {
                if (doc.IsVisible)
                    count++;
                doc.SilentImport = true;
            }

            if (doc.IsModified)
                doc.Save();

            if (doc.IsMetaModified)
                doc.SaveMetadata();
        }

        if (count > 0)
        {
            Log.Info($"Saved {count} asset(s)");
            Notifications.Add($"saved {count} asset(s)");
        }

        if (AssetManifest.IsModified)
            AssetManifest.Generate();
    }

    public static bool Rename(Document doc, string name)
    {
        var canonicalName = MakeCanonicalName(name);
        if (Find(doc.Def.Type, canonicalName) != null)
            return false;

        var directory = Path.GetDirectoryName(doc.Path);
        if (directory == null)
            return false;

        var newPath = Path.Combine(directory, canonicalName + doc.Def.Extension);
        if (File.Exists(newPath))
            return false;

        var oldMetaPath = doc.Path + ".meta";
        var newMetaPath = newPath + ".meta";

        File.Move(doc.Path, newPath);

        if (File.Exists(oldMetaPath))
            File.Move(oldMetaPath, newMetaPath);

        doc.Path = Path.GetFullPath(newPath).ToLowerInvariant();
        doc.Name = canonicalName;
        doc.MarkModified();

        AssetManifest.IsModified = true;

        return true;
    }

    public static void Delete(Document doc)
    {
        Undo.RemoveDocument(doc);

        if (File.Exists(doc.Path))
            File.Delete(doc.Path);

        var metaPath = doc.Path + ".meta";
        if (File.Exists(metaPath))
            File.Delete(metaPath);

        AssetManifest.IsModified = true;

        _documents.Remove(doc);
        doc.Dispose();
    }

    private static void InitDocuments()
    {
        foreach (var sourcePath in _sourcePaths)
        {
            if (!Directory.Exists(sourcePath))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(filePath);
                if (ext == ".meta") continue;

                var def = GetDef(ext);
                if (def == null) continue;

                var name = MakeCanonicalName(filePath);
                if (Find(def.Type, name) != null) continue;

                Create(filePath);
            }

            for (int i=0; i< _documents.Count; i++)
                _documents[i]?.LoadMetadata();
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

    public static string? GetUniquePath(string sourcePath)
    {
        var parentPath = Path.GetDirectoryName(sourcePath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var def = GetDef(ext);
        if (def == null) return null;
        var canonicalBase = MakeCanonicalName(fileName);

        var startIndex = 2;
        var lastUnderscore = canonicalBase.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(canonicalBase[(lastUnderscore + 1)..], out var existingNum))
        {
            canonicalBase = canonicalBase[..lastUnderscore];
            startIndex = existingNum + 1;
        }

        for (var i = startIndex; ; i++)
        {
            var candidate = Path.Combine(parentPath, $"{canonicalBase}_{i}{ext}");

            if (File.Exists(candidate))
                continue;

            var canonicalName = MakeCanonicalName(candidate);
            if (Find(def.Type, canonicalName) != null)
                continue;

            return candidate;
        }
    }

    public static Document? Duplicate(Document source)
    {
        var newPath = GetUniquePath(source.Path);
        if (newPath == null) return null;
        var newName = Path.GetFileNameWithoutExtension(newPath);

        var directory = Path.GetDirectoryName(newPath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        File.Copy(source.Path, newPath);

        var metaPath = source.Path + ".meta";
        if (File.Exists(metaPath))
            File.Copy(metaPath, newPath + ".meta");

        Importer.Queue(newPath);
        Importer.Update();

        var doc = Find(source.Def.Type, newName);
        if (doc == null) return null;

        doc.LoadMetadata();
        doc.Load();
        doc.PostLoad();
        doc.PostLoaded = true;
        doc.Position = source.Position;

        DocumentAdded?.Invoke(doc);

        return doc;
    }
}
