//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public enum InputCode
{
    None = 0,

    // Mouse Buttons
    MouseLeft,
    MouseRight,
    MouseMiddle,
    MouseButton4,
    MouseButton5,
    MouseLeftDoubleClick,

    // Mouse Axes
    MouseX,
    MouseY,
    MouseScrollX,
    MouseScrollY,

    // Keyboard Keys
    KeyA, KeyB, KeyC, KeyD, KeyE, KeyF, KeyG, KeyH, KeyI, KeyJ, KeyK, KeyL, KeyM,
    KeyN, KeyO, KeyP, KeyQ, KeyR, KeyS, KeyT, KeyU, KeyV, KeyW, KeyX, KeyY, KeyZ,

    Key0, Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9,

    KeySemicolon,
    KeyQuote,
    KeyMinus,
    KeyEquals,

    KeyTilde,
    KeyLeftBracket,
    KeyRightBracket,

    KeySpace,
    KeyEnter,
    KeyTab,
    KeyComma,
    KeyPeriod,
    KeyBackspace,
    KeyEscape,

    KeyLeftShift,
    KeyLeftCtrl,
    KeyLeftAlt,
    KeyRightShift,
    KeyRightCtrl,
    KeyRightAlt,

    KeyUp,
    KeyDown,
    KeyLeft,
    KeyRight,

    KeyF1, KeyF2, KeyF3, KeyF4, KeyF5, KeyF6,
    KeyF7, KeyF8, KeyF9, KeyF10, KeyF11, KeyF12,

    KeyLeftSuper,
    KeyRightSuper,

    // Gamepad Buttons (Works for any connected gamepad)
    GamepadA,           // Cross on PS, A on Xbox
    GamepadB,           // Circle on PS, B on Xbox
    GamepadX,           // Square on PS, X on Xbox
    GamepadY,           // Triangle on PS, Y on Xbox
    GamepadStart,
    GamepadBack,        // Select/View
    GamepadDpadUp,
    GamepadDpadDown,
    GamepadDpadLeft,
    GamepadDpadRight,
    GamepadLeftShoulder,
    GamepadRightShoulder,
    GamepadLeftStickButton,
    GamepadLeftStickLeft,
    GamepadLeftStickRight,
    GamepadLeftStickUp,
    GamepadLeftStickDown,
    GamepadRightStickButton,
    GamepadLeftTriggerButton,
    GamepadRightTriggerButton,

    GamepadGuide,       // PS/Xbox button

    // Gamepad Axes
    GamepadLeftStickX,
    GamepadLeftStickY,
    GamepadRightStickX,
    GamepadRightStickY,
    GamepadLeftTrigger,   // 0 to 1
    GamepadRightTrigger,  // 0 to 1

    // Gamepad 1
    Gamepad1A, Gamepad1B, Gamepad1X, Gamepad1Y,
    Gamepad1Start, Gamepad1Back,
    Gamepad1DpadUp, Gamepad1DpadDown, Gamepad1DpadLeft, Gamepad1DpadRight,
    Gamepad1LeftShoulder, Gamepad1RightShoulder,
    Gamepad1LeftStickButton, Gamepad1LeftStickLeft, Gamepad1LeftStickRight,
    Gamepad1LeftStickUp, Gamepad1LeftStickDown,
    Gamepad1RightStickButton,
    Gamepad1LeftTriggerButton, Gamepad1RightTriggerButton,
    Gamepad1LeftStickX, Gamepad1LeftStickY,
    Gamepad1RightStickX, Gamepad1RightStickY,
    Gamepad1LeftTrigger, Gamepad1RightTrigger,

    // Gamepad 2
    Gamepad2A, Gamepad2B, Gamepad2X, Gamepad2Y,
    Gamepad2LeftShoulder, Gamepad2RightShoulder,
    Gamepad2Start, Gamepad2Back,
    Gamepad2LeftStickButton, Gamepad2RightStickButton,
    Gamepad2DpadUp, Gamepad2DpadDown, Gamepad2DpadLeft, Gamepad2DpadRight,
    Gamepad2LeftStickX, Gamepad2LeftStickY,
    Gamepad2RightStickX, Gamepad2RightStickY,
    Gamepad2LeftTrigger, Gamepad2RightTrigger,

    // Gamepad 3
    Gamepad3A, Gamepad3B, Gamepad3X, Gamepad3Y,
    Gamepad3LeftShoulder, Gamepad3RightShoulder,
    Gamepad3Start, Gamepad3Back,
    Gamepad3LeftStickButton, Gamepad3RightStickButton,
    Gamepad3DpadUp, Gamepad3DpadDown, Gamepad3DpadLeft, Gamepad3DpadRight,
    Gamepad3LeftStickX, Gamepad3LeftStickY,
    Gamepad3RightStickX, Gamepad3RightStickY,
    Gamepad3LeftTrigger, Gamepad3RightTrigger,

    // Gamepad 4
    Gamepad4A, Gamepad4B, Gamepad4X, Gamepad4Y,
    Gamepad4LeftShoulder, Gamepad4RightShoulder,
    Gamepad4Start, Gamepad4Back,
    Gamepad4LeftStickButton, Gamepad4RightStickButton,
    Gamepad4DpadUp, Gamepad4DpadDown, Gamepad4DpadLeft, Gamepad4DpadRight,
    Gamepad4LeftStickX, Gamepad4LeftStickY,
    Gamepad4RightStickX, Gamepad4RightStickY,
    Gamepad4LeftTrigger, Gamepad4RightTrigger,

    Count
}

public static class InputCodeExtensions
{
    public static bool IsKeyboard(this InputCode code) =>
        code >= InputCode.KeyA && code <= InputCode.KeyRightSuper;

    public static bool IsMouse(this InputCode code) =>
        code >= InputCode.MouseLeft && code <= InputCode.MouseScrollY;

    public static bool IsGamepad(this InputCode code) =>
        code >= InputCode.GamepadA && code <= InputCode.Gamepad4RightTrigger;

    public static bool IsAxis(this InputCode code) =>
        code == InputCode.MouseX || code == InputCode.MouseY ||
        code == InputCode.MouseScrollX || code == InputCode.MouseScrollY ||
        (code >= InputCode.GamepadLeftStickX && code <= InputCode.GamepadRightTrigger) ||
        (code >= InputCode.Gamepad1LeftStickX && code <= InputCode.Gamepad1RightTrigger) ||
        (code >= InputCode.Gamepad2LeftStickX && code <= InputCode.Gamepad2RightTrigger) ||
        (code >= InputCode.Gamepad3LeftStickX && code <= InputCode.Gamepad3RightTrigger) ||
        (code >= InputCode.Gamepad4LeftStickX && code <= InputCode.Gamepad4RightTrigger);

    public static bool IsButton(this InputCode code) =>
        !code.IsAxis() && code != InputCode.None;
}
