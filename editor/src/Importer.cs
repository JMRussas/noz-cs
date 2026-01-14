//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz.editor;

public static class Importer
{
    private static readonly HashSet<Document> _pendingDocuments = [];
    private static readonly List<Task> _activeTasks = [];
    private static readonly Lock _lock = new();
    private static FileSystemWatcher? _watcher;
    private static PropertySet? _config;
    private static bool _running;

    public static event Action<Document>? OnImported;

    public static void Init(bool clean = false)
    {
        _config = new PropertySet();
        _running = true;

        if (clean)
            Log.Info("Clean build requested, reimporting all assets...");

        foreach (var doc in DocumentManager.Documents)
            QueueImport(doc, clean);

        WaitForAllTasks();

        foreach (var sourcePath in DocumentManager.SourcePaths)
        {
            if (Directory.Exists(sourcePath))
                StartWatching(sourcePath);
        }
    }

    public static void Shutdown()
    {
        _running = false;
        _watcher?.Dispose();
        _watcher = null;

        WaitForAllTasks();

        lock (_lock)
        {
            _pendingDocuments.Clear();
            _activeTasks.Clear();
        }
    }

    public static void QueueImport(Document doc, bool force = false)
    {
        if (!File.Exists(doc.Path))
            return;

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

        lock (_lock)
        {
            if (!_pendingDocuments.Add(doc))
                return;

            _activeTasks.Add(Task.Run(() => ExecuteImport(doc)));
        }
    }

    public static void QueueImport(string path)
    {
        var ext = Path.GetExtension(path);
        var def = DocumentManager.GetDef(ext);
        if (def == null)
            return;

        var name = DocumentManager.MakeCanonicalName(path);
        var doc = DocumentManager.FindDocument(def.Type, name);

        if (doc == null)
        {
            doc = DocumentManager.CreateDocument(path);
            if (doc == null)
                return;
            DocumentManager.LoadMetadata(doc);
        }

        QueueImport(doc);
    }

    public static void WaitForAllTasks()
    {
        while (true)
        {
            Task[] tasks;
            lock (_lock)
            {
                _activeTasks.RemoveAll(t => t.IsCompleted);
                if (_activeTasks.Count == 0)
                    break;
                tasks = _activeTasks.ToArray();
            }
            Task.WaitAll(tasks);
        }
    }

    private static void ExecuteImport(Document doc)
    {
        try
        {
            if (!File.Exists(doc.Path))
                return;

            var metaPath = doc.Path + ".meta";
            var meta = PropertySet.LoadFile(metaPath) ?? new PropertySet();
            var targetDir = DocumentManager.GetTargetPath(doc);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir) ?? "");

            doc.Import(targetDir, _config ?? new PropertySet(), meta);

            Log.Info($"Imported {doc.Def.Type.ToString().ToLowerInvariant()}/{doc.Name}");
            OnImported?.Invoke(doc);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to import '{doc.Name}': {ex.Message}");
        }
        finally
        {
            RemovePending(doc);
        }
    }

    private static void RemovePending(Document doc)
    {
        lock (_lock)
        {
            _pendingDocuments.Remove(doc);
        }
    }

    private static void StartWatching(string path)
    {
        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_running)
            return;

        HandleFileChange(e.FullPath);
    }

    private static void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!_running)
            return;

        HandleFileChange(e.FullPath);
    }

    private static void HandleFileChange(string path)
    {
        var filename = Path.GetFileName(path);
        if (filename.EndsWith(".d.luau") || filename.EndsWith(".d.lua"))
            return;

        QueueImport(Path.GetExtension(path) == ".meta" ? path[..^5] : path);
    }
}
