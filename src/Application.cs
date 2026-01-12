//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SDL;
using static SDL.SDL3;

namespace noz;

public static class Application
{
    internal static unsafe SDL_Window* Window { get; private set; }
    private static unsafe SDL_GLContextState* _glContext;
    private static bool _running;
    
    public static ApplicationConfig Config { get; private set; } = null!;

    public static void Init(ApplicationConfig config)
    {
        Config = config;

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");
        }

        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, Config.GLMajorVersion);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, Config.GLMinorVersion);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);

        var windowFlags = SDL_WindowFlags.SDL_WINDOW_OPENGL;
        if (Config.Resizable)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        }

        unsafe
        {
            Window = SDL_CreateWindow(Config.Title, Config.Width, Config.Height, windowFlags);

            if (Window == null)
            {
                throw new Exception($"Failed to create window: {SDL_GetError()}");
            }

            _glContext = SDL_GL_CreateContext(Window);
            if (_glContext == null)
            {
                throw new Exception($"Failed to create OpenGL context: {SDL_GetError()}");
            }

            SDL_GL_MakeCurrent(Window, _glContext);
        }

        SDL_GL_SetSwapInterval(Config.VSync ? 1 : 0);
        _running = true;

        Input.Init();
        Render.Init(Config.Render);
    }

    public static void Run()
    {
        while (_running)
        {
            unsafe
            {
                SDL_Event evt;
                while (SDL_PollEvent(&evt))
                {
                    if (evt.Type == SDL_EventType.SDL_EVENT_QUIT)
                    {
                        _running = false;
                    }
                }
            }

            Input.Update();
            
            Render.BeginFrame();
            Config.Vtable.Update?.Invoke();
            Render.EndFrame();
            
        }
    }

    public static void Shutdown()
    {
        Render.Shutdown();
        Input.Shutdown();

        unsafe
        {
            if (_glContext != null)
            {
                SDL_GL_DestroyContext(_glContext);
                _glContext = null;
            }

            if (Window != null)
            {
                SDL_DestroyWindow(Window);
                Window = null;
            }
        }

        SDL_Quit();
        _running = false;
    }

    public static void Quit()
    {
        _running = false;
    }
}