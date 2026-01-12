//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SDL;

namespace noz;

public struct ApplicationVtable
{
    public Action? Update;
}

public class ApplicationConfig
{
    public string Title { get; set; } = "Noz Application";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool VSync { get; set; } = true;
    public bool Resizable { get; set; } = true;
    public int GLMajorVersion { get; set; } = 4;
    public int GLMinorVersion { get; set; } = 5;

    public RenderConfig? Render = null;

    public ApplicationVtable Vtable;
}
