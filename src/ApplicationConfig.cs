//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public interface IApplicationVtable
{
    void Update();
    void LoadAssets() { }
    void UnloadAssets() { }
    void ReloadAssets() { }
}

public class ApplicationConfig
{
    public string Title { get; init; } = "NoZ";
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool VSync { get; init; } = true;
    public bool Resizable { get; init; } = true;
    public RenderConfig? Render { get; init; }
    public IApplicationVtable? Vtable { get; init; }
    public IPlatform? Platform { get; init; }
    public IRender? RenderBackend { get; init; }
    public string AssetPath { get; init; } = "assets/library";
}
