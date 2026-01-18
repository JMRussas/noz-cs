//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Platform;

public struct NativeTextboxStyle
{
    public Color32 BackgroundColor;
    public Color32 TextColor;
    public Color32 PlaceholderColor;
    public int FontSize;
    public bool Password;
    public string? Placeholder;
    public string? FontFamily;
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
    /// Gets the display scale factor (DPI scale) for the window.
    /// Returns 1.0 for standard displays, 1.5 for 150% scaling, 2.0 for 200% scaling, etc.
    /// </summary>
    float DisplayScale { get; }

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
