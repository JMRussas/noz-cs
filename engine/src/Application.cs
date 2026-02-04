//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Reflection;
using NoZ.Platform;

namespace NoZ;

public static class Application
{
    private static bool _running;
    private static bool _assetTypesRegistered = false;

    private static IApplication _instance = null!;

    public static ApplicationConfig Config { get; private set; } = null!;
    public static IPlatform Platform { get; private set; } = null!;
    public static IAudioDriver AudioDriverBackend { get; private set; } = null!;
    public static Assembly? ResourceAssembly { get; private set; }
    public static Vector2Int WindowSize => Platform.WindowSize;
    public static Vector2Int WindowPosition => Platform.WindowPosition;
    public static string AssetPath { get; private set; } = null!;

    public static void SetWindowSize(int width, int height) => Platform.SetWindowSize(width, height);
    public static void SetWindowPosition(int x, int y) => Platform.SetWindowPosition(x, y);

    public static Stream? LoadPersistentData(string name, string? appName = null) => Platform.LoadPersistentData(name, appName);
    public static void SavePersistentData(string name, Stream data, string? appName = null) => Platform.SavePersistentData(name, data, appName);

    public static void Init(ApplicationConfig config)
    {
        Config = config;

        Platform = config.Platform ?? throw new ArgumentNullException(nameof(config.Platform),
            "Platform must be provided. Use SDLPlatform for desktop or WebPlatform for web.");

        if (config.Graphics == null)
            throw new ArgumentNullException(nameof(config.Graphics),
                "RenderConfig must be provided");
        
        AudioDriverBackend = config.AudioBackend ?? throw new ArgumentNullException(nameof(config.AudioBackend),
            "AudioBackend must be provided. Use SDLAudio for desktop or WebAudio for web.");

        _instance = config.Vtable ?? throw new ArgumentNullException(nameof(config.Vtable),
            "App must be provided. Implement IApplication in your game.");

        AssetPath = config.AssetPath ?? Path.Combine(Directory.GetCurrentDirectory(), "library");
        ResourceAssembly = config.ResourceAssembly;

        // Allow game to load config (window size/position) before platform init
        _instance.LoadConfig(config);

        // Initialize platform
        Platform.Init(new PlatformConfig
        {
            Title = config.Title,
            Width = config.Width,
            Height = config.Height,
            MinWidth = config.MinWidth,
            MinHeight = config.MinHeight,
            X = config.X,
            Y = config.Y,
            VSync = config.VSync,
            Resizable = config.Resizable,
            IconPath = config.IconPath
        });

        // Subscribe to platform events
        Platform.OnEvent += Input.ProcessEvent;

        // Initialize subsystems
        Time.Init();
        Input.Init();
        Audio.Init(AudioDriverBackend);
        Graphics.Init(Config);

        // Register asset types and load assets
        RegisterAssetTypes();
        _instance.LoadAssets();
        Graphics.ResolveAssets();

        TextRender.Init(config);
        UI.Init(config.UI);

        // Set resize callback after all subsystems are initialized to avoid early resize events
        Platform.SetResizeCallback(RenderFrame);

        _running = true;
    }

    internal static void RegisterAssetTypes()
    {
        if (_assetTypesRegistered) return;
        _assetTypesRegistered = true;

        Animation.RegisterDef();
        Skeleton.RegisterDef();
        Texture.RegisterDef();
        Atlas.RegisterDef();
        Sprite.RegisterDef();
        Sound.RegisterDef();
        Shader.RegisterDef();
        Font.RegisterDef();
    }

    public static void Run()
    {
        while (RunFrame())
        {
            Platform.SwapBuffers();
        }
    }

    public static bool RunFrame()
    {
        if (!_running)
            return false;

        Time.Update();
        Input.BeginFrame();

        if (!Platform.PollEvents())
        {
            _running = false;
            return false;
        }

        Input.Update();

        if (!Graphics.BeginFrame())
            return _running;

        _instance.Update();
        Graphics.BeginUI();
        UI.Begin();
        _instance.UpdateUI();
        UI.End();
        _instance.LateUpdate();
        Graphics.EndFrame();

        return _running;
    }

    public static void Shutdown()
    {
        _instance.SaveConfig();
        _instance.UnloadAssets();

        UI.Shutdown();
        TextRender.Shutdown();
        Graphics.Shutdown();
        Audio.Shutdown();
        Input.Shutdown();
        Time.Shutdown();

        Platform.OnEvent -= Input.ProcessEvent;
        Platform.SetResizeCallback(null);
        Platform.Shutdown();

        _running = false;
    }

    public static void Quit()
    {
        _running = false;
    }

    private static void RenderFrame()
    {
        // Don't call Time.Update() during resize - this is an "extra" render frame
        // that shouldn't increment the frame count or affect frame-gap detection
        // in UI canvas state management.
        Input.BeginFrame();
        Input.Update();

        if (!Graphics.BeginFrame())
            return;

        _instance.Update();
        Graphics.BeginUI();
        UI.Begin();
        _instance.UpdateUI();
        UI.End();
        Graphics.EndFrame();
    }
}
