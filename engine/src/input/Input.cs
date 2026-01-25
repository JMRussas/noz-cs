//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ.Platform;

namespace NoZ;

internal struct InputButton
{
    public bool Physical;
    public bool Logical;
    public bool Pressed;
    public bool Released;
    public bool Repeat;
    public bool Consumed;
    public float HeldTime;
}

public static class Input
{
    private static readonly InputButton[] Buttons = new InputButton[(int)InputCode.Count];
    private static readonly float[] AxisState = new float[(int)InputCode.Count];

    private const float RepeatDelay = 0.4f;
    private const float RepeatInterval = 0.05f;

    private static float _scrollX;
    private static float _scrollY;
    private static string _textInput = string.Empty;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
    }

    public static void BeginFrame()
    {
        _scrollX = 0;
        _scrollY = 0;
        _textInput = string.Empty;

        for (var i = 0; i < (int)InputCode.Count; i++)
        {
            Buttons[i].Pressed = false;
            Buttons[i].Released = false;
            Buttons[i].Repeat = false;

            // Clear consumed flags for buttons that are physically released
            if (Buttons[i].Consumed && !Buttons[i].Physical)
                Buttons[i].Consumed = false;
        }
    }

    public static void Update()
    {
        UpdateRepeat();
    }

    private static void UpdateRepeat()
    {
        for (var i = 0; i < (int)InputCode.Count; i++)
        {
            ref var btn = ref Buttons[i];

            if (!btn.Logical)
            {
                btn.HeldTime = 0;
                continue;
            }

            var prevTime = btn.HeldTime;
            btn.HeldTime += Time.DeltaTime;

            if (prevTime < RepeatDelay)
            {
                if (btn.HeldTime >= RepeatDelay)
                    btn.Repeat = true;
            }
            else
            {
                var prevRepeats = (int)((prevTime - RepeatDelay) / RepeatInterval);
                var newRepeats = (int)((btn.HeldTime - RepeatDelay) / RepeatInterval);
                if (newRepeats > prevRepeats)
                    btn.Repeat = true;
            }
        }
    }

    public static void ProcessEvent(PlatformEvent evt)
    {
        switch (evt.Type)
        {
            case PlatformEventType.KeyDown:
                if (evt.KeyCode != InputCode.None)
                {
                    ref var btn = ref Buttons[(int)evt.KeyCode];
                    btn.Physical = true;
                    if (!btn.Consumed)
                    {
                        if (!btn.Logical)
                            btn.Pressed = true;
                        btn.Logical = true;
                    }
                }
                break;

            case PlatformEventType.KeyUp:
                if (evt.KeyCode != InputCode.None)
                {
                    ref var btn = ref Buttons[(int)evt.KeyCode];
                    btn.Physical = false;
                    btn.Logical = false;
                    btn.Released = true;
                }
                break;

            case PlatformEventType.TextInput:
                if (!string.IsNullOrEmpty(evt.Text))
                    _textInput += evt.Text;
                break;

            case PlatformEventType.MouseButtonDown:
                if (evt.MouseButton != InputCode.None)
                {
                    ref var btn = ref Buttons[(int)evt.MouseButton];
                    btn.Physical = true;
                    if (!btn.Consumed)
                    {
                        if (!btn.Logical)
                            btn.Pressed = true;
                        btn.Logical = true;
                    }
                }

                if (evt.ClickCount == 2 &&
                    evt.MouseButton == InputCode.MouseLeft &&
                    !Buttons[(int)InputCode.MouseLeftDoubleClick].Consumed)
                    Buttons[(int)InputCode.MouseLeftDoubleClick].Pressed = true;
                break;

            case PlatformEventType.MouseButtonUp:
                if (evt.MouseButton != InputCode.None)
                {
                    ref var btn = ref Buttons[(int)evt.MouseButton];
                    btn.Physical = false;
                    btn.Logical = false;
                    btn.Released = true;
                }

                Buttons[(int)InputCode.MouseLeftDoubleClick].Logical = false;
                break;

            case PlatformEventType.MouseMove:
                MousePosition = evt.MousePosition;
                break;

            case PlatformEventType.MouseScroll:
                _scrollX = evt.ScrollX;
                _scrollY = evt.ScrollY;
                break;

            case PlatformEventType.GamepadButtonDown:
                if (evt.GamepadButton != InputCode.None)
                {
                    ref var btn = ref Buttons[(int)evt.GamepadButton];
                    btn.Physical = true;
                    btn.Logical = true;
                }
                break;

            case PlatformEventType.GamepadButtonUp:
                if (evt.GamepadButton != InputCode.None)
                {
                    ref var btn = ref Buttons[(int)evt.GamepadButton];
                    btn.Physical = false;
                    btn.Logical = false;
                }
                break;

            case PlatformEventType.GamepadAxis:
                if (evt.GamepadAxis != InputCode.None)
                {
                    AxisState[(int)evt.GamepadAxis] = evt.AxisValue;

                    switch (evt.GamepadAxis)
                    {
                        case InputCode.GamepadLeftStickX:
                            Buttons[(int)InputCode.GamepadLeftStickLeft].Logical = evt.AxisValue < -0.5f;
                            Buttons[(int)InputCode.GamepadLeftStickRight].Logical = evt.AxisValue > 0.5f;
                            break;
                        case InputCode.GamepadLeftStickY:
                            Buttons[(int)InputCode.GamepadLeftStickUp].Logical = evt.AxisValue < -0.5f;
                            Buttons[(int)InputCode.GamepadLeftStickDown].Logical = evt.AxisValue > 0.5f;
                            break;
                        case InputCode.GamepadLeftTrigger:
                            AxisState[(int)evt.GamepadAxis] = (evt.AxisValue + 1f) / 2f;
                            Buttons[(int)InputCode.GamepadLeftTriggerButton].Logical = evt.AxisValue > 0.5f;
                            break;
                        case InputCode.GamepadRightTrigger:
                            AxisState[(int)evt.GamepadAxis] = (evt.AxisValue + 1f) / 2f;
                            Buttons[(int)InputCode.GamepadRightTriggerButton].Logical = evt.AxisValue > 0.5f;
                            break;
                    }
                }
                break;

            case PlatformEventType.WindowFocus:
                for (var i = 0; i < (int)InputCode.Count; i++)
                {
                    if (Buttons[i].Logical)
                        Buttons[i].Consumed = true;
                }
                break;
        }
    }

    internal static bool IsButtonDownRaw(InputCode code) => Buttons[(int)code].Logical;
    internal static bool WasButtonPressedRaw(InputCode code) => Buttons[(int)code].Pressed;
    internal static bool WasButtonReleasedRaw(InputCode code) => Buttons[(int)code].Released;
    internal static bool WasButtonRepeatRaw(InputCode code) => Buttons[(int)code].Repeat;

    internal static float GetAxisValue(InputCode code)
    {
        return code switch
        {
            InputCode.MouseX => MousePosition.X,
            InputCode.MouseY => MousePosition.Y,
            InputCode.MouseScrollX => _scrollX,
            InputCode.MouseScrollY => _scrollY,
            _ => AxisState[(int)code]
        };
    }

    public static bool IsButtonDown(InputCode code) => Buttons[(int)code].Logical;
    public static bool WasButtonPressed(InputCode code) => Buttons[(int)code].Pressed && !Buttons[(int)code].Consumed;
    public static bool WasButtonPressed(InputCode code, bool allowRepeat) =>
        (Buttons[(int)code].Pressed || (allowRepeat && Buttons[(int)code].Repeat)) && !Buttons[(int)code].Consumed;
    public static bool WasButtonReleased(InputCode code) => Buttons[(int)code].Released && !Buttons[(int)code].Consumed;
    public static float GetAxis(InputCode code) => GetAxisValue(code);

    public static void ConsumeButton(InputCode code)
    {
        ref var btn = ref Buttons[(int)code];
        btn.Pressed = false;
        btn.Released = false;
        btn.Logical = false;
        btn.Consumed = true;
    }

    public static bool IsShiftDown() => IsButtonDown(InputCode.KeyLeftShift) || IsButtonDown(InputCode.KeyRightShift);
    public static bool IsCtrlDown() => IsButtonDown(InputCode.KeyLeftCtrl) || IsButtonDown(InputCode.KeyRightCtrl);
    public static bool IsAltDown() => IsButtonDown(InputCode.KeyLeftAlt) || IsButtonDown(InputCode.KeyRightAlt);
    public static bool IsSuperDown() => IsButtonDown(InputCode.KeyLeftSuper) || IsButtonDown(InputCode.KeyRightSuper);

    public static string GetTextInput() => _textInput;

    public static Vector2 MousePosition { get; private set; }
}
