//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;

namespace NoZ.Editor;

public static class Importer
{
    private static readonly Queue<Document> _queue = [];
    private static readonly Queue<Document> _deferred = [];
    private static readonly List<FileSystemWatcher> _watchers = [];
    private static readonly ConcurrentQueue<string> _watcherQueue = [];
    private static bool _watching;

    public static event Action<Document>? OnImported;

    public static void Init(bool clean = false)
    {
        if (clean)
            Log.Info("Clean build requested, reimporting all assets...");

        foreach (var doc in DocumentManager.Documents)
            Queue(doc, clean);

        Update();

        foreach (var sourcePath in DocumentManager.SourcePaths)
            if (Directory.Exists(sourcePath))
                StartWatching(sourcePath);

        _watching = true;
    }

    public static void Shutdown()
    {        
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }

    public static void Queue(Document? doc, bool force = false)
    {
        if (doc == null) return;
        if (doc.IsQueuedForImport) return;
        if (!File.Exists(doc.Path)) return;

        var targetPath = DocumentManager.GetTargetPath(doc);
        var metaPath = doc.Path + ".meta";

        if (!force)
        {
            bool targetExists = File.Exists(targetPath);
            if (targetExists)
            {
                var targetTime = File.GetLastWriteTimeUtc(targetPath);
                var sourceTime = File.GetLastWriteTimeUtc(doc.Path);
                var metaTime = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath) : DateTime.MinValue;

                if (sourceTime <= targetTime && metaTime <= targetTime)
                    return;
            }
        }

        doc.IsQueuedForImport = true;
        _queue.Enqueue(doc);
    }

    public static void Queue(string path)
    {
        var ext = Path.GetExtension(path);
        var def = DocumentManager.GetDef(ext);
        if (def == null)
            return;

        var name = DocumentManager.MakeCanonicalName(path);
        Queue(DocumentManager.Find(def.Type, name) ?? DocumentManager.Create(path));
    }

    private static bool IsFileReady(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Import(Document doc)
    {
        try
        {
            if (!File.Exists(doc.Path))
                return;

            if (_watching && !IsFileReady(doc.Path))
            {
                _deferred.Enqueue(doc);
                return;
            }

            var metaPath = doc.Path + ".meta";
            var meta = PropertySet.LoadFile(metaPath) ?? new PropertySet();
            var targetDir = DocumentManager.GetTargetPath(doc);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir) ?? "");

            doc.Import(targetDir,  meta);

            Log.Info($"Imported {doc.Def.Type.ToString().ToLowerInvariant()}/{doc.Name}");
            OnImported?.Invoke(doc);
            AssetManifest.IsModified = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to import '{doc.Name}': {ex.Message}");
        }
    }

    private static void StartWatching(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += OnFileRenamed;

        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e) =>
        HandleFileChange(e.FullPath);

    private static void OnFileRenamed(object sender, RenamedEventArgs e) =>
        HandleFileChange(e.FullPath);

    private static void HandleFileChange(string path) =>
        _watcherQueue.Enqueue(Path.GetExtension(path) == ".meta" ? path[..^5] : path);

    public static void Update()
    {
        while (_watcherQueue.TryDequeue(out var path))
            Queue(path);

        while (_deferred.Count > 0)
            _queue.Enqueue(_deferred.Dequeue());

        while (_queue.Count > 0)
        {
            var doc = _queue.Dequeue();
            doc.IsQueuedForImport = false;
            if (doc.IsDisposed) continue;
            Import(doc);
        }
    }
}
