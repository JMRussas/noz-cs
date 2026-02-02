//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static class EditorUI
{
    public const int PopupId = 128;
    public const int FirstPopupItemId = PopupId + 1;
    private static readonly string[] OpacityStrings = ["0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%"];
    private static readonly string[] FrameTimeStrings = ["0", "4", "8", "12", "16", "20", "24", "28", "32", "36", "40", "44", "48", "52", "56", "60"];
    private static readonly float[] OpacityValues = [0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1.0f];

    private static bool _controlHovered = false;
    private static bool _controlSelected = false;
    private static bool _controlDisabled = false;
    private static int _popupId = -1;
    private static int _nextPopupItemId = FirstPopupItemId;

    private static float _opacityValue;
    private static int _colorValue;
    private static int _colorPalette;

    private static void SetState(bool selected, bool disabled)
    {
        _controlHovered = UI.IsHovered();
        _controlSelected = selected;
        _controlDisabled = disabled;
    }

    private static void ClearState()
    {
        _controlDisabled = false;
        _controlHovered = false;
        _controlSelected = false;
    }

    private static void ShortcutText(string text, bool selected = false)
    {
        UI.Label(text, style: selected ? EditorStyle.Control.Text : EditorStyle.Shortcut.Text);
    }

    private static void ShortcutText(InputCode code, bool selected = false) =>
        ShortcutText(code.ToDisplayString(), selected: selected);

    public static void Shortcut(InputCode code, bool ctrl, bool alt, bool shift, bool selected = false, Align align = Align.Min)
    {
        using (UI.BeginRow(EditorStyle.Shortcut.ListContainer with { AlignX = align }))
        {
            if (ctrl)
                ShortcutText(InputCode.KeyLeftCtrl, selected);
            if (alt)
                ShortcutText(InputCode.KeyLeftAlt, selected);
            if (shift)
                ShortcutText(InputCode.KeyLeftShift, selected);
            ShortcutText(code, selected);
        }
    }

    public static void Shortcut(Command command, bool selected = false) =>
        Shortcut(command.Key, command.Ctrl, command.Alt, command.Shift, selected);

    public static void ButtonFill(bool selected, bool hovered, bool disabled, bool toolbar = false)
    {
        if (disabled && toolbar)
            return;
        if (disabled)
            UI.Container(EditorStyle.Button.DisabledFill);
        else if (selected && hovered)
            UI.Container(EditorStyle.Button.SelectedHoverFill);
        else if (selected)
            UI.Container(EditorStyle.Button.SelectedFill);
        else if (hovered)
            UI.Container(EditorStyle.Button.HoverFill);
        else if (!toolbar)
            UI.Container(EditorStyle.Button.Fill);
    }

    public static bool Button(ElementId id, string text, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.Root))
        {
            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            using (UI.BeginContainer(EditorStyle.Button.TextContent))
                UI.Label(text, (disabled ? EditorStyle.Control.DisabledText : EditorStyle.Control.Text) with { AlignX = Align.Center, AlignY = Align.Center});
            pressed = !disabled && UI.WasPressed();
        }

        return pressed;
    }

    private static void ButtonIcon(Sprite icon)
    {
        using var _ = UI.BeginContainer(EditorStyle.Button.IconContent);
        if (_controlDisabled)
            UI.Image(icon, EditorStyle.Control.DisabledIcon);
        else if (_controlSelected)
            UI.Image(icon, EditorStyle.Control.SelectedIcon);
        else if (_controlHovered)
            UI.Image(icon, EditorStyle.Control.HoveredIcon);
        else
            UI.Image(icon, EditorStyle.Control.Icon);

    }

    public static bool Button(ElementId id, Sprite icon, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithIcon))
        {
            _controlDisabled = disabled;
            _controlHovered = UI.IsHovered();
            _controlSelected = selected;

            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            ButtonIcon(icon);
            pressed = !disabled && UI.WasPressed();
        }

        return pressed;
    }

    public static bool Button(ElementId id, Action content, bool selected = false, bool disabled = false, bool toolbar = false)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Button.RootWithContent))
        {
            ButtonFill(selected, UI.IsHovered(), disabled, toolbar: toolbar);
            using (UI.BeginContainer(EditorStyle.Button.Content))
                content.Invoke();
            pressed = UI.WasPressed();
        }

        return pressed;
    }

    public static void PopupIcon(Sprite icon, bool hovered = false, bool selected = false, bool disabled = false)
    {
        using (UI.BeginContainer(EditorStyle.Control.IconContainer))
            UI.Image(
                icon,
                style: disabled
                    ? EditorStyle.Control.DisabledIcon
                    : selected
                        ? EditorStyle.Control.SelectedIcon
                        : hovered
                            ? EditorStyle.Control.HoveredIcon
                            : EditorStyle.Control.Icon);
    }

    public static void PopupText(string text, bool hovered = false, bool selected = false, bool disabled = false)
    {
        UI.Label(
            text,
            style: disabled
                ? EditorStyle.Control.DisabledText
                : selected
                    ? EditorStyle.Control.SelectedText
                    : hovered
                        ? EditorStyle.Control.HoveredText
                        : EditorStyle.Control.Text);
    }

    private static void PopupItemFill()
    {
        if (_controlHovered)
            UI.Container(EditorStyle.Control.HoverFill);
    }

    public static bool PopupItem(Action? content = null, bool selected = false, bool disabled = false, bool showIcon=false) =>
        PopupItem(_nextPopupItemId++, null, null, content, selected, disabled, showIcon: showIcon);

    public static bool PopupItem(string text, Action? content = null, bool selected = false, bool disabled = false) =>
        PopupItem(_nextPopupItemId++, text, content, selected, disabled);

    public static bool PopupItem(
        ElementId id,
        string text,
        Action? content = null,
        bool selected = false,
        bool disabled = false) =>
        PopupItem(id, null, text, content, selected, disabled, showIcon: false);

    public static bool PopupItem(
        Sprite? icon,
        string text,
        Action? content = null,
        bool selected = false,
        bool disabled = false,
        bool showIcon = true) =>
        PopupItem(_nextPopupItemId++, icon, text, content, selected, disabled, showIcon);

    public static bool PopupItem(
        ElementId id,
        Sprite? icon,
        string? text,
        Action? content = null,
        bool selected = false,
        bool disabled = false,
        bool showIcon = true,
        bool showChecked = true)
    {
        var pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Popup.Item))
        { 
            SetState(selected, disabled);

            PopupItemFill();

            using (UI.BeginRow(EditorStyle.Popup.ItemContent))
            {
                if (showChecked)
                    ControlIcon(EditorStyle.Popup.CheckContent, selected ? EditorAssets.Sprites.IconCheck : null);
                if (showIcon)
                    ControlIcon(icon);
                if (text != null)
                    ControlText(text);

                if (content != null)
                    content?.Invoke();
            }

            pressed = UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    public static void OpenPopup(ElementId id)
    {
        _popupId = id;
    }

    public static void ClosePopup()
    {
        _popupId = -1;
    }

    public static void TogglePopup(ElementId id)
    {
        if (_popupId == id)
            _popupId = -1;
        else
            _popupId = id;
    }

    public static bool IsPopupOpen(ElementId id) =>
        _popupId == id;

    public static bool Popup(ElementId id, Action content, PopupStyle? style = null, Vector2 offset = default)
    {
        if (_popupId != id) return false;

        _nextPopupItemId = FirstPopupItemId;

        var anchorRect = UI.GetElementCanvasRect(id).Translate(offset);
        using var _ = UI.BeginPopup(
            PopupId,
            style ?? new PopupStyle
            {
                AnchorX = Align.Min,
                AnchorY = Align.Min,
                PopupAlignX = Align.Min,
                PopupAlignY = Align.Max,
                Spacing = 2,
                ClampToScreen = true,
                AnchorRect = anchorRect,
                MinWidth = anchorRect.Width
            });

        if (UI.IsClosed())
        {
            _popupId = -1;
            return false;
        }

        using var __ = UI.BeginColumn(EditorStyle.Popup.Root);

        content.Invoke();
        
        return _popupId == id;
    }

    public static void ControlFill(bool ignoreDefaultFill = false)
    {
        if (_controlDisabled)
            UI.Container(EditorStyle.Control.DisabledFill);
        else if (_controlSelected)
            UI.Container(EditorStyle.Control.SelectedFill);
        else if (_controlHovered)
            UI.Container(EditorStyle.Control.HoverFill);
        else if (!ignoreDefaultFill)
            UI.Container(EditorStyle.Control.Fill);
    }

    public static bool Control(
        ElementId id,
        Action content,
        bool selected = false,
        bool disabled = false,
        bool toolbar = false,
        bool padding = true)
    {
        bool pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Control.Root))
        {
            SetState(selected, disabled);
            ControlFill(ignoreDefaultFill: toolbar);

            using (UI.BeginRow(padding ? EditorStyle.Control.Content : EditorStyle.Control.ContentNoPadding))
                content.Invoke();

            pressed = !disabled && UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    public static void ControlPlaceholderText(string text)
    {
        UI.Label(
            text,
            style: _controlSelected
                ? EditorStyle.Control.PlaceholderSelectedText
                : _controlHovered
                    ? EditorStyle.Control.PlaceholderHoverText
                    : EditorStyle.Control.PlaceholderText);
    }

    public static void ControlText(string text)
    {
        UI.Label(
            text,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledText
                : _controlSelected
                    ? EditorStyle.Control.SelectedText
                    : _controlHovered
                        ? EditorStyle.Control.HoveredText
                        : EditorStyle.Control.Text);
    }

    private static void ControlIcon (in ContainerStyle style, Sprite? icon)
    {
        using var _ = UI.BeginContainer(style);
        if (icon == null) return;

        UI.Image(
            icon,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledIcon
                : _controlSelected
                    ? EditorStyle.Control.SelectedIcon
                    : _controlHovered
                        ? EditorStyle.Control.HoveredIcon
                        : EditorStyle.Control.Icon);
    }

    public static void ControlIcon(Sprite? icon) =>
        ControlIcon(EditorStyle.Control.IconContainer, icon);

    public static void ToolbarSpacer()
    {
        UI.Container(EditorStyle.Toolbar.Spacer);
    }

    private static UI.AutoContainer BeginColorContainer(ElementId id, bool selected)
    {
        var container = UI.BeginContainer(id, new ContainerStyle
        {
            Padding = EdgeInsets.All(3),
            Color = selected ? EditorStyle.SelectionColor : Color.Transparent,
            Border = new BorderStyle { Radius = EditorStyle.Control.BorderRadius }
        });

        if (!selected && UI.IsHovered())
            UI.Container(EditorStyle.Control.HoverFill with
            { 
                Margin = EdgeInsets.All(-3)
            });

        return container;
    }

    private static void ColorPopupContent()
    {
        using var _ = UI.BeginGrid(new GridStyle
        {
            CellHeight = EditorStyle.Control.Height,
            CellWidth = EditorStyle.Control.Height,
            Columns = 8
        });

        var nextItemId = FirstPopupItemId;
        var hasOpacity = _opacityValue > float.Epsilon;

        for (var i = 0; i < 64; i++)
        {
            using (BeginColorContainer(nextItemId++, selected: hasOpacity && i == _colorValue))
            {
                var displayColor = PaletteManager.GetColor(_colorPalette, i);
                if (displayColor.A <= float.Epsilon)
                {
                    UI.Container(new ContainerStyle { Color = Color.White2Pct });
                    continue;
                }

                UI.Container(new ContainerStyle
                {
                    Color = displayColor,
                    Border = new BorderStyle { Radius = EditorStyle.Control.BorderRadius - 2 }
                });

                if (UI.WasPressed())
                {
                    if (_opacityValue <= 0.0f)
                        _opacityValue = 1.0f;

                    _colorValue = i;
                    ClosePopup();
                }
            }
        }

        using (BeginColorContainer(nextItemId++, selected: _opacityValue <= float.MinValue))
        {
            UI.Image(EditorAssets.Sprites.IconSubtract, ImageStyle.Center);
            if (UI.WasPressed())
            {
                _opacityValue = float.MinValue;
                ClosePopup();
            }
        }

        using (BeginColorContainer(nextItemId++, selected: MathEx.Approximately(0, _opacityValue)))
        {
            UI.Image(EditorAssets.Sprites.IconNofill, ImageStyle.Center);
            if (UI.WasPressed())
            {
                _opacityValue = 0.0f;
                ClosePopup();
            }
        }

        for (var i = 0; i < 6; i++)
        {
            var o = OpacityValues[i];
            using (BeginColorContainer(nextItemId++, selected: MathEx.Approximately(o, _opacityValue)))
            {
                UI.Image(EditorAssets.Sprites.IconOpacity, style: EditorStyle.Control.Icon);
                UI.Image(
                    EditorAssets.Sprites.IconOpacityOverlay,
                    EditorStyle.Control.Icon with { Color = Color.White.WithAlpha(o) });

                if (UI.WasPressed())
                {
                    _opacityValue = o;
                    ClosePopup();
                }
            }
        }
    }

    private static void ColorPopup(ElementId id, int palette, ref int value, ref float opacity)
    {
        if (id != _popupId) return;

        _colorPalette = palette;
        _colorValue = value;
        _opacityValue = opacity;
        Popup(id, ColorPopupContent);
        value = _colorValue;
        opacity = _opacityValue;
    }

    public static bool ColorButton(
        ElementId id,
        int paletteId,
        ref int colorId,
        ref float opacity,
        Sprite? icon = null)
    {
        var oldValue = colorId;
        var oldOpacity = opacity;
        void ButtonContent()
        {
            if (icon != null)
                ControlIcon(icon);

            if (oldOpacity <= float.MinValue)
            {
                ControlIcon(EditorAssets.Sprites.IconSubtract);
                return;
            }

            if (MathEx.Approximately(oldOpacity, 0.0f))
            {
                ControlIcon(EditorAssets.Sprites.IconNofill);
                return;
            }

            using var _ = UI.BeginContainer(new ContainerStyle
            {
                Width = EditorStyle.Control.ContentHeight,
                Padding = EdgeInsets.All(2)
            });

            var displayColor = PaletteManager.GetColor(paletteId, oldValue).WithAlpha(oldOpacity);
            if (oldOpacity < 1.0f)
            {
                UI.Image(EditorAssets.Sprites.IconOpacity);
                UI.Image(EditorAssets.Sprites.IconOpacity, new ImageStyle { Color = displayColor });
            }
            else
            {
                UI.Container(new ContainerStyle
                {
                    Color = displayColor,
                    Border = new BorderStyle { Radius = 5 }
                });
            }
        }

        if (Control(id, ButtonContent, selected: false, disabled: false, toolbar: false))
            TogglePopup(id); ;

        ColorPopup(id: id, palette: paletteId, value: ref colorId, opacity: ref opacity);

        return colorId != oldValue || oldOpacity != opacity;
    }

    private static float OpacityPopup(ElementId id, float value, bool showSubtract)
    {
        void OpacityItem(float value)
        {
            using (UI.BeginContainer(EditorStyle.Control.IconContainer))
            {
                UI.Image(EditorAssets.Sprites.IconOpacity, style: EditorStyle.Control.Icon);
                UI.Image(
                    EditorAssets.Sprites.IconOpacityOverlay,
                    EditorStyle.Control.Icon with { Color = Color.White.WithAlpha(value) });
            }
            ControlText(OpacityStrings[(int)(value * 10)]);
        }

        void SubtractIcon()
        {
            PopupIcon(EditorAssets.Sprites.IconSubtract);
            ControlText("Subtract");
        }

        void Content()
        {
            if (showSubtract && PopupItem(SubtractIcon, selected: value <= float.MinValue))
            {
                ClosePopup();
                _opacityValue = float.MinValue;
                return;
            }

            for (int i = 0; i <= 10; i++)
            {
                var itemValue = i / 10.0f;
                if (PopupItem(() => OpacityItem(itemValue), itemValue == value))
                {
                    _opacityValue = i / 10.0f;
                    ClosePopup();
                }
            }
        }

        _opacityValue = value;
        Popup(id, Content, offset: new Vector2(-34,0));
        return _opacityValue;
    }

    public static bool OpacityButton(ElementId id, ref float value, bool showSubtract=false)
    {
        var oldValue = value;

        void ControlContent()
        {
            if (showSubtract && oldValue <= float.MinValue)
            {
                ControlIcon(EditorAssets.Sprites.IconSubtract);
                return;
            }

            using var _ = UI.BeginContainer(EditorStyle.Control.IconContainer);
            UI.Image(EditorAssets.Sprites.IconOpacity, EditorStyle.Control.Icon);
            UI.Image(
                EditorAssets.Sprites.IconOpacityOverlay,
                EditorStyle.Control.Icon with { Color = Color.White.WithAlpha(oldValue) });
        }

        if (Control(id, ControlContent))
            TogglePopup(id);

        value = OpacityPopup(id, value: value, showSubtract: showSubtract);

        return oldValue != value;
    }

    public struct DopeSheetFrame
    {
        public int Hold;
    }

    public static bool DopeSheet(int baseId, ReadOnlySpan<DopeSheetFrame> frames, ref int currentFrame, int maxFrames, bool isPlaying)
    {
        int oldCurrentFrame = currentFrame;

        using var _ = UI.BeginColumn();

        UI.Container(EditorStyle.Dopesheet.LayerSeparator);

        using (UI.BeginRow(EditorStyle.Dopesheet.HeaderContainer))
        {
            UI.Container(EditorStyle.Dopesheet.FrameSeparator);
            var blockCount = maxFrames / 4;
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                using (UI.BeginContainer(EditorStyle.Dopesheet.TimeBlock))
                    UI.Label(FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);

                UI.Container(EditorStyle.Dopesheet.FrameSeparator);
            }
        }

        UI.Container(EditorStyle.Dopesheet.LayerSeparator);

        using (UI.BeginRow(EditorStyle.Dopesheet.FrameContainer))
        {
            UI.Container(EditorStyle.Dopesheet.FrameSeparator);

            var slotIndex = 0;
            var frameIndex = 0;
            for (; frameIndex < frames.Length; frameIndex++)
            {
                var selected = oldCurrentFrame == frameIndex;
                using (UI.BeginRow(baseId + frameIndex, ContainerStyle.Fit))
                {
                    if (UI.WasPressed())
                        currentFrame = frameIndex;

                    using (UI.BeginContainer(selected
                        ? EditorStyle.Dopesheet.SelectedFrame
                        : EditorStyle.Dopesheet.Frame))
                    {
                        UI.Container(selected
                            ? EditorStyle.Dopesheet.SelectedFrameDot
                            : EditorStyle.Dopesheet.FrameDot);

                    }

                    slotIndex++;

                    int hold = frames[frameIndex].Hold;
                    if (hold <= 0)
                    {
                        UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                        continue;
                    }

                    for (int holdIndex = 0; holdIndex < hold && slotIndex < maxFrames; holdIndex++, slotIndex++)
                    {
                        using (UI.BeginContainer(selected
                            ? EditorStyle.Dopesheet.SelectedFrame
                            : EditorStyle.Dopesheet.Frame))
                        {
                            //UI.Container(selected
                            //    ? EditorStyle.Dopesheet.SelectedFrameDot
                            //    : EditorStyle.Dopesheet.FrameDot);
                            //if (UI.WasPressed())
                            //    currentFrame = slotIndex;
                        }

                        if (holdIndex < hold - 1)
                        {
                            UI.Container(selected
                                ? EditorStyle.Dopesheet.SelectedHoldSeparator
                                : EditorStyle.Dopesheet.HoldSeparator);
                        }
                    }

                    UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                }
            }

            for (; slotIndex < maxFrames; slotIndex++)
            {
                UI.Container(slotIndex % 4 == 0
                    ? EditorStyle.Dopesheet.FourthEmptyFrame
                    : EditorStyle.Dopesheet.EmptyFrame);

                UI.Container(EditorStyle.Dopesheet.FrameSeparator);
            }
        }

        UI.Container(EditorStyle.Dopesheet.LayerSeparator);

        return oldCurrentFrame != currentFrame;
    }
}
