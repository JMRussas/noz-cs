//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Platform;

public struct PlatformEvent
{
    public PlatformEventType Type;

    // Keyboard
    public InputCode KeyCode;

    // Text Input
    public string? Text;

    // Mouse
    public InputCode MouseButton;
    public Vector2 MousePosition;
    public float ScrollX;
    public float ScrollY;
    public int ClickCount;

    // Gamepad
    public InputCode GamepadButton;
    public InputCode GamepadAxis;
    public float AxisValue;

    // Window
    public int WindowWidth;
    public int WindowHeight;

    public static PlatformEvent KeyDown(InputCode code) => new() { Type = PlatformEventType.KeyDown, KeyCode = code };
    public static PlatformEvent KeyUp(InputCode code) => new() { Type = PlatformEventType.KeyUp, KeyCode = code };
    public static PlatformEvent TextInputEvent(string text) => new() { Type = PlatformEventType.TextInput, Text = text };

    public static PlatformEvent MouseDown(InputCode button, int clickCount = 1) => new() { Type = PlatformEventType.MouseButtonDown, MouseButton = button, ClickCount = clickCount };
    public static PlatformEvent MouseUp(InputCode button) => new() { Type = PlatformEventType.MouseButtonUp, MouseButton = button };
    public static PlatformEvent MouseMove(Vector2 position) => new() { Type = PlatformEventType.MouseMove, MousePosition = position };
    public static PlatformEvent MouseScroll(float x, float y) => new() { Type = PlatformEventType.MouseScroll, ScrollX = x, ScrollY = y };

    public static PlatformEvent GamepadDown(InputCode button) => new() { Type = PlatformEventType.GamepadButtonDown, GamepadButton = button };
    public static PlatformEvent GamepadUp(InputCode button) => new() { Type = PlatformEventType.GamepadButtonUp, GamepadButton = button };
    public static PlatformEvent GamepadAxisMove(InputCode axis, float value) => new() { Type = PlatformEventType.GamepadAxis, GamepadAxis = axis, AxisValue = value };

    public static PlatformEvent Resize(int width, int height) => new() { Type = PlatformEventType.WindowResize, WindowWidth = width, WindowHeight = height };
    public static PlatformEvent MouseEnter() => new() { Type = PlatformEventType.WindowMouseEnter };
    public static PlatformEvent MouseLeave() => new() { Type = PlatformEventType.WindowMouseLeave };
}
