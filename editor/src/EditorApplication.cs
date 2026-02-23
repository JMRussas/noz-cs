//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NoZ.Platform;
using NoZ.Platform.WebGPU;

namespace NoZ.Editor;

public class EditorApplicationConfig
{
    public string Title { get; init; } = "NoZ Editor";
    public string? IconPath { get; init; }
    public Action? RegisterAssetTypes { get; init; }
    public Action? RegisterDocumentTypes { get; init; }
}

internal class EditorApplicationInstance : IApplication
{
    public void Update() => EditorApplication.Update();
    public void UpdateUI() => EditorApplication.UpdateUI();
    public void LateUpdate() => EditorApplication.LateUpdate();

    public void LoadAssets()
    {
        Importer.Update();
        EditorAssets.LoadAssets();
        EditorApplication.PostLoad();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static partial class EditorApplication
{
    private static readonly Queue<Action> _mainThreadQueue = new();

    public static EditorConfig Config { get; private set; } = null!;
    public static string OutputPath { get; private set; } = null!;
    public static string EditorPath { get; private set; } = null!;
    public static string ProjectPath { get; private set; } = null!;

    public static void Run(EditorApplicationConfig config, string[] args)
    {
        var initProject = false;
        var importOnly = false;
        string? initProjectName = null;
        var clean = false;
        string? projectArg = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--init")
            {
                initProject = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    initProjectName = args[++i];
            }
            else if (args[i] == "--import")
                importOnly = true;
            else if (args[i] == "--project" && i + 1 < args.Length)
                projectArg = args[++i];
            else if (args[i] == "--clean")
                clean = true;
        }

        // For CLI modes on Windows, attach to parent console so Console.WriteLine output is visible
        // (WinExe has no console by default; Linux/Mac don't need this)
        if ((importOnly || initProject) && OperatingSystem.IsWindows())
            AttachConsole(-1);

        // Find editor path by walking up from app base directory
        // Look for directory with BOTH library/ AND NoZ.Editor.csproj
        var editorPath = AppContext.BaseDirectory;
        while (editorPath != null)
        {
            if (Directory.Exists(Path.Combine(editorPath, "library")) &&
                File.Exists(Path.Combine(editorPath, "NoZ.Editor.csproj")))
                break;

            editorPath = Path.GetDirectoryName(editorPath);
        }

        if (editorPath == null)
        {
            if (initProject)
            {
                // For --init mode, we don't need library/ to exist yet
                // Walk up from bin/Debug/net10.0 looking for NoZ.Editor.csproj
                editorPath = AppContext.BaseDirectory;
                for (var i = 0; i < 6; i++)
                {
                    var parent = Path.GetDirectoryName(editorPath);
                    if (parent == null) break;
                    editorPath = parent;
                    if (File.Exists(Path.Combine(editorPath, "NoZ.Editor.csproj")))
                        break;
                }

                Directory.CreateDirectory(Path.Combine(editorPath, "library"));
            }
            else
            {
                Log.Error("Could not find editor directory. Expected to find library/ folder and NoZ.Editor.csproj");
                return;
            }
        }

        var projectPath = projectArg ?? editorPath;

        if (projectPath.StartsWith('.'))
        {
            projectPath = Path.Combine(Directory.GetCurrentDirectory(), projectPath);
            projectPath = Path.GetFullPath(projectPath);
        }

        // Handle project initialization before normal startup
        if (initProject)
        {
            try
            {
                var projectName = initProjectName ?? Path.GetFileName(Path.GetFullPath(projectPath));
                ProjectInitializer.Initialize(projectPath, projectName, editorPath);

                Log.Info("");
                Log.Info("Importing assets...");
                Init(editorPath, projectPath, false, config);

                Log.Info($"Project '{projectName}' initialized successfully at {projectPath}");
                Log.Info("");
                Log.Info("Next steps:");
                Log.Info("  1. cd " + projectPath);
                Log.Info("  2. git init");
                Log.Info("  3. git submodule add https://github.com/nozgames/noz-cs noz");
                Log.Info("  4. git submodule update --init --recursive");
                Log.Info("  5. dotnet restore");
                Log.Info("  6. dotnet build");
                Log.Info("");
                Log.Info("To open in the editor:");
                Log.Info("  noz --project " + projectPath);
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize project: {ex.Message}");
                Environment.Exit(1);
            }
        }

        if (!File.Exists(Path.Combine(projectPath, "editor.cfg")))
        {
            Log.Info("Searching for project path...");
            projectPath = Path.GetDirectoryName(projectPath)!;
            while (projectPath != null)
            {
                Log.Info($"Trying {projectPath}");
                if (File.Exists(Path.Combine(projectPath, "editor.cfg")))
                    break;

                projectPath = Path.GetDirectoryName(projectPath)!;
            }

            if (projectPath == null)
            {
                Log.Error("Could not find project path (no 'editor.cfg' found)");
                return;
            }
        }

        Log.Info($"Editor Path: {editorPath}");
        Log.Info($"Project Path: {projectPath}");

        Init(editorPath, projectPath, clean, config);

        // Import-only mode: import assets and exit without launching GUI
        if (importOnly)
        {
            Log.Info("Import complete.");
            Importer.Shutdown();
            return;
        }

        Application.Init(new ApplicationConfig
        {
            Title = config.Title,
            Width = 1600,
            Height = 900,
            IconPath = config.IconPath ?? "res/windows/nozed.png",
            Platform = new SDLPlatform(),
            AudioBackend = new SDLAudioDriver(),
            Vtable = new EditorApplicationInstance(),
            AssetPath = Path.Combine(EditorPath, "library"),

            UI = new UIConfig()
            {
                DefaultFont = EditorAssets.Names.Seguisb,
                ScaleMode = UIScaleMode.ConstantPixelSize,
            },
            Graphics = new GraphicsConfig
            {
                Driver = new WebGPUGraphicsDriver(),
                PixelsPerUnit = Config.PixelsPerUnit
            }
        });

        Application.Run();

        Shutdown();
        Application.Shutdown();
    }

    private static void Init(string editorPath, string projectPath, bool clean, EditorApplicationConfig config)
    {
        EditorPath = editorPath;
        ProjectPath = projectPath;

        Config = EditorConfig.Load(Path.Combine(ProjectPath, "editor.cfg"))!;
        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        Application.RegisterAssetTypes();
        config.RegisterAssetTypes?.Invoke();

        AtlasDocument.RegisterDef();
        TextureDocument.RegisterDef();
        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();
        SpriteDocument.RegisterDef();
        FontDocument.RegisterDef();
        SkeletonDocument.RegisterDef();
        AnimationDocument.RegisterDef();
        VfxDocument.RegisterDef();
        BinDocument.RegisterDef();

        config.RegisterDocumentTypes?.Invoke();

        OutputPath = System.IO.Path.Combine(ProjectPath, Config.OutputPath);

        Log.Info($"OutputPath: {OutputPath}");

        CollectionManager.Init(Config);
        DocumentManager.Init(Config.SourcePaths, Config.OutputPath);
        PaletteManager.Init(Config);
        DocumentManager.LoadAll();
        AtlasManager.Init();
        Importer.Init(clean);
        AssetManifest.Generate();
    }

    internal static void PostLoad()
    {
        if (Config == null)
            return;

        DocumentManager.PostLoad();
        EditorStyle.Init();
        PopupMenu.Init();
        ConfirmDialog.Init();
        Notifications.Init();
        VfxSystem.Shader = EditorAssets.Shaders.Texture;
        Workspace.Init();
        UserSettings.Load();

        DocumentManager.SaveAll();
    }

    private static void Shutdown()
    {
        UserSettings.Save();
        DocumentManager.SaveAll();

        Workspace.Shutdown();
        Notifications.Shutdown();
        ConfirmDialog.Shutdown();
        PopupMenu.Shutdown();
        EditorStyle.Shutdown();
        CollectionManager.Shutdown();
        PaletteManager.Shutdown();
        Importer.Shutdown();
        DocumentManager.Shutdown();
        Config = null!;
    }

    public static void RunOnMainThread(Action action)
    {
        lock (_mainThreadQueue)
            _mainThreadQueue.Enqueue(action);
    }

    internal static void Update()
    {
        lock (_mainThreadQueue)
            while (_mainThreadQueue.Count > 0)
                _mainThreadQueue.Dequeue().Invoke();

        Importer.Update();
        ConfirmDialog.Update();
        CommandPalette.Update();
        PopupMenu.Update();
        Notifications.Update();
        Workspace.Update();
    }

    internal static void UpdateUI()
    {
        Workspace.UpdateUI();
        Notifications.UpdateUI();
        PopupMenu.UpdateUI();
        CommandPalette.UpdateUI();
        ConfirmDialog.UpdateUI();
    }

    internal static void LateUpdate()
    {
        Workspace.LateUpdate();
    }

    [LibraryImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial void AttachConsole(int dwProcessId);
}
