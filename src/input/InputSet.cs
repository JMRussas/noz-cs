//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

[Flags]
internal enum ButtonState : byte
{
    None = 0,
    Pressed = 1 << 0,
    Released = 1 << 1,
    Down = 1 << 2,
    Reset = 1 << 3,
    Enabled = 1 << 4
}

public class InputSet
{
    private readonly string _name;
    private readonly ButtonState[] _buttons = new ButtonState[(int)InputCode.Count];
    private readonly List<InputCode> _enabledCodes = new();
    private bool _active;

    public string Name => _name;
    public bool IsActive => _active;

    public InputSet(string name = "")
    {
        _name = name;
        Reset();
    }

    public void SetActive(bool active)
    {
        _active = active;
    }

    public void EnableButton(InputCode code)
    {
        if ((_buttons[(int)code] & ButtonState.Enabled) != 0)
            return;

        _enabledCodes.Add(code);
        _buttons[(int)code] |= ButtonState.Enabled;
    }

    public void DisableButton(InputCode code)
    {
        if ((_buttons[(int)code] & ButtonState.Enabled) == 0)
            return;

        _enabledCodes.Remove(code);
        _buttons[(int)code] = (_buttons[(int)code] & ~ButtonState.Enabled) | ButtonState.Reset;
    }

    public void EnableCharacters()
    {
        for (var i = InputCode.KeyA; i <= InputCode.KeyZ; i++)
            EnableButton(i);

        for (var i = InputCode.Key0; i <= InputCode.Key9; i++)
            EnableButton(i);

        EnableButton(InputCode.KeySpace);
        EnableButton(InputCode.KeyMinus);
    }

    public void EnableModifiers()
    {
        EnableButton(InputCode.KeyLeftShift);
        EnableButton(InputCode.KeyRightShift);
        EnableButton(InputCode.KeyLeftCtrl);
        EnableButton(InputCode.KeyRightCtrl);
        EnableButton(InputCode.KeyLeftAlt);
        EnableButton(InputCode.KeyRightAlt);
    }

    public bool IsButtonDown(InputCode code)
    {
        var state = _buttons[(int)code];
        if ((state & ButtonState.Reset) != 0)
            return false;
        return (state & ButtonState.Down) != 0;
    }

    public bool WasButtonPressed(InputCode code)
    {
        return (_buttons[(int)code] & ButtonState.Pressed) != 0;
    }

    public bool WasButtonReleased(InputCode code)
    {
        return (_buttons[(int)code] & ButtonState.Released) != 0;
    }

    public float GetAxis(InputCode code)
    {
        if (!_active)
            return 0.0f;
        return Input.GetAxisValue(code);
    }

    public void ConsumeButton(InputCode code)
    {
        _buttons[(int)code] = (_buttons[(int)code] & ButtonState.Enabled) | ButtonState.Reset;
    }

    public void Reset()
    {
        for (var i = 0; i < (int)InputCode.Count; i++)
            _buttons[i] = (_buttons[i] & ButtonState.Enabled) | ButtonState.Reset;

        UpdateButtonStates(true);
    }

    public void CopyFrom(InputSet src)
    {
        for (var i = 0; i < (int)InputCode.Count; i++)
        {
            if ((_buttons[i] & ButtonState.Enabled) != 0)
                _buttons[i] = src._buttons[i] | ButtonState.Enabled;
        }
    }

    internal void Update()
    {
        // Clear pressed/released flags from previous frame
        for (var i = 0; i < (int)InputCode.Count; i++)
            _buttons[i] &= ~(ButtonState.Pressed | ButtonState.Released);

        UpdateButtonStates(false);
    }

    private void UpdateButtonStates(bool reset)
    {
        foreach (var code in _enabledCodes)
        {
            var newState = Input.IsButtonDownRaw(code);
            UpdateButtonState(code, newState, reset);
        }
    }

    private void UpdateButtonState(InputCode code, bool newState, bool reset)
    {
        var idx = (int)code;
        if ((_buttons[idx] & ButtonState.Enabled) == 0)
            return;

        var oldState = (_buttons[idx] & ButtonState.Down) != 0;

        if (newState)
            _buttons[idx] |= ButtonState.Down;
        else
            _buttons[idx] &= ~ButtonState.Down;

        if (reset)
            return;

        // If button was reset, wait for release before allowing new presses
        if ((_buttons[idx] & ButtonState.Reset) != 0)
        {
            if (!newState)
                _buttons[idx] &= ~ButtonState.Reset;
            return;
        }

        // Detect state transitions
        if (newState != oldState)
        {
            if (newState)
                _buttons[idx] |= ButtonState.Pressed;
            else
                _buttons[idx] |= ButtonState.Released;
        }
    }
}
