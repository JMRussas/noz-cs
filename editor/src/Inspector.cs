//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ.Editor;

internal static partial class Inspector
{
    private const int MaxProperties = 128;

    public struct AutoSection : IDisposable { readonly void IDisposable.Dispose() => EndSection(); }

    [ElementId("Property", MaxProperties)]
    private static partial class ElementId { }

    private static int _nextPropertyId = 0;
    private static int _propertyId;
    private static bool _propertyEnabled;
    private static string? _dropdownText = null!;

    private static int GetNextPropertyId() => _nextPropertyId++;

    public static void UpdateUI()
    {
        if (!(Workspace.ActiveEditor?.ShowInspector ?? false))
            return;

        _nextPropertyId = ElementId.Property;

        using (UI.BeginColumn(EditorStyle.Inspector.Root))
            Workspace.ActiveEditor.InspectorUI();        
    }

    public static AutoSection BeginSection(string name)
    {
        UI.BeginColumn(EditorStyle.Inspector.Section);
        UI.Label(name, EditorStyle.Text.Primary);

        return new AutoSection();
    }

    public static UI.AutoRow BeginRow()
    {
        return UI.BeginRow(EditorStyle.Inspector.Row);
    }

    public static void EndSection()
    {
        UI.EndColumn();        
    }

    public static bool Property(Action content, string? name = null, Sprite? icon = null, bool enabled = true)
    {
        _propertyId = _nextPropertyId++;
        _propertyEnabled = enabled;

        var hovered = UI.IsHovered(_propertyId);
        var pressed = false;
        using (UI.BeginRow(_propertyId, hovered ? EditorStyle.Control.RootHovered : EditorStyle.Control.Root))
        {
            if (icon != null)
                UI.Image(icon, EditorStyle.Icon.Secondary);

            if (name != null)
                UI.Label(name, EditorStyle.Text.Primary);

            content();

            pressed = UI.WasPressed();
        }

        return pressed;
    }

    public static void DropdownProperty(
        string text,
        Func<PopupMenuItem[]> getItems,
        string? name = null,
        Sprite? icon = null,
        bool enabled = true
    )
    {
        _dropdownText = text;

        static void DropdownContent()
        {
            UI.Label(_dropdownText!, EditorStyle.Text.Primary);

            if (UI.IsHovered())
                UI.Flex();

            UI.Image(EditorAssets.Sprites.IconFoldoutClosed, EditorStyle.Icon.SecondarySmall);
        }

        if (Property(DropdownContent, name: name, icon: icon, enabled: enabled))
        {
            var style = EditorStyle.Popup.Left with { AnchorRect = UI.GetElementWorldRect(_propertyId) };
            PopupMenu.Open(_propertyId, getItems(), style);
        }
    }

    public static bool ToggleProperty(Sprite icon, ref bool value, bool enabled=true)
    {
        var propertyId = GetNextPropertyId();

        var hovered = UI.IsHovered(propertyId);
        var pressed = false;
        using (UI.BeginContainer(propertyId, hovered ? EditorStyle.Inspector.ToggleButtonHovered : EditorStyle.Inspector.ToggleButton))
        {
            if (value)
                UI.Container(EditorStyle.Inspector.ToggleButtonChecked);

            UI.Image(icon, EditorStyle.Icon.Primary);

            pressed = UI.WasPressed();
        }

        if (pressed)
            value = !value;

        return pressed;
    }
}

