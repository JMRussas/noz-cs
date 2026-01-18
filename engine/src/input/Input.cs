//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ.Platform;

namespace NoZ;

public static class Input
{
    private static readonly bool[] ButtonState = new bool[(int)InputCode.Count];
    private static readonly bool[] PhysicalButtonState = new bool[(int)InputCode.Count];
    private static readonly bool[] ButtonPressedThisFrame = new bool[(int)InputCode.Count];
    private static readonly bool[] ButtonReleasedThisFrame = new bool[(int)InputCode.Count];
    private static readonly bool[] ButtonRepeatThisFrame = new bool[(int)InputCode.Count];
    private static readonly bool[] ButtonConsumed = new bool[(int)InputCode.Count];
    private static readonly float[] ButtonHeldTime = new float[(int)InputCode.Count];
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
        Array.Clear(ButtonPressedThisFrame);
        Array.Clear(ButtonReleasedThisFrame);
        Array.Clear(ButtonRepeatThisFrame);

        // Clear consumed flags for buttons that are physically released
        for (var i = 0; i < (int)InputCode.Count; i++)
        {
            if (ButtonConsumed[i] && !PhysicalButtonState[i])
                ButtonConsumed[i] = false;
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
            if (!ButtonState[i])
            {
                ButtonHeldTime[i] = 0;
                continue;
            }

            var prevTime = ButtonHeldTime[i];
            ButtonHeldTime[i] += Time.DeltaTime;

            if (prevTime < RepeatDelay)
            {
                if (ButtonHeldTime[i] >= RepeatDelay)
                    ButtonRepeatThisFrame[i] = true;
            }
            else
            {
                var prevRepeats = (int)((prevTime - RepeatDelay) / RepeatInterval);
                var newRepeats = (int)((ButtonHeldTime[i] - RepeatDelay) / RepeatInterval);
                if (newRepeats > prevRepeats)
                    ButtonRepeatThisFrame[i] = true;
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
                    PhysicalButtonState[(int)evt.KeyCode] = true;
                    if (!ButtonConsumed[(int)evt.KeyCode])
                    {
                        if (!ButtonState[(int)evt.KeyCode])
                            ButtonPressedThisFrame[(int)evt.KeyCode] = true;
                        ButtonState[(int)evt.KeyCode] = true;
                    }
                }
                break;

            case PlatformEventType.KeyUp:
                if (evt.KeyCode != InputCode.None)
                {
                    PhysicalButtonState[(int)evt.KeyCode] = false;
                    ButtonState[(int)evt.KeyCode] = false;
                    ButtonReleasedThisFrame[(int)evt.KeyCode] = true;
                }
                break;

            case PlatformEventType.TextInput:
                if (!string.IsNullOrEmpty(evt.Text))
                    _textInput += evt.Text;
                break;

            case PlatformEventType.MouseButtonDown:
                if (evt.MouseButton != InputCode.None)
                {
                    PhysicalButtonState[(int)evt.MouseButton] = true;
                    if (!ButtonConsumed[(int)evt.MouseButton])
                    {
                        if (!ButtonState[(int)evt.MouseButton])
                            ButtonPressedThisFrame[(int)evt.MouseButton] = true;
                        ButtonState[(int)evt.MouseButton] = true;
                    }
                }

                if (evt.ClickCount == 2 && evt.MouseButton == InputCode.MouseLeft && !ButtonConsumed[(int)InputCode.MouseLeftDoubleClick])
                    ButtonPressedThisFrame[(int)InputCode.MouseLeftDoubleClick] = true;
                break;

            case PlatformEventType.MouseButtonUp:
                if (evt.MouseButton != InputCode.None)
                {
                    PhysicalButtonState[(int)evt.MouseButton] = false;
                    ButtonState[(int)evt.MouseButton] = false;
                    ButtonReleasedThisFrame[(int)evt.MouseButton] = true;
                }

                ButtonState[(int)InputCode.MouseLeftDoubleClick] = false;
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
                    PhysicalButtonState[(int)evt.GamepadButton] = true;
                    ButtonState[(int)evt.GamepadButton] = true;
                }
                break;

            case PlatformEventType.GamepadButtonUp:
                if (evt.GamepadButton != InputCode.None)
                {
                    PhysicalButtonState[(int)evt.GamepadButton] = false;
                    ButtonState[(int)evt.GamepadButton] = false;
                }
                break;

            case PlatformEventType.GamepadAxis:
                if (evt.GamepadAxis != InputCode.None)
                {
                    AxisState[(int)evt.GamepadAxis] = evt.AxisValue;

                    switch (evt.GamepadAxis)
                    {
                        case InputCode.GamepadLeftStickX:
                            ButtonState[(int)InputCode.GamepadLeftStickLeft] = evt.AxisValue < -0.5f;
                            ButtonState[(int)InputCode.GamepadLeftStickRight] = evt.AxisValue > 0.5f;
                            break;
                        case InputCode.GamepadLeftStickY:
                            ButtonState[(int)InputCode.GamepadLeftStickUp] = evt.AxisValue < -0.5f;
                            ButtonState[(int)InputCode.GamepadLeftStickDown] = evt.AxisValue > 0.5f;
                            break;
                        case InputCode.GamepadLeftTrigger:
                            AxisState[(int)evt.GamepadAxis] = (evt.AxisValue + 1f) / 2f;
                            ButtonState[(int)InputCode.GamepadLeftTriggerButton] = evt.AxisValue > 0.5f;
                            break;
                        case InputCode.GamepadRightTrigger:
                            AxisState[(int)evt.GamepadAxis] = (evt.AxisValue + 1f) / 2f;
                            ButtonState[(int)InputCode.GamepadRightTriggerButton] = evt.AxisValue > 0.5f;
                            break;
                    }
                }
                break;

            case PlatformEventType.WindowFocus:
                for (var i = 0; i < (int)InputCode.Count; i++)
                {
                    if (ButtonState[i])
                        ButtonConsumed[i] = true;
                }
                break;
        }
    }

    internal static bool IsButtonDownRaw(InputCode code) => ButtonState[(int)code];
    internal static bool WasButtonPressedRaw(InputCode code) => ButtonPressedThisFrame[(int)code];
    internal static bool WasButtonReleasedRaw(InputCode code) => ButtonReleasedThisFrame[(int)code];
    internal static bool WasButtonRepeatRaw(InputCode code) => ButtonRepeatThisFrame[(int)code];

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

    public static bool IsButtonDown(InputCode code) => ButtonState[(int)code];
    public static bool WasButtonPressed(InputCode code) => ButtonPressedThisFrame[(int)code];
    public static bool WasButtonPressed(InputCode code, bool allowRepeat) =>
        ButtonPressedThisFrame[(int)code] || (allowRepeat && ButtonRepeatThisFrame[(int)code]);
    public static bool WasButtonReleased(InputCode code) => ButtonReleasedThisFrame[(int)code] && !ButtonConsumed[(int)code];
    public static float GetAxis(InputCode code) => GetAxisValue(code);

    public static void ConsumeButton(InputCode code)
    {
        Log.Debug($"ConsumeButton: {code}");
        ButtonPressedThisFrame[(int)code] = false;
        ButtonReleasedThisFrame[(int)code] = false;
        ButtonState[(int)code] = false;
        ButtonConsumed[(int)code] = true;
    }

    public static bool IsShiftDown() => IsButtonDown(InputCode.KeyLeftShift) || IsButtonDown(InputCode.KeyRightShift);
    public static bool IsCtrlDown() => IsButtonDown(InputCode.KeyLeftCtrl) || IsButtonDown(InputCode.KeyRightCtrl);
    public static bool IsAltDown() => IsButtonDown(InputCode.KeyLeftAlt) || IsButtonDown(InputCode.KeyRightAlt);
    public static bool IsSuperDown() => IsButtonDown(InputCode.KeyLeftSuper) || IsButtonDown(InputCode.KeyRightSuper);

    public static string GetTextInput() => _textInput;

    public static Vector2 MousePosition { get; private set; }
}
