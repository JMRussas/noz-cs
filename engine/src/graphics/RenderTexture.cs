//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct RenderTexture : IDisposable
{
    public nuint Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public readonly bool IsValid => Handle != 0;

    internal RenderTexture(nuint handle, int width, int height)
    {
        Handle = handle;
        Width = width;
        Height = height;
    }

    public static RenderTexture Create(int width, int height, string? name = null)
    {
        var handle = Graphics.Driver.CreateRenderTexture(width, height, name: name);
        return new RenderTexture(handle, width, height);
    }

    public void Dispose()
    {
        if (Handle == 0) return;

        Graphics.Driver.DestroyRenderTexture(Handle);
        Handle = 0;
    }
}
