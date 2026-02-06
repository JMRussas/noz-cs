//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform;

public enum PlatformEventType
{
    None,

    // Keyboard
    KeyDown,
    KeyUp,
    TextInput,

    // Mouse
    MouseButtonDown,
    MouseButtonUp,
    MouseMove,
    MouseScroll,

    // Gamepad
    GamepadButtonDown,
    GamepadButtonUp,
    GamepadAxis,

    // Window
    WindowResize,
    WindowFocus,
    WindowUnfocus,
    WindowMouseEnter,
    WindowMouseLeave,
}