//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using SDL;

using static SDL.SDL3;

namespace noz;

public static class Render
{
    private static RenderConfig? _config;
    private static RenderCommand[]? _commands;
    
    internal static void Init(RenderConfig? config)
    {
        _config = config ?? new RenderConfig();
        _commands = new RenderCommand[_config.MaxCommands];
    }

    internal static void Shutdown()
    {
        _config = null;
    }

    public static void BeginFrame()
    {
        
    }

    public static void EndFrame()
    {
        unsafe
        {
            SDL_GL_SwapWindow(Application.Window);
        }
    }
    
    public static void BindTransform(in Matrix3x2 transform)
    {
    }
    
    public static void Draw(Sprite sprite)
    {
    }

    public static void Draw(Sprite sprite, in Matrix3x2 transform)
    {
        
    } 
}