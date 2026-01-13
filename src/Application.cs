//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public static class Application
{
    private static bool _running;

    private static IApplication _vtable = null!;

    public static ApplicationConfig Config { get; private set; } = null!;
    public static IPlatform Platform { get; private set; } = null!;
    public static IRender RenderBackend { get; private set; } = null!;
    public static Vector2 WindowSize => Platform.WindowSize;

    public static void Init(ApplicationConfig config)
    {
        Config = config;

        // Platform and render backend must be provided
        Platform = config.Platform ?? throw new ArgumentNullException(nameof(config.Platform),
            "Platform must be provided. Use SDLPlatform for desktop or WebPlatform for web.");

        RenderBackend = config.RenderBackend ?? throw new ArgumentNullException(nameof(config.RenderBackend),
            "RenderBackend must be provided. Use OpenGLRender for desktop or WebGLRender for web.");

        _vtable = config.Vtable ?? throw new ArgumentNullException(nameof(config.Vtable),
            "App must be provided. Implement IApplication in your game.");

        // Initialize platform
        Platform.Init(new PlatformConfig
        {
            Title = config.Title,
            Width = config.Width,
            Height = config.Height,
            VSync = config.VSync,
            Resizable = config.Resizable
        });

        // Subscribe to platform events
        Platform.OnEvent += Input.ProcessEvent;

        // Initialize subsystems
        Time.Init();
        Input.Init();
        Render.Init(config.Render, RenderBackend);

        _running = true;
    }

    public static void Run()
    {
        while (_running)
        {
            Time.Update();

            if (!Platform.PollEvents())
            {
                _running = false;
                continue;
            }

            Input.Update();

            Render.BeginFrame();
            _vtable.Update();
            Render.EndFrame();

            Platform.SwapBuffers();
        }
    }

    public static void Shutdown()
    {
        Render.Shutdown();
        Input.Shutdown();
        Time.Shutdown();

        Platform.OnEvent -= Input.ProcessEvent;
        Platform.Shutdown();

        _running = false;
    }

    public static void Quit()
    {
        _running = false;
    }
}
