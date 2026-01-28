//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public interface IApplication
{
    void Update();
    void UpdateUI() { }
    void LateUpdate() { }
    void LoadConfig(ApplicationConfig config) { }
    void SaveConfig() { }
    void LoadAssets() { }
    void UnloadAssets() { }
    void ReloadAssets() { }
}

public class ApplicationConfig
{
    public string Title { get; init; } = "NoZ";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int MinWidth { get; init; } = 800;
    public int MinHeight { get; init; } = 600;
    public int X { get; set; } = PlatformConfig.WindowPositionCentered;
    public int Y { get; set; } = PlatformConfig.WindowPositionCentered;
    public bool VSync { get; init; } = true;
    public bool Resizable { get; init; } = true;
    public string? IconPath { get; init; }
    public GraphicsConfig? Graphics { get; init; }
    public UIConfig? UI { get; init; }
    public IApplication? Vtable { get; init; }
    public IPlatform? Platform { get; init; }
    public IAudioDriver? AudioBackend { get; init; }
    public string? AssetPath { get; init; } = null;
    public string TextShader { get; init; } = "text";
}
