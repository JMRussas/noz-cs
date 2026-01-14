//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using noz.Platform;

namespace noz;

public static class Application
{
    private static bool _running;

    private static IApplicationVtable _vtable = null!;

    public static ApplicationConfig Config { get; private set; } = null!;
    public static IPlatform Platform { get; private set; } = null!;
    public static IRender RenderBackend { get; private set; } = null!;
    public static IAudio AudioBackend { get; private set; } = null!;
    public static Vector2 WindowSize => Platform.WindowSize;
    public static string AssetPath { get; private set; } = null!;

    public static void Init(ApplicationConfig config)
    {
        Config = config;

        // Platform and render backend must be provided
        Platform = config.Platform ?? throw new ArgumentNullException(nameof(config.Platform),
            "Platform must be provided. Use SDLPlatform for desktop or WebPlatform for web.");

        RenderBackend = config.RenderBackend ?? throw new ArgumentNullException(nameof(config.RenderBackend),
            "RenderBackend must be provided. Use OpenGLRender for desktop or WebGLRender for web.");

        AudioBackend = config.AudioBackend ?? throw new ArgumentNullException(nameof(config.AudioBackend),
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
        Audio.Init(AudioBackend);
        Render.Init(config.Render, RenderBackend);
        UI.Init();

        // Register asset types and load assets
        RegisterAssetTypes();
        _vtable.LoadAssets();
        Render.ResolveAssets();

        _running = true;
    }

    private static void RegisterAssetTypes()
    {
        Texture.RegisterDef();
        Sprite.RegisterDef();
        Sound.RegisterDef();
        Shader.RegisterDef();
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

            Render.BeginFrame();
            _vtable.Update();
            Render.BeginUI();
            UI.Begin();
            _vtable.UpdateUI();
            UI.End();
            Render.EndFrame();

            Platform.SwapBuffers();
        }
    }

    public static void Shutdown()
    {
        _vtable.UnloadAssets();

        UI.Shutdown();
        Render.Shutdown();
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

        Render.BeginFrame();
        _vtable.Update();
        Render.BeginUI();
        UI.Begin();
        _vtable.UpdateUI();
        UI.End();
        Render.EndFrame();
    }
}
