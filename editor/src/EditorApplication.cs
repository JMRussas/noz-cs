//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using NoZ.Platform;
using NoZ.Platform.WebGPU;

namespace NoZ.Editor;

public class EditorApplicationConfig
{
    public string Title { get; init; } = "NoZ Editor";
    public string? IconPath { get; init; }
    public string? EditorPath { get; init; }
    public string? ProjectPath { get; init; }
    public Action? RegisterAssetTypes { get; init; }
    public Action? RegisterDocumentTypes { get; init; }
    public Action? Update { get; init; }
    public Action? UpdateUI { get; init; }
    public Action<PropertySet>? LoadUserSettings { get; init; }
    public Action<PropertySet>? SaveUserSettings { get; init; }
    public Action<List<Command>>? RegisterCommands { get; init; }
    public Assembly? ResourceAssembly { get; init; }
    public bool IsTablet { get; init; }
}

public static partial class EditorApplication
{
    private const float UIScaleMin = 0.5f;
    private const float UIScaleMax = 3f;
    private const float UIScaleStep = 0.1f;

    private class EditorApplicationInstance : IApplication
    {
        public void Update() => EditorApplication.Update();
        public void LateUpdate() => EditorApplication.LateUpdate();
        public void LoadAssets() => EditorAssets.LoadAssets();
        public void UnloadAssets() => EditorAssets.UnloadAssets();
        public void ReloadAssets() => EditorAssets.ReloadAssets();
        public void Shutdown() => EditorApplication.Shutdown();
        
        public void LoadConfig(ApplicationConfig config)
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NoZEditor",
                "app.cfg");

            var props = PropertySet.LoadFile(path);
            if (props == null)
            {
                UI.UserScale = EditorApplication.DefaultUIScale;
                return;
            }

            UI.UserScale = props.GetFloat("ui", "scale", EditorApplication.DefaultUIScale);

            var w = props.GetInt("window", "width", 0);
            var h = props.GetInt("window", "height", 0);
            if (w > 100 && h > 100)
            {
                config.Width = w;
                config.Height = h;
            }

            var x = props.GetInt("window", "x", int.MinValue);
            var y = props.GetInt("window", "y", int.MinValue);
            if (x != int.MinValue && y != int.MinValue)
            {
                config.X = x;
                config.Y = y;
            }
        }
    }

    private static readonly Queue<Action> _mainThreadQueue = new();


    internal static EditorApplicationConfig AppConfig { get; private set; } = null!;

    public static EditorConfig Config { get; private set; } = null!;
    public static string EditorPath { get; private set; } = null!;
    public static IProjectSync? Sync { get; private set; }

    public static float DefaultUIScale => Application.IsTablet ? 1.6f : 1.2f;

    public static void LoadUserSettings(PropertySet props)
    {                
        if (Project.IsInitialized)
        {
            CollectionManager.LoadUserSettings(props);
            Workspace.LoadUserSettings(props);
            VectorSpriteEditor.LoadUserSettings(props);
        }

        AppConfig.LoadUserSettings?.Invoke(props);
    }

    public static void SaveUserSettings(PropertySet props)
    {       
        AppConfig.SaveUserSettings?.Invoke(props);
    }

    public static void Run(EditorApplicationConfig config, string[] args)
    {
        Init(config);

        Application.Init(new ApplicationConfig
        {
            Title = config.Title,
            Width = 1920,
            Height = 1080,
            HotReload = false,
            IconPath = config.IconPath ?? "res/windows/nozed.png",
            Platform = new SDLPlatform(),
            AudioBackend = new SDLAudioDriver(),
            Vtable = new EditorApplicationInstance(),
            AssetPath = Path.Combine(EditorPath, "library"),
            IsTablet = config.IsTablet,
            ResourceAssembly = config.ResourceAssembly,
            UI = new UIConfig()
            {
                DefaultFont = EditorAssets.Names.Seguisb,
                ScaleMode = UIScaleMode.ConstantPixelSize,
            },
            Graphics = new GraphicsConfig
            {
                Driver = new WebGPUGraphicsDriver(),
                PixelsPerUnit = Config?.PixelsPerUnit ?? 256,
                Vsync = true,
                HDR = true
            }
        });
        
        if (!config.IsTablet)
            LoadProject(config.ProjectPath!);
        else
            ProjectLoader.Init(config.ProjectPath!);

        Touch.SimulateMouse = Application.IsTablet;

        Application.Run();
    }


    private static void Init(EditorApplicationConfig config)
    {
        AppConfig = config;
        EditorPath = config.EditorPath!;

        Application.RegisterAssetTypes();
        config.RegisterAssetTypes?.Invoke();        
    }

    public static bool LoadProject(IProjectSync sync)
    {        
        if (!LoadProject(sync.ProjectPath))
            return false;

        Sync = sync;

        return true;
    }

    public static bool LoadProject(string projectPath)
    {
        Config = EditorConfig.Load(Path.Combine(projectPath, "editor.cfg"))!;
        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return false;
        }

        CollectionManager.Init(Config);
        Project.Init(projectPath, Config);
        PaletteManager.Init();
        Project.LoadAll();
        PaletteManager.DiscoverPalettes();
        AtlasManager.Init();
        Project.InitExports();
        AssetManifest.Generate();

        EditorStyle.Init();
        ConfirmDialog.Init();
        EditorCursor.Init();
        
        Project.PostLoad();
        AtlasManager.Update();
        VfxSystem.Shader = EditorAssets.Shaders.Sprite;
        Workspace.Init();
        UserSettings.Load();

        Project.SaveAll();

        return true;
    }

    // Headless equivalent of LoadProject for CLI asset builds. Mirrors the same
    // ordering but skips everything that needs a window, GPU or user settings.
    // Callers are responsible for Project.Shutdown.
    public static bool LoadProjectHeadless(string projectPath, string editorPath)
    {
        EditorPath = editorPath;
        Application.RegisterAssetTypes();

        Config = EditorConfig.Load(Path.Combine(projectPath, "editor.cfg"))!;
        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return false;
        }

        CollectionManager.Init(Config);
        Project.Init(projectPath, Config);
        PaletteManager.Init();
        Project.LoadAll();
        PaletteManager.DiscoverPalettes();
        AtlasManager.Init();
        Project.InitExports();
        AssetManifest.Generate(force: true);
        Project.PostLoad();

        return true;
    }

    public static void Shutdown()
    {
        SaveConfig();
        UserSettings.Save();
        Project.SaveAll();

        Workspace.Shutdown();
        ConfirmDialog.Shutdown();
        EditorStyle.Shutdown();
        CollectionManager.Shutdown();
        PaletteManager.Shutdown();
        Project.Shutdown();

        Sync?.Dispose();
        Sync = null;
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

        if (!Application.HasFocus && !(Workspace.ActiveEditor is { RunInBackground: true}))
            Application.PowerMode = PowerMode.Conserve;

        if (Application.IsTablet && !Project.IsInitialized)
            ProjectLoader.UpdateUI();
        else if (Project.IsInitialized)
            UpdateProject();
        else
            UpdateProjectError();
    }

    private static void UpdateProjectError()
    {
        UI.Text("Failed to editor.cfg", EditorStyle.Text.Primary with {Align = Align.Center});
    }

    private static void UpdateProject()
    {
        Project.UpdateExports();
        ConfirmDialog.Update();
        CommandPalette.Update();
        AssetPalette.Update();
        Workspace.Update();
        AppConfig.Update?.Invoke();

        Workspace.UpdateUI();
        CommandPalette.UpdateUI();
        AssetPalette.UpdateUI();
        ConfirmDialog.UpdateUI();
        AppConfig.UpdateUI?.Invoke();        
    }

    internal static void LateUpdate()
    {
        Workspace.LateUpdate();
    }

    public static void IncreaseUIScale() =>
        UI.UserScale = Math.Clamp(UI.UserScale + UIScaleStep, UIScaleMin, UIScaleMax);

    public static void DecreaseUIScale() =>
        UI.UserScale = Math.Clamp(UI.UserScale - UIScaleStep, UIScaleMin, UIScaleMax);

    public static void ResetUIScale() =>
        UI.UserScale = DefaultUIScale;

    private static void SaveConfig()
    {
        var winSize = Application.WindowSize;
        var winPos = Application.WindowPosition;
        var props = new PropertySet();
        props.SetInt("window", "width", winSize.X);
        props.SetInt("window", "height", winSize.Y);
        props.SetInt("window", "x", winPos.X);
        props.SetInt("window", "y", winPos.Y);        
        props.SetFloat("ui", "scale", UI.UserScale);

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NoZEditor",
            "app.cfg");

        props.Save(path);        
    }
}
