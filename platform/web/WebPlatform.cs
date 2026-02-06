//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Microsoft.JSInterop;
using NoZ;
using NoZ.Platform;

namespace NoZ.Platform.Web;

public class WebPlatform : IPlatform

{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private Vector2 _windowSize;
    private float _displayScale = 1.0f;
    private bool _shouldQuit;
    private DotNetObjectReference<WebPlatform>? _dotNetRef;

    Vector2Int IPlatform.WindowSize => new((int)_windowSize.X, (int)_windowSize.Y);
    public Vector2Int WindowPosition => Vector2Int.Zero; // Not applicable for web
    public float DisplayScale => _displayScale;
    public bool IsTextboxVisible => false; // TODO: Implement
    public nint WindowHandle => nint.Zero; // Not applicable for web
    public event Action<PlatformEvent>? OnEvent;

    public void SetWindowSize(int width, int height) { } // Not applicable for web
    public void SetWindowPosition(int x, int y) { } // Not applicable for web

    public nint GetGraphicsProcAddress(string name) => nint.Zero; // Not applicable for web

    public WebPlatform(IJSRuntime js)
    {
        _js = js;
    }

    public void Init(PlatformConfig config)
    {
        // Don't overwrite _windowSize if InitAsync already set it to actual window size
        if (!_initialized)
            _windowSize = new Vector2(config.Width, config.Height);
    }

    private bool _initialized;

    public async Task InitAsync(PlatformConfig config)
    {
        _windowSize = new Vector2(config.Width, config.Height);
        _dotNetRef = DotNetObjectReference.Create(this);

        _module = await _js.InvokeAsync<IJSObjectReference>("import", "/js/noz/noz-platform.js");
        var result = await _module.InvokeAsync<InitResult>("init", _dotNetRef, config.Width, config.Height);
        _windowSize = new Vector2(result.Width, result.Height);
        _displayScale = result.Dpr;
        _initialized = true;
    }

    private record InitResult(int Width, int Height, float Dpr);

    public void Shutdown()
    {
        _module?.InvokeVoidAsync("shutdown");
        _dotNetRef?.Dispose();
    }

    public bool PollEvents()
    {
        // Events are pushed from JS via JSInvokable methods
        return !_shouldQuit;
    }

    public void SwapBuffers()
    {
        // WebGPU automatically presents after draw calls
        // Nothing to do here
    }

    public void RequestQuit()
    {
        _shouldQuit = true;
    }

    public void SetResizeCallback(Action? callback)
    {
        // TODO: Implement resize callback for web
    }

    public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style)
    {
        // TODO: Implement native textbox for web
    }

    public void HideTextbox()
    {
        // TODO: Implement native textbox for web
    }

    public void UpdateTextboxRect(Rect rect, int fontSize)
    {
        // TODO: Implement native textbox for web
    }

    public bool UpdateTextboxText(ref string text)
    {
        // TODO: Implement native textbox for web
        return false;
    }

    public void SetClipboardText(string text)
    {
        // TODO: Implement clipboard for web
    }

    public string? GetClipboardText()
    {
        // TODO: Implement clipboard for web
        return null;
    }

    public void SetCursor(SystemCursor cursor)
    {
        var cursorStyle = cursor switch
        {
            SystemCursor.None => "none",
            SystemCursor.Default => "default",
            SystemCursor.Move => "move",
            SystemCursor.Crosshair => "crosshair",
            SystemCursor.Wait => "wait",
            _ => "default"
        };
        _module?.InvokeVoidAsync("setCursor", cursorStyle);
    }

    // Called from JavaScript

    [JSInvokable]
    public void OnKeyDown(string key)
    {
        var code = KeyToInputCode(key);
        if (code != InputCode.None)
            OnEvent?.Invoke(PlatformEvent.KeyDown(code));
    }

    [JSInvokable]
    public void OnKeyUp(string key)
    {
        var code = KeyToInputCode(key);
        if (code != InputCode.None)
            OnEvent?.Invoke(PlatformEvent.KeyUp(code));
    }

    [JSInvokable]
    public void OnMouseDown(int button, int clickCount)
    {
        var code = MouseButtonToInputCode(button);
        if (code != InputCode.None)
            OnEvent?.Invoke(PlatformEvent.MouseDown(code, clickCount));
    }

    [JSInvokable]
    public void OnMouseUp(int button)
    {
        var code = MouseButtonToInputCode(button);
        if (code != InputCode.None)
            OnEvent?.Invoke(PlatformEvent.MouseUp(code));
    }

    [JSInvokable]
    public void OnMouseMove(float x, float y)
    {
        OnEvent?.Invoke(PlatformEvent.MouseMove(new Vector2(x, y)));
    }

    [JSInvokable]
    public void OnMouseWheel(float deltaX, float deltaY)
    {
        OnEvent?.Invoke(PlatformEvent.MouseScroll(deltaX, deltaY));
    }

    [JSInvokable]
    public void OnMouseEnter()
    {
        OnEvent?.Invoke(PlatformEvent.MouseEnter());
    }

    [JSInvokable]
    public void OnMouseLeave()
    {
        OnEvent?.Invoke(PlatformEvent.MouseLeave());
    }

    [JSInvokable]
    public void OnResize(int width, int height)
    {
        _windowSize = new Vector2(width, height);
        OnEvent?.Invoke(PlatformEvent.Resize(width, height));
    }

    private static InputCode KeyToInputCode(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "a" => InputCode.KeyA,
            "b" => InputCode.KeyB,
            "c" => InputCode.KeyC,
            "d" => InputCode.KeyD,
            "e" => InputCode.KeyE,
            "f" => InputCode.KeyF,
            "g" => InputCode.KeyG,
            "h" => InputCode.KeyH,
            "i" => InputCode.KeyI,
            "j" => InputCode.KeyJ,
            "k" => InputCode.KeyK,
            "l" => InputCode.KeyL,
            "m" => InputCode.KeyM,
            "n" => InputCode.KeyN,
            "o" => InputCode.KeyO,
            "p" => InputCode.KeyP,
            "q" => InputCode.KeyQ,
            "r" => InputCode.KeyR,
            "s" => InputCode.KeyS,
            "t" => InputCode.KeyT,
            "u" => InputCode.KeyU,
            "v" => InputCode.KeyV,
            "w" => InputCode.KeyW,
            "x" => InputCode.KeyX,
            "y" => InputCode.KeyY,
            "z" => InputCode.KeyZ,

            "1" => InputCode.Key1,
            "2" => InputCode.Key2,
            "3" => InputCode.Key3,
            "4" => InputCode.Key4,
            "5" => InputCode.Key5,
            "6" => InputCode.Key6,
            "7" => InputCode.Key7,
            "8" => InputCode.Key8,
            "9" => InputCode.Key9,
            "0" => InputCode.Key0,

            "enter" => InputCode.KeyEnter,
            "escape" => InputCode.KeyEscape,
            "backspace" => InputCode.KeyBackspace,
            "tab" => InputCode.KeyTab,
            " " => InputCode.KeySpace,

            "-" => InputCode.KeyMinus,
            "=" => InputCode.KeyEquals,
            "[" => InputCode.KeyLeftBracket,
            "]" => InputCode.KeyRightBracket,
            ";" => InputCode.KeySemicolon,
            "'" => InputCode.KeyQuote,
            "`" => InputCode.KeyTilde,
            "," => InputCode.KeyComma,
            "." => InputCode.KeyPeriod,

            "f1" => InputCode.KeyF1,
            "f2" => InputCode.KeyF2,
            "f3" => InputCode.KeyF3,
            "f4" => InputCode.KeyF4,
            "f5" => InputCode.KeyF5,
            "f6" => InputCode.KeyF6,
            "f7" => InputCode.KeyF7,
            "f8" => InputCode.KeyF8,
            "f9" => InputCode.KeyF9,
            "f10" => InputCode.KeyF10,
            "f11" => InputCode.KeyF11,
            "f12" => InputCode.KeyF12,

            "arrowright" => InputCode.KeyRight,
            "arrowleft" => InputCode.KeyLeft,
            "arrowdown" => InputCode.KeyDown,
            "arrowup" => InputCode.KeyUp,

            "control" => InputCode.KeyLeftCtrl,
            "shift" => InputCode.KeyLeftShift,
            "alt" => InputCode.KeyLeftAlt,
            "meta" => InputCode.KeyLeftSuper,

            _ => InputCode.None
        };
    }

    private static InputCode MouseButtonToInputCode(int button)
    {
        return button switch
        {
            0 => InputCode.MouseLeft,
            1 => InputCode.MouseMiddle,
            2 => InputCode.MouseRight,
            3 => InputCode.MouseButton4,
            4 => InputCode.MouseButton5,
            _ => InputCode.None
        };
    }

    public Stream? OpenAssetStream(AssetType type, string name, string extension) => null;

    public Stream? LoadPersistentData(string name, string? appName = null) => null; // TODO: Use localStorage
    public void SavePersistentData(string name, Stream data, string? appName = null) { } // TODO: Use localStorage

    public void Log(string message) => Console.WriteLine(message);
}
