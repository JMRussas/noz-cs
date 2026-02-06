//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class ConfirmDialog
{
    [ElementId("Yes")]
    [ElementId("No")]
    private static partial class ElementId { }

    private static bool _visible;
    private static string _message = string.Empty;
    private static Action? _onConfirm;
    private static string _yesText = "Yes";
    private static string _noText = "No";

    public static bool IsVisible => _visible;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
        _visible = false;
        _message = string.Empty;
        _onConfirm = null;
    }

    public static void Show(string message, Action onConfirm, string yes="Yes", string no="No")
    {
        _yesText = yes;
        _noText = no; 
        _message = message;
        _onConfirm = onConfirm;
        _visible = true;
        UI.SetFocus(ElementId.No);
    }

    public static void Close()
    {
        _visible = false;
        _message = string.Empty;
        _onConfirm = null;
        UI.ClearFocus();
    }

    public static void Update()
    {
        if (!_visible)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            Close();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            var callback = _onConfirm;
            Close();
            callback?.Invoke();
            return;
        }
    }

    public static void UpdateUI()
    {
        if (!_visible)
            return;

        Action? executed = null;

        using (UI.BeginContainer(id: 0))
                if (UI.WasPressed())
                    Close();

            using (UI.BeginColumn(EditorStyle.Confirm.Root))
            {
                UI.Label(_message, EditorStyle.Confirm.MessageLabel);

                using (UI.BeginRow(EditorStyle.Confirm.ButtonContainer))
                {
                    if (EditorUI.Button(ElementId.Yes, _yesText, selected: true))
                        executed = _onConfirm;

                if (EditorUI.Button(ElementId.No, _noText))
                    Close();
            }
        }

        if (executed != null)
        {
            Close();
            executed();
        }
    }
}
