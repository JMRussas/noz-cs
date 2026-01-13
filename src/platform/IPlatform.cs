//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz.Platform;

public struct NativeTextboxStyle
{
    public Color32 BackgroundColor;
    public Color32 TextColor;
    public int FontSize;
    public bool Password;
}

public class PlatformConfig
{
    public string Title { get; set; } = "Noz Application";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool VSync { get; set; } = true;
    public bool Resizable { get; set; } = true;
    public string? IconPath { get; set; }
}

public interface IPlatform
{
    void Init(PlatformConfig config);
    void Shutdown();

    /// <summary>
    /// Poll platform events and dispatch to handlers.
    /// Returns false if quit was requested.
    /// </summary>
    bool PollEvents();

    void SwapBuffers();

    Vector2 WindowSize { get; }

    /// <summary>
    /// Called when an input event occurs.
    /// </summary>
    event Action<PlatformEvent>? OnEvent;

    /// <summary>
    /// Set a callback to render a frame during window resize.
    /// </summary>
    void SetResizeCallback(Action? callback);

    // Native Text Input
    void ShowTextbox(Rect rect, string text, NativeTextboxStyle style);
    void HideTextbox();
    void UpdateTextboxRect(Rect rect, int fontSize);
    bool UpdateTextboxText(ref string text);
    bool IsTextboxVisible { get; }
}
