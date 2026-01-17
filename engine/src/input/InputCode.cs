//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

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
    KeyDelete,
    KeyInsert,
    KeyHome,
    KeyEnd,
    KeyPageUp,
    KeyPageDown,

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
    private static readonly string[] Names =
    [
        "", // None

        // Mouse Buttons
        "Mouse Left", "Mouse Right", "Mouse Middle", "Mouse 4", "Mouse 5", "Double Click",

        // Mouse Axes
        "Mouse X", "Mouse Y", "Scroll X", "Scroll Y",

        // Keyboard Keys
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        ";", "'", "-", "=",
        "~", "[", "]",
        "Space", "Enter", "Tab", ",", ".", "Backspace", "Escape",
        "Shift", "Ctrl", "Alt", "Shift", "Ctrl", "Alt",
        "Up", "Down", "Left", "Right", "Delete", "Insert", "Home", "End", "Page Up", "Page Down",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Left Super", "Right Super",

        // Gamepad Buttons
        "A", "B", "X", "Y", "Start", "Back",
        "D-Pad Up", "D-Pad Down", "D-Pad Left", "D-Pad Right",
        "LB", "RB", "L3", "L-Left", "L-Right", "L-Up", "L-Down", "R3", "LT Button", "RT Button",
        "Guide",

        // Gamepad Axes
        "Left Stick X", "Left Stick Y", "Right Stick X", "Right Stick Y", "LT", "RT",

        // Gamepad 1
        "GP1 A", "GP1 B", "GP1 X", "GP1 Y", "GP1 Start", "GP1 Back",
        "GP1 D-Up", "GP1 D-Down", "GP1 D-Left", "GP1 D-Right",
        "GP1 LB", "GP1 RB", "GP1 L3", "GP1 L-Left", "GP1 L-Right", "GP1 L-Up", "GP1 L-Down",
        "GP1 R3", "GP1 LT Button", "GP1 RT Button",
        "GP1 LX", "GP1 LY", "GP1 RX", "GP1 RY", "GP1 LT", "GP1 RT",

        // Gamepad 2
        "GP2 A", "GP2 B", "GP2 X", "GP2 Y", "GP2 LB", "GP2 RB", "GP2 Start", "GP2 Back",
        "GP2 L3", "GP2 R3", "GP2 D-Up", "GP2 D-Down", "GP2 D-Left", "GP2 D-Right",
        "GP2 LX", "GP2 LY", "GP2 RX", "GP2 RY", "GP2 LT", "GP2 RT",

        // Gamepad 3
        "GP3 A", "GP3 B", "GP3 X", "GP3 Y", "GP3 LB", "GP3 RB", "GP3 Start", "GP3 Back",
        "GP3 L3", "GP3 R3", "GP3 D-Up", "GP3 D-Down", "GP3 D-Left", "GP3 D-Right",
        "GP3 LX", "GP3 LY", "GP3 RX", "GP3 RY", "GP3 LT", "GP3 RT",

        // Gamepad 4
        "GP4 A", "GP4 B", "GP4 X", "GP4 Y", "GP4 LB", "GP4 RB", "GP4 Start", "GP4 Back",
        "GP4 L3", "GP4 R3", "GP4 D-Up", "GP4 D-Down", "GP4 D-Left", "GP4 D-Right",
        "GP4 LX", "GP4 LY", "GP4 RX", "GP4 RY", "GP4 LT", "GP4 RT",
    ];

    public static string ToDisplayString(this InputCode code) =>
        (int)code < Names.Length ? Names[(int)code] : "";

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
