//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;

namespace NoZ.Editor;

public static class Project
{
    private static readonly List<Document> _documents = [];
    private static readonly List<string> _sourcePaths = [];
    private static readonly List<FileSystemWatcher> _watchers = [];
    private static string _outputPath = "";
    private static bool _initialized;

    private static readonly Queue<Document> _exportQueue = [];
    private static readonly Queue<Document> _exportDeferred = [];
    private static readonly ConcurrentQueue<string> _watcherQueue = [];
    private static readonly ConcurrentQueue<string> _reloadQueue = [];
    private static bool _watching;

    public static string Path { get; private set; } = "";
    public static bool IsInitialized => _initialized;
    public static IReadOnlyList<Document> Documents => _documents;
    public static int Count => _documents.Count;
    public static Document GetAt(int index) => _documents[index];
    public static IReadOnlyList<string> SourcePaths => _sourcePaths;
    public static string OutputPath => _outputPath;

    public static IEnumerable<Document> SelectedDocuments =>
        _documents.Where(d => d.IsSelected);

    public delegate void DocumentAddedDelegate(Document doc);

    public static event DocumentAddedDelegate? DocumentAdded;
    public static event DocumentAddedDelegate? DocumentRemoved;
    public static event Action<Document, string>? DocumentRenamed;
    public static event Action<Document>? OnExported;

    public static void NotifyDocumentAdded(Document doc) => DocumentAdded?.Invoke(doc);

    private static readonly Dictionary<AssetType, DocumentDef> _defsByType = new();
    private static readonly Dictionary<string, List<DocumentDef>> _defsByExtension = new();

    public static void Init(string projectPath, EditorConfig config)
    {
        Path = projectPath.Replace('\\', '/');
        _sourcePaths.Clear();
        _sourcePaths.AddRange(config.SourcePaths.Select(p => CombinePath(projectPath, p)));
        _outputPath = CombinePath(projectPath, config.OutputPath);

        Log.Info($"DocumentOutputPath: {_outputPath}");
        foreach (var path in _sourcePaths)
            Log.Info($"DocumentSourcePath: {path}");

        Directory.CreateDirectory(_outputPath);

        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();
        SpriteDocument.RegisterDef();
        SpriteInstanceDocument.RegisterDef();
        GenerationConfig.RegisterDef();
        FontDocument.RegisterDef();
        SkeletonDocument.RegisterDef();
        AnimationDocument.RegisterDef();
        VfxDocument.RegisterDef();
        BinDocument.RegisterDef();
        BundleDocument.RegisterDef();
        PaletteDocument.RegisterDef();
        SceneDocument.RegisterDef();

        //config.RegisterDocumentTypes?.Invoke();

        InitDocuments();
    }

    public static void InitExports()
    {
        foreach (var doc in _documents)
            QueueExport(doc);

        UpdateExports();

        foreach (var sourcePath in _sourcePaths)
            if (Directory.Exists(sourcePath))
                StartWatching(sourcePath);

        _watching = true;
    }

    public static void Shutdown()
    {
        StopWatching();

        _initialized = false;
        _documents.Clear();
        _sourcePaths.Clear();
    }

    public static void RegisterDef(DocumentDef def)
    {
        _defsByType[def.Type] = def;
        foreach (var ext in def.Extensions)
        {
            if (!_defsByExtension.TryGetValue(ext, out var list))
                _defsByExtension[ext] = list = [];
            list.Add(def);
        }
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
        return _defsByExtension.TryGetValue(ext, out var list) && list.Count > 0 ? list[0] : null;
    }

    public static IReadOnlyList<DocumentDef>? GetDefs(string ext)
    {
        ext = ext.ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return _defsByExtension.TryGetValue(ext, out var list) ? list : null;
    }

    private static bool IsAuxiliaryFile(string path)
    {
        var filename = System.IO.Path.GetFileName(path);
        foreach (var def in _defsByType.Values)
        {
            if (def.AuxiliaryExtensions == null) continue;
            foreach (var auxExt in def.AuxiliaryExtensions)
            {
                if (filename.EndsWith(auxExt, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static string? GetAuxiliaryParentPath(string path)
    {
        var dir = GetDirectory(path);
        var filename = System.IO.Path.GetFileName(path);
        foreach (var def in _defsByType.Values)
        {
            if (def.AuxiliaryExtensions == null) continue;
            foreach (var auxExt in def.AuxiliaryExtensions)
            {
                if (!filename.EndsWith(auxExt, StringComparison.OrdinalIgnoreCase)) continue;
                var stem = filename[..^auxExt.Length];
                foreach (var primaryExt in def.Extensions)
                {
                    var candidate = CombinePath(dir, stem + primaryExt);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }
        return null;
    }

    public static DocumentDef? ResolveDef(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        var defs = GetDefs(ext);
        if (defs == null || defs.Count == 0)
            return null;
        if (defs.Count == 1)
            return defs[0];

        var metaPath = path + ".meta";
        if (File.Exists(metaPath))
        {
            var meta = PropertySet.LoadFile(metaPath);
            var docType = meta?.GetString("editor", "document_type", "");
            if (!string.IsNullOrEmpty(docType))
            {
                foreach (var def in defs)
                {
                    if (string.Equals(def.Name, docType, StringComparison.OrdinalIgnoreCase))
                        return def;
                }
            }
        }

        return defs[0];
    }

    public static Document? Create(string path)
    {
        var def = ResolveDef(path);
        if (def == null)
            return null;

        var normalizedPath = path.Replace('\\', '/');
        var doc = def.Factory(normalizedPath);
        doc.Def = def;
        doc.Path = normalizedPath;
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

    public static Document? New(AssetType assetType, string extension, string? name, System.Numerics.Vector2? position = null) =>
        New(assetType, extension, name, (Stream stream) => {}, position);

    public static Document? New(AssetType assetType, string extension, string? name, Action<StreamWriter> writeContent, System.Numerics.Vector2? position = null) =>
        New(assetType, extension, name, stream =>
        {
            using var sw = new StreamWriter(stream);
            writeContent(sw);
            sw.Flush();
        }, position);

    public static Document? New(AssetType assetType, string extension, string? name, Action<Stream> writeContent, System.Numerics.Vector2? position = null)
    {
        if (_sourcePaths.Count == 0)
            return null;

        var typeName = (Asset.GetDef(assetType)?.Name ?? GetDef(assetType)?.Name ?? assetType.ToString()).ToLowerInvariant();
        name = name ?? MakeCanonicalName($"new_{typeName}");
        name = GenerateUniqueName(assetType, name);

        var canonicalName = MakeCanonicalName(name);
        if (Find(assetType, canonicalName) != null)
            return null;
        var path = CombinePath(CombinePath(_sourcePaths[0], typeName), canonicalName + extension);
        if (File.Exists(path))
            return null;

        var directory = GetDirectory(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var ms = new MemoryStream();
        writeContent?.Invoke(ms);
        File.WriteAllBytes(path, ms.ToArray());

        var doc = Create(path);
        if (doc == null) return null;

        doc.Loaded = true;
        doc.Load();
        doc.LoadMetadata();

        if (position.HasValue)
        {
            doc.Position = position.Value;
            doc.IncrementVersion();
        }

        if (_initialized)
        {
            doc.PostLoad();
            doc.PostLoaded = true;
        }

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

    public static T? Find<T>(string name) where T : Document
        => Find(DocumentDef<T>.Def.Type, name) as T;

    public static void LoadAll()
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

        // Load metadata after Load() so DiscoverFiles() has resolved the correct Path
        // (e.g. cheese.png -> cheese.sprite) before we read the .meta file.
        foreach (var doc in _documents)
            doc.LoadMetadata();
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

        _initialized = true;
    }

    private static void OnDocumentExported(Document doc)
    {
        if (!_initialized)
            return;

        if (!doc.Loaded)
        {
            try
            {
                doc.Loaded = true;
                doc.Load();
                doc.LoadMetadata();
                doc.PostLoad();
                doc.PostLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to load '{doc.Name}' after export: {ex.Message}");
                doc.Loaded = false;
            }
            finally
            {
                NotifyDocumentAdded(doc);
            }
        }
    }

    public static void SaveAll()
    {
        var count = 0;
        foreach (var doc in _documents)
        {
            if (!doc.IsModified)
                continue;

            count++;
            doc.SilentExport = true;
            doc.Save();
            doc.SaveMetadata();
        }

        if (count > 0)
            Log.Info($"Saved {count} asset(s)");

        if (AssetManifest.IsModified)
            AssetManifest.Generate();
    }

    private static IEnumerable<string> GetCompanionFiles(Document doc)
    {
        var directory = GetDirectory(doc.Path);
        var stem = System.IO.Path.GetFileNameWithoutExtension(doc.Path);
        foreach (var ext in doc.Def.Extensions)
        {
            var path = CombinePath(directory, stem + ext);
            if (File.Exists(path) && !string.Equals(path, doc.Path, StringComparison.OrdinalIgnoreCase))
                yield return path;
        }

        if (doc.Def.AuxiliaryExtensions != null)
        {
            foreach (var auxExt in doc.Def.AuxiliaryExtensions)
            {
                var path = CombinePath(directory, stem + auxExt);
                if (File.Exists(path))
                    yield return path;
            }
        }
    }

    public static bool Rename(Document doc, string name)
    {
        var canonicalName = MakeCanonicalName(name);
        if (Find(doc.Def.Type, canonicalName) != null)
            return false;

        var directory = GetDirectory(doc.Path);
        if (string.IsNullOrEmpty(directory))
            return false;

        var currentExt = System.IO.Path.GetExtension(doc.Path);
        var newPath = CombinePath(directory, canonicalName + currentExt);
        if (File.Exists(newPath))
            return false;

        // Rename companion files (e.g., .png alongside .sprite)
        foreach (var companionPath in GetCompanionFiles(doc).ToList())
        {
            var companionExt = System.IO.Path.GetExtension(companionPath);
            var newCompanionPath = CombinePath(directory, canonicalName + companionExt);
            File.Move(companionPath, newCompanionPath);
        }

        var oldMetaPath = doc.Path + ".meta";
        var newMetaPath = newPath + ".meta";

        File.Move(doc.Path, newPath);

        if (File.Exists(oldMetaPath))
            File.Move(oldMetaPath, newMetaPath);

        var oldName = doc.Name;
        var oldTargetPath = GetTargetPath(doc);

        doc.Path = newPath.Replace('\\', '/');
        doc.Name = canonicalName;
        doc.IncrementVersion();

        if (File.Exists(oldTargetPath))
            File.Delete(oldTargetPath);

        foreach (var other in _documents)
        {
            if (other == doc || !other.Loaded) continue;
            other.OnRenamed(doc, oldName, canonicalName);
        }

        DocumentRenamed?.Invoke(doc, oldName);
        AssetManifest.IsModified = true;

        return true;
    }

    public static Document? ChangeType(Document doc, DocumentDef newDef)
    {
        if (doc.Def == newDef)
            return doc;

        var path = doc.Path;
        var position = doc.Position;
        var collectionId = doc.CollectionId;
        var shouldExport = doc.ShouldExport;
        var wasSelected = doc.IsSelected;

        // Update the meta file with the new document type
        var metaPath = path + ".meta";
        var meta = PropertySet.LoadFile(metaPath) ?? new PropertySet();
        meta.SetString("editor", "document_type", newDef.Name);
        meta.Save(metaPath);

        // Remove old document
        Workspace.ClearSelection();
        _documents.Remove(doc);
        doc.Dispose();

        // Create new document from same path (ResolveDef will read the updated meta)
        var newDoc = Create(path);
        if (newDoc == null)
            return null;

        newDoc.Loaded = true;
        newDoc.Load();
        newDoc.LoadMetadata();
        newDoc.Position = position;
        newDoc.CollectionId = collectionId;
        newDoc.ShouldExport = shouldExport;
        newDoc.PostLoad();
        newDoc.PostLoaded = true;

        if (wasSelected)
            Workspace.SetSelected(newDoc, true);

        AssetManifest.IsModified = true;

        return newDoc;
    }

    public static void Remove(Document doc)
    {
        _documents.Remove(doc);
        doc.Dispose();
    }

    public static void Delete(Document doc)
    {
        Undo.RemoveDocument(doc);

        // Delete companion files
        foreach (var companionPath in GetCompanionFiles(doc).ToList())
            if (File.Exists(companionPath))
                File.Delete(companionPath);

        if (File.Exists(doc.Path))
            File.Delete(doc.Path);

        var metaPath = doc.Path + ".meta";
        if (File.Exists(metaPath))
            File.Delete(metaPath);

        AssetManifest.IsModified = true;

        _documents.Remove(doc);
        DocumentRemoved?.Invoke(doc);
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
                var ext = System.IO.Path.GetExtension(filePath);
                if (ext == ".meta") continue;
                if (IsAuxiliaryFile(filePath)) continue;

                var def = ResolveDef(filePath);
                if (def == null) continue;

                var name = MakeCanonicalName(filePath);
                if (Find(def.Type, name) != null) continue;

                Create(filePath);
            }
        }
    }

    public static string GetTargetPath(Document doc)
    {
        var typeName = (Asset.GetDef(doc.Def.Type)?.Name ?? doc.Def.Name).ToLowerInvariant();
        var filename = System.IO.Path.GetFileNameWithoutExtension(doc.Path);
        var safeName = MakeCanonicalName(filename);
        return CombinePath(CombinePath(_outputPath, typeName), safeName);
    }

    private static string CombinePath(string a, string b) =>
        System.IO.Path.Combine(a, b).Replace('\\', '/');

    private static string GetDirectory(string path) =>
        (System.IO.Path.GetDirectoryName(path) ?? "").Replace('\\', '/');

    public static string MakeCanonicalName(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name.ToLowerInvariant()
            .Replace('/', '_')
            .Replace('.', '_')
            .Replace(' ', '_')
            .Replace('-', '_');
    }

    public static Document Get(int index) => _documents[index];

    public static string? GetUniquePath(string sourcePath)
    {
        var parentPath = GetDirectory(sourcePath);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        var ext = System.IO.Path.GetExtension(sourcePath);
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
            var candidate = CombinePath(parentPath, $"{canonicalBase}_{i}{ext}");

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
        var newName = System.IO.Path.GetFileNameWithoutExtension(newPath);

        var directory = GetDirectory(newPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.Copy(source.Path, newPath);

        // Copy companion files
        var newStem = System.IO.Path.GetFileNameWithoutExtension(newPath);
        var newDir = GetDirectory(newPath);
        foreach (var companionPath in GetCompanionFiles(source))
        {
            var companionExt = System.IO.Path.GetExtension(companionPath);
            File.Copy(companionPath, CombinePath(newDir, newStem + companionExt));
        }

        var metaPath = source.Path + ".meta";
        if (File.Exists(metaPath))
            File.Copy(metaPath, newPath + ".meta");

        QueueExport(newPath);
        UpdateExports();

        var doc = Find(source.Def.Type, newName);
        if (doc == null) return null;

        // The export already loads, post-loads, and fires DocumentAdded via OnExported.
        // Only do it manually if the export didn't get to it (e.g. skipped by timestamp).
        if (!doc.PostLoaded)
        {
            doc.Loaded = true;
            doc.Load();
            doc.LoadMetadata();
            doc.PostLoad();
            doc.PostLoaded = true;
            DocumentAdded?.Invoke(doc);
        }

        // Re-export now that the document is loaded and atlas-assigned
        if (doc.ShouldExport)
        {
            QueueExport(doc, force: true);
            UpdateExports();
        }

        // Ensure the atlas is rebuilt with the duplicate's rasterized pixels
        if (doc is SpriteDocument sprite)
            AtlasManager.UpdateSource(sprite);

        doc.Position = source.Position;

        return doc;
    }

    // --- Export pipeline (moved from Importer.cs) ---

    public static void QueueExport(Document? doc, bool force = false)
    {
        if (doc == null) return;
        if (!doc.ShouldExport) return;
        if (doc.IsQueuedForExport) return;

        if (!File.Exists(doc.Path)) return;

        var targetPath = GetTargetPath(doc);
        var metaPath = doc.Path + ".meta";

        if (!force)
        {
            bool targetExists = File.Exists(targetPath);
            if (targetExists)
            {
                // Force export if the binary's version doesn't match the engine's expected version
                var assetDef = Asset.GetDef(doc.Def.Type);
                if (assetDef is { Version: > 0 } && ReadAssetVersion(targetPath) != assetDef.Version)
                    force = true;

                if (!force)
                {
                    var targetTime = File.GetLastWriteTimeUtc(targetPath);
                    var sourceTime = File.GetLastWriteTimeUtc(doc.Path);
                    var metaTime = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath) : DateTime.MinValue;

                    if (sourceTime <= targetTime && metaTime <= targetTime)
                        return;
                }
            }
        }

        doc.IsQueuedForExport = true;
        _exportQueue.Enqueue(doc);
    }

    public static void QueueExport(string path)
    {
        var def = ResolveDef(path);
        if (def == null)
        {
            Log.Info($"[hotload] QueueExport: no def for {path}");
            return;
        }

        var name = MakeCanonicalName(path);
        var doc = Find(def.Type, name);
        if (doc == null)
        {
            doc = Create(path);
            Log.Info($"[hotload] QueueExport: created doc {def.Type}/{name} (watching={_watching}, created={doc != null})");
            if (doc != null && _watching)
                DocumentAdded?.Invoke(doc);
        }
        else
        {
            Log.Info($"[hotload] QueueExport: existing doc {def.Type}/{name}");
        }
        QueueExport(doc);
    }

    private static ushort ReadAssetVersion(string path)
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

    private static bool IsFileReady(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return stream.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Export(Document doc)
    {
        try
        {
            if (!File.Exists(doc.Path))
                return;

            if (_watching && !IsFileReady(doc.Path))
            {
                _exportDeferred.Enqueue(doc);
                return;
            }

            var metaPath = doc.Path + ".meta";
            var meta = PropertySet.LoadFile(metaPath) ?? new PropertySet();
            var targetDir = GetTargetPath(doc);

            Directory.CreateDirectory(GetDirectory(targetDir));

            doc.Export(targetDir, meta);

            Log.Info($"Exported {(Asset.GetDef(doc.Def.Type)?.Name ?? doc.Def.Type.ToString()).ToLowerInvariant()}/{doc.Name}");
            OnExported?.Invoke(doc);
            OnDocumentExported(doc);
            doc.SilentExport = false;
            AssetManifest.IsModified = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to export '{doc.Name}': {ex.Message}");
        }
    }

    private static void HandleFileChange(string path)
    {
        if (System.IO.Path.GetExtension(path) == ".meta")
        {
            Log.Info($"[hotload] HandleFileChange meta -> {path[..^5]}");
            _watcherQueue.Enqueue(path[..^5]);
            return;
        }

        if (IsAuxiliaryFile(path))
        {
            var parentPath = GetAuxiliaryParentPath(path);
            Log.Info($"[hotload] HandleFileChange aux {path} -> parent {parentPath ?? "<null>"}");
            if (parentPath != null)
                _watcherQueue.Enqueue(parentPath);
            return;
        }

        Log.Info($"[hotload] HandleFileChange primary: {path}");
        _watcherQueue.Enqueue(path);
        _reloadQueue.Enqueue(path);
    }

    public static void NotifyFilePulled(string relativePath) =>
        HandleFileChange(relativePath.Replace('\\', '/'));

    public static void UpdateExports()
    {
        if (_reloadQueue.Count > 0 || _watcherQueue.Count > 0)
            Log.Info($"[hotload] UpdateExports drain: reload={_reloadQueue.Count} watcher={_watcherQueue.Count}");

        while (_reloadQueue.TryDequeue(out var path))
        {
            var def = ResolveDef(path);
            if (def != null)
            {
                var name = MakeCanonicalName(path);
                var doc = Find(def.Type, name);
                if (doc is { Loaded: true } && !doc.SilentExport)
                    doc.Reload();
            }
        }

        while (_watcherQueue.TryDequeue(out var path))
            QueueExport(path);

        while (_exportDeferred.Count > 0)
            _exportQueue.Enqueue(_exportDeferred.Dequeue());

        while (_exportQueue.Count > 0)
        {
            var doc = _exportQueue.Dequeue();
            doc.IsQueuedForExport = false;
            if (doc.IsDisposed) continue;
            Export(doc);
        }

        AtlasManager.ExportIfNeeded();
    }

    private static void StartWatching(string path)
    {
        if (OperatingSystem.IsIOS())
            return;

        Log.Info($"[hotload] StartWatching: {path}");
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        watcher.Changed += OnWatcherEvent;
        watcher.Created += OnWatcherEvent;
        watcher.Renamed += OnWatcherEvent;
        watcher.Error += OnWatcherError;
        _watchers.Add(watcher);
    }

    private static void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Log.Warning($"[hotload] watcher error: {e.GetException().Message} — rescanning all source paths");
        if (sender is FileSystemWatcher w)
            RescanDirectory(w.Path);
    }

    private static void RescanDirectory(string absoluteDir)
    {
        if (!Directory.Exists(absoluteDir))
            return;
        foreach (var file in Directory.EnumerateFiles(absoluteDir, "*", SearchOption.AllDirectories))
            HandleFileChange(ToRelative(file));
    }

    private static void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private static string ToRelative(string absolutePath)
    {
        if (absolutePath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
            return absolutePath[Path.Length..].Replace('\\', '/').TrimStart('/');
        return absolutePath.Replace('\\', '/');
    }

    private static void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        Log.Info($"[hotload] watcher {e.ChangeType}: {e.FullPath}");
        if (Directory.Exists(e.FullPath))
        {
            Log.Info($"[hotload] directory event, rescanning {e.FullPath}");
            RescanDirectory(e.FullPath);
            return;
        }
        HandleFileChange(ToRelative(e.FullPath));
    }
}
