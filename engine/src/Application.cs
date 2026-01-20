//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ.Platform;

namespace NoZ;

public static class Application
{
    private static bool _running;
    private static bool _assetTypesRegistered = false;

    private static IApplicationVtable _vtable = null!;

    public static ApplicationConfig Config { get; private set; } = null!;
    public static IPlatform Platform { get; private set; } = null!;
    public static IAudioDriver AudioDriverBackend { get; private set; } = null!;
    public static Vector2 WindowSize => Platform.WindowSize;
    public static string AssetPath { get; private set; } = null!;

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

        _vtable = config.Vtable ?? throw new ArgumentNullException(nameof(config.Vtable),
            "App must be provided. Implement IApplication in your game.");

        AssetPath = Path.Combine(Directory.GetCurrentDirectory(), config.AssetPath);

        // Initialize platform
        Platform.Init(new PlatformConfig
        {
            Title = config.Title,
            Width = config.Width,
            Height = config.Height,
            VSync = config.VSync,
            Resizable = config.Resizable,
            IconPath = config.IconPath
        });

        // Subscribe to platform events
        Platform.OnEvent += Input.ProcessEvent;
        Platform.SetResizeCallback(RenderFrame);

        // Initialize subsystems
        Time.Init();
        Input.Init();
        Audio.Init(AudioDriverBackend);
        Graphics.Init(Config);

        // Register asset types and load assets
        RegisterAssetTypes();
        _vtable.LoadAssets();
        Graphics.ResolveAssets();

        TextRender.Init(config);
        UI.Init(config.UI);
        
        _running = true;
    }

    internal static void RegisterAssetTypes()
    {
        if (_assetTypesRegistered) return;
        _assetTypesRegistered = true;
        
        Texture.RegisterDef();
        Atlas.RegisterDef();
        Sprite.RegisterDef();
        Sound.RegisterDef();
        Shader.RegisterDef();
        Font.RegisterDef();
    }

    public static void Run()
    {
        while (_running)
        {
            Time.Update();
            Input.BeginFrame();

            if (!Platform.PollEvents())
            {
                _running = false;
                continue;
            }

            Input.Update();

            Graphics.BeginFrame();
            _vtable.Update();
            Graphics.BeginUI();
            UI.Begin();
            _vtable.UpdateUI();
            UI.End();
            Graphics.EndFrame();

            Platform.SwapBuffers();
        }
    }

    public static void Shutdown()
    {
        _vtable.UnloadAssets();

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
        Time.Update();
        Input.BeginFrame();
        Input.Update();

        Graphics.BeginFrame();
        _vtable.Update();
        Graphics.BeginUI();
        UI.Begin();
        _vtable.UpdateUI();
        UI.End();
        Graphics.EndFrame();
    }
}
