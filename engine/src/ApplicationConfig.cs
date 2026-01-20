//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public interface IApplicationVtable
{
    void Update();
    void UpdateUI() { }
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
    public string? IconPath { get; init; }
    public GraphicsConfig? Graphics { get; init; }
    public UIConfig? UI { get; init; }
    public IApplicationVtable? Vtable { get; init; }
    public IPlatform? Platform { get; init; }
    public IAudioDriver? AudioBackend { get; init; }
    public string AssetPath { get; init; } = "library/";
    public string TextShader { get; init; } = "text";
}
