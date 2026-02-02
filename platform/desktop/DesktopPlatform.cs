//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace NoZ.Platform;

public unsafe partial class SDLPlatform : IPlatform
{
    private SDL_Window* _window;
    private SDL_GLContextState* _glContext;
    private Action? _resizeCallback;
    private static SDLPlatform? _instance;

    private readonly SDL_Cursor*[] _cursors = new SDL_Cursor*[Enum.GetValues<SystemCursor>().Length];
    private SystemCursor _currentCursor = SystemCursor.Default;

    public Vector2Int WindowSize { get; private set; }

    public Vector2Int WindowPosition
    {
        get
        {
            if (_window == null)
                return Vector2Int.Zero;
            int x, y;
            SDL_GetWindowPosition(_window, &x, &y);
            return new Vector2Int(x, y);
        }
    }

    public void SetWindowSize(int width, int height)
    {
        if (_window == null) return;
        SDL_SetWindowSize(_window, width, height);
        WindowSize = new Vector2Int(width, height);
    }

    public void SetWindowPosition(int x, int y)
    {
        if (_window == null) return;
        SDL_SetWindowPosition(_window, x, y);
    }

    internal static SDL_Window* Window => _instance != null ? _instance._window : null;

    public float DisplayScale => _window != null ? SDL_GetWindowDisplayScale(_window) : 1.0f;

    public event Action<PlatformEvent>? OnEvent;

    public void Init(PlatformConfig config)
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");
        }

        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 5);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);

        if (config.MsaaSamples > 0)
        {
            SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_MULTISAMPLEBUFFERS, 1);
            SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_MULTISAMPLESAMPLES, config.MsaaSamples);
        }

        var windowFlags = SDL_WindowFlags.SDL_WINDOW_OPENGL;
        if (config.Resizable)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        }

        _window = SDL_CreateWindow(config.Title, config.Width, config.Height, windowFlags);
        if (_window == null)
        {
            throw new Exception($"Failed to create window: {SDL_GetError()}");
        }

        if (config.X != PlatformConfig.WindowPositionCentered || config.Y != PlatformConfig.WindowPositionCentered)
        {
            var x = config.X == PlatformConfig.WindowPositionCentered ? (int)SDL_WINDOWPOS_CENTERED : config.X;
            var y = config.Y == PlatformConfig.WindowPositionCentered ? (int)SDL_WINDOWPOS_CENTERED : config.Y;
            SDL_SetWindowPosition(_window, x, y);
        }

        if (config.MinWidth > 0 && config.MinHeight > 0)
            SDL_SetWindowMinimumSize(_window, config.MinWidth, config.MinHeight);

        _glContext = SDL_GL_CreateContext(_window);
        if (_glContext == null)
        {
            throw new Exception($"Failed to create OpenGL context: {SDL_GetError()}");
        }

        SDL_GL_MakeCurrent(_window, _glContext);
        SDL_GL_SetSwapInterval(config.VSync ? 1 : 0);

        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
        SDL_SetHint(SDL_HINT_MOUSE_DOUBLE_CLICK_RADIUS, "4");  // Windows default ~4px

        WindowSize = new Vector2Int(config.Width, config.Height);

        _instance = this;
        SDL_AddEventWatch(&ResizeEventWatch, nint.Zero);

        if (!string.IsNullOrEmpty(config.IconPath) && File.Exists(config.IconPath))
            SetWindowIcon(config.IconPath);

        InitNativeTextInput();

        SDL_StartTextInput(_window);
    }

    private void SetWindowIcon(string iconPath)
    {
#if false
        using var stream = File.OpenRead(iconPath);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        fixed (byte* pixels = image.Data)
        {
            var surface = SDL_CreateSurfaceFrom(
                image.Width,
                image.Height,
                SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888,
                (nint)pixels,
                image.Width * 4
            );

            if (surface != null)
            {
                SDL_SetWindowIcon(_window, surface);
                SDL_DestroySurface(surface);
            }
        }
#endif
    }

    public void SetResizeCallback(Action? callback)
    {
        _resizeCallback = callback;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static SDLBool ResizeEventWatch(nint userdata, SDL_Event* evt)
    {
        if (_instance == null) return true;

        if (evt->Type == SDL_EventType.SDL_EVENT_WINDOW_RESIZED ||
            evt->Type == SDL_EventType.SDL_EVENT_WINDOW_EXPOSED)
        {
            if (evt->Type == SDL_EventType.SDL_EVENT_WINDOW_RESIZED)
            {
                _instance.WindowSize = new Vector2Int(evt->window.data1, evt->window.data2);
                _instance.OnEvent?.Invoke(PlatformEvent.Resize(evt->window.data1, evt->window.data2));
            }

            _instance._resizeCallback?.Invoke();
            SDL_GL_SwapWindow(_instance._window);
        }

        return true;
    }

    public void Shutdown()
    {
        SDL_StopTextInput(_window);

        SDL_RemoveEventWatch(&ResizeEventWatch, nint.Zero);
        _instance = null;
        _resizeCallback = null;

        for (var i = 0; i < _cursors.Length; i++)
        {
            if (_cursors[i] != null)
            {
                SDL_DestroyCursor(_cursors[i]);
                _cursors[i] = null;
            }
        }

        ShutdownNativeTextInput();

        if (_glContext != null)
        {
            SDL_GL_DestroyContext(_glContext);
            _glContext = null;
        }

        if (_window != null)
        {
            SDL_DestroyWindow(_window);
            _window = null;
        }

        SDL_Quit();
    }

    public bool PollEvents()
    {
        SDL_Event evt;
        while (SDL_PollEvent(&evt))
        {
            if (evt.Type == SDL_EventType.SDL_EVENT_QUIT)
                return false;

            ProcessEvent(evt);
        }
        return true;
    }

    public void SwapBuffers()
    {
        SDL_GL_SwapWindow(_window);
    }

    private void ProcessEvent(SDL_Event evt)
    {
        switch (evt.Type)
        {
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            {
                var code = ScancodeToInputCode(evt.key.scancode);
                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.KeyDown(code));
                break;
            }

            case SDL_EventType.SDL_EVENT_KEY_UP:
            {
                var code = ScancodeToInputCode(evt.key.scancode);
                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.KeyUp(code));
                break;
            }

            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
            {
                var text = Marshal.PtrToStringUTF8((nint)evt.text.text);
                if (!string.IsNullOrEmpty(text))
                    OnEvent?.Invoke(PlatformEvent.TextInputEvent(text));
                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            {
                var code = MouseButtonToInputCode(evt.button.button);
                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.MouseDown(code, evt.button.clicks));
                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            {
                var code = MouseButtonToInputCode(evt.button.button);
                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.MouseUp(code));
                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                OnEvent?.Invoke(PlatformEvent.MouseMove(new Vector2(evt.motion.x, evt.motion.y)));
                break;

            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                OnEvent?.Invoke(PlatformEvent.MouseScroll(evt.wheel.x, evt.wheel.y));
                break;

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            {
                var code = GamepadButtonToInputCode((SDL_GamepadButton)evt.gbutton.button);
                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.GamepadDown(code));
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
            {
                var code = GamepadButtonToInputCode((SDL_GamepadButton)evt.gbutton.button);
                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.GamepadUp(code));
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
            {
                var axis = (SDL_GamepadAxis)evt.gaxis.axis;
                var value = evt.gaxis.value / 32767.0f;

                var code = axis switch
                {
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX => InputCode.GamepadLeftStickX,
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY => InputCode.GamepadLeftStickY,
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX => InputCode.GamepadRightStickX,
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY => InputCode.GamepadRightStickY,
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER => InputCode.GamepadLeftTrigger,
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER => InputCode.GamepadRightTrigger,
                    _ => InputCode.None
                };

                if (code != InputCode.None)
                    OnEvent?.Invoke(PlatformEvent.GamepadAxisMove(code, value));
                break;
            }

            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                WindowSize = new Vector2Int(evt.window.data1, evt.window.data2);
                OnEvent?.Invoke(PlatformEvent.Resize(evt.window.data1, evt.window.data2));
                break;
        }
    }

    private static InputCode ScancodeToInputCode(SDL_Scancode scancode)
    {
        return scancode switch
        {
            SDL_Scancode.SDL_SCANCODE_A => InputCode.KeyA,
            SDL_Scancode.SDL_SCANCODE_B => InputCode.KeyB,
            SDL_Scancode.SDL_SCANCODE_C => InputCode.KeyC,
            SDL_Scancode.SDL_SCANCODE_D => InputCode.KeyD,
            SDL_Scancode.SDL_SCANCODE_E => InputCode.KeyE,
            SDL_Scancode.SDL_SCANCODE_F => InputCode.KeyF,
            SDL_Scancode.SDL_SCANCODE_G => InputCode.KeyG,
            SDL_Scancode.SDL_SCANCODE_H => InputCode.KeyH,
            SDL_Scancode.SDL_SCANCODE_I => InputCode.KeyI,
            SDL_Scancode.SDL_SCANCODE_J => InputCode.KeyJ,
            SDL_Scancode.SDL_SCANCODE_K => InputCode.KeyK,
            SDL_Scancode.SDL_SCANCODE_L => InputCode.KeyL,
            SDL_Scancode.SDL_SCANCODE_M => InputCode.KeyM,
            SDL_Scancode.SDL_SCANCODE_N => InputCode.KeyN,
            SDL_Scancode.SDL_SCANCODE_O => InputCode.KeyO,
            SDL_Scancode.SDL_SCANCODE_P => InputCode.KeyP,
            SDL_Scancode.SDL_SCANCODE_Q => InputCode.KeyQ,
            SDL_Scancode.SDL_SCANCODE_R => InputCode.KeyR,
            SDL_Scancode.SDL_SCANCODE_S => InputCode.KeyS,
            SDL_Scancode.SDL_SCANCODE_T => InputCode.KeyT,
            SDL_Scancode.SDL_SCANCODE_U => InputCode.KeyU,
            SDL_Scancode.SDL_SCANCODE_V => InputCode.KeyV,
            SDL_Scancode.SDL_SCANCODE_W => InputCode.KeyW,
            SDL_Scancode.SDL_SCANCODE_X => InputCode.KeyX,
            SDL_Scancode.SDL_SCANCODE_Y => InputCode.KeyY,
            SDL_Scancode.SDL_SCANCODE_Z => InputCode.KeyZ,

            SDL_Scancode.SDL_SCANCODE_1 => InputCode.Key1,
            SDL_Scancode.SDL_SCANCODE_2 => InputCode.Key2,
            SDL_Scancode.SDL_SCANCODE_3 => InputCode.Key3,
            SDL_Scancode.SDL_SCANCODE_4 => InputCode.Key4,
            SDL_Scancode.SDL_SCANCODE_5 => InputCode.Key5,
            SDL_Scancode.SDL_SCANCODE_6 => InputCode.Key6,
            SDL_Scancode.SDL_SCANCODE_7 => InputCode.Key7,
            SDL_Scancode.SDL_SCANCODE_8 => InputCode.Key8,
            SDL_Scancode.SDL_SCANCODE_9 => InputCode.Key9,
            SDL_Scancode.SDL_SCANCODE_0 => InputCode.Key0,

            SDL_Scancode.SDL_SCANCODE_RETURN => InputCode.KeyEnter,
            SDL_Scancode.SDL_SCANCODE_ESCAPE => InputCode.KeyEscape,
            SDL_Scancode.SDL_SCANCODE_BACKSPACE => InputCode.KeyBackspace,
            SDL_Scancode.SDL_SCANCODE_TAB => InputCode.KeyTab,
            SDL_Scancode.SDL_SCANCODE_SPACE => InputCode.KeySpace,

            SDL_Scancode.SDL_SCANCODE_MINUS => InputCode.KeyMinus,
            SDL_Scancode.SDL_SCANCODE_EQUALS => InputCode.KeyEquals,
            SDL_Scancode.SDL_SCANCODE_LEFTBRACKET => InputCode.KeyLeftBracket,
            SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET => InputCode.KeyRightBracket,
            SDL_Scancode.SDL_SCANCODE_SEMICOLON => InputCode.KeySemicolon,
            SDL_Scancode.SDL_SCANCODE_APOSTROPHE => InputCode.KeyQuote,
            SDL_Scancode.SDL_SCANCODE_GRAVE => InputCode.KeyTilde,
            SDL_Scancode.SDL_SCANCODE_COMMA => InputCode.KeyComma,
            SDL_Scancode.SDL_SCANCODE_PERIOD => InputCode.KeyPeriod,

            SDL_Scancode.SDL_SCANCODE_F1 => InputCode.KeyF1,
            SDL_Scancode.SDL_SCANCODE_F2 => InputCode.KeyF2,
            SDL_Scancode.SDL_SCANCODE_F3 => InputCode.KeyF3,
            SDL_Scancode.SDL_SCANCODE_F4 => InputCode.KeyF4,
            SDL_Scancode.SDL_SCANCODE_F5 => InputCode.KeyF5,
            SDL_Scancode.SDL_SCANCODE_F6 => InputCode.KeyF6,
            SDL_Scancode.SDL_SCANCODE_F7 => InputCode.KeyF7,
            SDL_Scancode.SDL_SCANCODE_F8 => InputCode.KeyF8,
            SDL_Scancode.SDL_SCANCODE_F9 => InputCode.KeyF9,
            SDL_Scancode.SDL_SCANCODE_F10 => InputCode.KeyF10,
            SDL_Scancode.SDL_SCANCODE_F11 => InputCode.KeyF11,
            SDL_Scancode.SDL_SCANCODE_F12 => InputCode.KeyF12,

            SDL_Scancode.SDL_SCANCODE_RIGHT => InputCode.KeyRight,
            SDL_Scancode.SDL_SCANCODE_LEFT => InputCode.KeyLeft,
            SDL_Scancode.SDL_SCANCODE_DOWN => InputCode.KeyDown,
            SDL_Scancode.SDL_SCANCODE_UP => InputCode.KeyUp,
            SDL_Scancode.SDL_SCANCODE_DELETE => InputCode.KeyDelete,
            SDL_Scancode.SDL_SCANCODE_INSERT => InputCode.KeyInsert,
            SDL_Scancode.SDL_SCANCODE_HOME => InputCode.KeyHome,
            SDL_Scancode.SDL_SCANCODE_END => InputCode.KeyEnd,
            SDL_Scancode.SDL_SCANCODE_PAGEUP => InputCode.KeyPageUp,
            SDL_Scancode.SDL_SCANCODE_PAGEDOWN => InputCode.KeyPageDown,

            SDL_Scancode.SDL_SCANCODE_LCTRL => InputCode.KeyLeftCtrl,
            SDL_Scancode.SDL_SCANCODE_LSHIFT => InputCode.KeyLeftShift,
            SDL_Scancode.SDL_SCANCODE_LALT => InputCode.KeyLeftAlt,
            SDL_Scancode.SDL_SCANCODE_LGUI => InputCode.KeyLeftSuper,
            SDL_Scancode.SDL_SCANCODE_RCTRL => InputCode.KeyRightCtrl,
            SDL_Scancode.SDL_SCANCODE_RSHIFT => InputCode.KeyRightShift,
            SDL_Scancode.SDL_SCANCODE_RALT => InputCode.KeyRightAlt,
            SDL_Scancode.SDL_SCANCODE_RGUI => InputCode.KeyRightSuper,

            _ => InputCode.None
        };
    }

    private static InputCode MouseButtonToInputCode(byte button)
    {
        return button switch
        {
            1 => InputCode.MouseLeft,
            2 => InputCode.MouseMiddle,
            3 => InputCode.MouseRight,
            4 => InputCode.MouseButton4,
            5 => InputCode.MouseButton5,
            _ => InputCode.None
        };
    }

    private static InputCode GamepadButtonToInputCode(SDL_GamepadButton button)
    {
        return button switch
        {
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => InputCode.GamepadA,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => InputCode.GamepadB,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => InputCode.GamepadX,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => InputCode.GamepadY,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => InputCode.GamepadBack,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE => InputCode.GamepadGuide,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => InputCode.GamepadStart,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => InputCode.GamepadLeftStickButton,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => InputCode.GamepadRightStickButton,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => InputCode.GamepadLeftShoulder,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => InputCode.GamepadRightShoulder,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => InputCode.GamepadDpadUp,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => InputCode.GamepadDpadDown,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => InputCode.GamepadDpadLeft,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => InputCode.GamepadDpadRight,
            _ => InputCode.None
        };
    }

    public void SetClipboardText(string text)
    {
        SDL_SetClipboardText(text);
    }

    public string? GetClipboardText()
    {
        if (SDL_HasClipboardText())
        {
            return SDL_GetClipboardText();
        }
        return null;
    }

    public void SetCursor(SystemCursor cursor)
    {
        if (_currentCursor == cursor)
            return;

        _currentCursor = cursor;

        if (cursor == SystemCursor.None)
        {
            SDL_HideCursor();
            return;
        }

        SDL_ShowCursor();

        var index = (int)cursor;
        if (_cursors[index] == null)
        {
            var sdlCursor = cursor switch
            {
                SystemCursor.Default => SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT,
                SystemCursor.Move => SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE,
                SystemCursor.Crosshair => SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR,
                SystemCursor.Wait => SDL_SystemCursor.SDL_SYSTEM_CURSOR_WAIT,
                _ => SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT
            };
            _cursors[index] = SDL_CreateSystemCursor(sdlCursor);
        }

        SDL_SetCursor(_cursors[index]);
    }

    public nint WindowHandle
    {
        get
        {
            if (_window == null)
                return nint.Zero;

            var props = SDL_GetWindowProperties(_window);
            return (nint)SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WIN32_HWND_POINTER, nint.Zero);
        }
    }

    public nint GetGraphicsProcAddress(string name)
    {
        return SDL_GL_GetProcAddress(name);
    }

    public Stream? OpenAssetStream(AssetType type, string name, string extension, string? libraryPath = null)
    {
        var typeName = type.ToString().ToLowerInvariant();
        var fileName = string.IsNullOrEmpty(extension) ? name : name + extension;
        var fullPath = Path.Combine(libraryPath ?? Application.AssetPath, typeName, fileName);
        return File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
    }

    private static string GetPersistentDataPath(string name, string? appName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        appName ??= Application.Config?.Title ?? "NoZ";
        return Path.Combine(appData, appName, name);
    }

    public Stream? LoadPersistentData(string name, string? appName = null)
    {
        var path = GetPersistentDataPath(name, appName);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void SavePersistentData(string name, Stream data, string? appName = null)
    {
        var path = GetPersistentDataPath(name, appName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var file = File.Create(path);
        data.CopyTo(file);
    }
}

