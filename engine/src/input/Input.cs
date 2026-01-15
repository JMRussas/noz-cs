//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ.Platform;

namespace NoZ;

public static class Input
{
    private static readonly bool[] ButtonState = new bool[(int)InputCode.Count];
    private static readonly bool[] ButtonPressedThisFrame = new bool[(int)InputCode.Count];
    private static readonly float[] AxisState = new float[(int)InputCode.Count];
    private static readonly Stack<InputSet> InputSetStack = new();

    private static float _scrollX;
    private static float _scrollY;
    private static string _textInput = string.Empty;

    public static InputSet? CurrentInputSet => InputSetStack.Count > 0 ? InputSetStack.Peek() : null;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
        InputSetStack.Clear();
    }

    public static void PushInputSet(InputSet set, bool inheritState = false)
    {
        if (inheritState && InputSetStack.Count > 0)
            set.CopyFrom(InputSetStack.Peek());

        CurrentInputSet?.Reset();
        CurrentInputSet?.SetActive(false);

        InputSetStack.Push(set);
        set.SetActive(true);
    }

    public static void PopInputSet()
    {
        if (InputSetStack.Count == 0)
            return;

        var old = InputSetStack.Pop();
        old.SetActive(false);

        if (InputSetStack.Count > 0)
        {
            var current = InputSetStack.Peek();
            current.SetActive(true);
            current.Reset();
        }
    }

    public static void SetInputSet(InputSet set)
    {
        while (InputSetStack.Count > 0)
            InputSetStack.Pop().SetActive(false);

        InputSetStack.Push(set);
        set.SetActive(true);
    }

    public static void BeginFrame()
    {
        _scrollX = 0;
        _scrollY = 0;
        _textInput = string.Empty;
        Array.Clear(ButtonPressedThisFrame);
    }

    public static void Update()
    {
        CurrentInputSet?.Update();
    }

    public static void ProcessEvent(PlatformEvent evt)
    {
        switch (evt.Type)
        {
            case PlatformEventType.KeyDown:
                if (evt.KeyCode != InputCode.None)
                {
                    if (!ButtonState[(int)evt.KeyCode])
                        ButtonPressedThisFrame[(int)evt.KeyCode] = true;
                    ButtonState[(int)evt.KeyCode] = true;
                }
                break;

            case PlatformEventType.KeyUp:
                if (evt.KeyCode != InputCode.None)
                    ButtonState[(int)evt.KeyCode] = false;
                break;

            case PlatformEventType.TextInput:
                if (!string.IsNullOrEmpty(evt.Text))
                    _textInput += evt.Text;
                break;

            case PlatformEventType.MouseButtonDown:
                if (evt.MouseButton != InputCode.None)
                {
                    if (!ButtonState[(int)evt.MouseButton])
                        ButtonPressedThisFrame[(int)evt.MouseButton] = true;
                    ButtonState[(int)evt.MouseButton] = true;
                }

                if (evt.ClickCount == 2 && evt.MouseButton == InputCode.MouseLeft)
                    ButtonPressedThisFrame[(int)InputCode.MouseLeftDoubleClick] = true;
                break;

            case PlatformEventType.MouseButtonUp:
                if (evt.MouseButton != InputCode.None)
                    ButtonState[(int)evt.MouseButton] = false;

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
                    ButtonState[(int)evt.GamepadButton] = true;
                break;

            case PlatformEventType.GamepadButtonUp:
                if (evt.GamepadButton != InputCode.None)
                    ButtonState[(int)evt.GamepadButton] = false;
                break;

            case PlatformEventType.GamepadAxis:
                if (evt.GamepadAxis != InputCode.None)
                {
                    AxisState[(int)evt.GamepadAxis] = evt.AxisValue;

                    // Generate virtual button states from analog sticks
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
        }
    }

    internal static bool IsButtonDownRaw(InputCode code) => ButtonState[(int)code];
    internal static bool WasButtonPressedRaw(InputCode code) => ButtonPressedThisFrame[(int)code];

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

    public static bool IsButtonDown(InputCode code) => CurrentInputSet?.IsButtonDown(code) ?? false;
    public static bool WasButtonPressed(InputCode code) => CurrentInputSet?.WasButtonPressed(code) ?? false;
    public static bool WasButtonReleased(InputCode code) => CurrentInputSet?.WasButtonReleased(code) ?? false;
    public static float GetAxis(InputCode code) => CurrentInputSet?.GetAxis(code) ?? 0.0f;
    public static void ConsumeButton(InputCode code) => CurrentInputSet?.ConsumeButton(code);

    public static bool IsShiftDown() => IsButtonDown(InputCode.KeyLeftShift) || IsButtonDown(InputCode.KeyRightShift);
    public static bool IsCtrlDown() => IsButtonDown(InputCode.KeyLeftCtrl) || IsButtonDown(InputCode.KeyRightCtrl);
    public static bool IsAltDown() => IsButtonDown(InputCode.KeyLeftAlt) || IsButtonDown(InputCode.KeyRightAlt);
    public static bool IsSuperDown() => IsButtonDown(InputCode.KeyLeftSuper) || IsButtonDown(InputCode.KeyRightSuper);

    public static string GetTextInput() => _textInput;

    public static Vector2 MousePosition { get; private set; }
}
