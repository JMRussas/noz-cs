# NoZ.Widgets - Reusable Widget Library

## Context

The game currently has `GameUI` (internal, game-specific) and the editor has `EditorUI` (internal, editor-specific). Both implement the same widget patterns (Button, Slider, Toggle, etc.) on top of the NoZ `UI` primitives. We want a shared, styleable widget library (`NoZ.Widgets`) that any NoZ game can use, without polluting the core UI system.

Two problems must be solved:
1. **ElementId collisions**: The source generator assigns IDs per-project starting at 1. A library project's IDs would collide with the game's IDs at runtime.
2. **Styleable widgets**: Games need to customize widget colors/dimensions without forking the library code.

## Plan

### Step 1: Add `ElementIdRangeAttribute` to the engine

**File**: `noz/engine/src/ElementIdAttribute.cs`

Add an assembly-level attribute:
```csharp
[AttributeUsage(AttributeTargets.Assembly)]
public class ElementIdRangeAttribute(int start, int end) : Attribute
{
    public int Start { get; } = start;
    public int End { get; } = end;
}
```

### Step 2: Update `ElementIdGenerator` to support ranges

**File**: `noz/generators/ElementIdGenerator.cs`

- Add a provider that reads `ElementIdRangeAttribute` from the compilation's assembly attributes
- Combine it with the existing class declarations pipeline
- In `GenerateSource`, initialize `nextId` from the range's `Start` value (default: 1)
- Optionally emit a diagnostic if generated IDs exceed the range's `End`

### Step 3: Create `NoZ.Widgets` project

**New directory**: `noz/widgets/`

**`noz/widgets/NoZ.Widgets.csproj`**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AssemblyName>NoZ.Widgets</AssemblyName>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\engine\NoZ.csproj" />
    <ProjectReference Include="..\generators\NoZ.Generators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**`noz/widgets/src/AssemblyInfo.cs`**:
```csharp
[assembly: NoZ.ElementIdRange(8192, 16383)]
```

Add project reference in `game/HighriseStudio.csproj`:
```xml
<ProjectReference Include="..\noz\widgets\NoZ.Widgets.csproj" />
```

### Step 4: Implement `WidgetStyle` with setter-based customization

**File**: `noz/widgets/src/WidgetStyle.cs`

Pattern: Pre-built `internal static` style structs. Public setters mutate specific fields in those structs. Style access in the render loop is just reading a static field (zero cost). Customization cost is paid once at init time.

**Key design**: No `Control` sub-class. Every widget IS a control, so the shared base styling (fill, text, icon, dimensions) lives at the top level of `WidgetStyle`. Only widget-specific styles nest (e.g. `WidgetStyle.Button`, `WidgetStyle.Slider`).

```csharp
namespace NoZ;

public static partial class WidgetStyle
{
    // Shared base styles - used by all widgets
    internal static ContainerStyle _root = new() { MinWidth = 40f, MinHeight = 40f, Height = 40f };
    internal static ContainerStyle _fill = new() { Color = Color.FromRgb(0x1d1d1d), Border = new BorderStyle { Radius = 10f } };
    internal static ContainerStyle _fillHovered = _fill with { Color = Color.FromRgb(0x3f3f3f) };
    internal static ContainerStyle _fillChecked = _fill with { Color = Color.FromRgb(0x545454) };
    internal static ContainerStyle _fillDisabled = _fill with { Color = Color.Transparent };
    internal static LabelStyle _text = new() { FontSize = 16f, Color = Color.FromRgb(0xebebeb), AlignY = Align.Center };
    internal static LabelStyle _textHovered = _text with { Color = Color.White };
    internal static LabelStyle _textDisabled = _text with { Color = Color.FromRgb(0x3e3e3e) };
    internal static ImageStyle _icon = new() { AlignY = Align.Center, Width = 16f, Height = 16f, Color = Color.FromRgb(0xebebeb) };

    // Public ref access for render path (zero cost)
    public static ref ContainerStyle Root => ref _root;
    public static ref ContainerStyle Fill => ref _fill;
    public static ref ContainerStyle FillHovered => ref _fillHovered;
    public static ref ContainerStyle FillChecked => ref _fillChecked;
    // ... etc

    // Widget-specific styles nest
    public static class Button
    {
        internal static ContainerStyle _content = new() { Padding = EdgeInsets.LeftRight(10f), Spacing = 5f };
        public static ref ContainerStyle Content => ref _content;
    }

    public static class Slider
    {
        internal static ContainerStyle _track = new() { Height = 4f, Color = Color.FromRgb(0x3a3a3a) };
        internal static ContainerStyle _thumb = new() { Width = 16f, Height = 16f };
        public static ref ContainerStyle Track => ref _track;
        public static ref ContainerStyle Thumb => ref _thumb;
    }
}
```

Game customization (mutate the pre-built structs directly):
```csharp
// At game init:
WidgetStyle.Fill = WidgetStyle.Fill with { Color = myPalette.Grey1 };
WidgetStyle.FillHovered = WidgetStyle.FillHovered with { Color = myPalette.Grey2 };
WidgetStyle.Root = WidgetStyle.Root with { Height = 44f };
WidgetStyle.Slider.Track = WidgetStyle.Slider.Track with { Color = myPalette.Grey3 };
```

### Step 5: Implement `Widget` static class

**Files** (partial class split by widget, same pattern as GameUI):
- `noz/widgets/src/Widget.cs` - Main class, ElementId declarations, sound hooks, shared control state + BeginWidget/EndWidget/WidgetFill/WidgetIcon/WidgetText
- `noz/widgets/src/Widget.Button.cs` - Button overloads (icon, text, icon+text)
- `noz/widgets/src/Widget.Slider.cs` - Slider
- `noz/widgets/src/Widget.Toggle.cs` - Toggle/Checkbox
- `noz/widgets/src/Widget.Popup.cs` - Popup menu
- `noz/widgets/src/Widget.ToolTip.cs` - Tooltips
- `noz/widgets/src/Widget.Dialog.cs` - Dialog/modal
- `noz/widgets/src/Widget.Tab.cs` - Tab bar

```csharp
namespace NoZ;

public static partial class Widget
{
    [ElementId("Popup")]
    [ElementId("PopupItem", count: 64)]
    [ElementId("Tooltip")]
    private static partial class ElementId { }

    // Optional sound hooks
    public static Action? OnHover { get; set; }
    public static Action? OnPress { get; set; }

    // Shared widget state (replaces "Control" layer)
    private static bool _enabled;
    private static bool _checked;
    private static bool _hovered;

    private static void BeginWidget(int id, bool isChecked, bool enabled) { ... }
    private static void EndWidget() { ... }
    private static void WidgetFill(bool toolbar = false) { ... }  // reads WidgetStyle.Fill etc
    private static void WidgetIcon(Sprite icon, ...) { ... }      // reads WidgetStyle._icon etc
    private static void WidgetText(string text, ...) { ... }      // reads WidgetStyle._text etc
}
```

Widget methods follow the exact GameUI pattern:
- Accept `int id` from caller for element state tracking
- Use `WidgetStyle.*` static fields for styling (zero-cost read, no `Control` nesting)
- Internal popups/tooltips use their own `ElementId.*` constants (in the 8192+ range)

### Step 6: Initial widget set (start with Button + Slider)

Start with `Widget.Button` and `Widget.Slider` as the first two widgets to validate the architecture end-to-end. Then expand to the remaining widgets.

## Key Files Modified

| File | Change |
|------|--------|
| `noz/engine/src/ElementIdAttribute.cs` | Add `ElementIdRangeAttribute` |
| `noz/generators/ElementIdGenerator.cs` | Read assembly range, use as start offset |
| `game/HighriseStudio.csproj` | Add NoZ.Widgets project reference |

## New Files

| File | Purpose |
|------|---------|
| `noz/widgets/NoZ.Widgets.csproj` | Project file |
| `noz/widgets/src/AssemblyInfo.cs` | `[assembly: ElementIdRange(8192, 16383)]` |
| `noz/widgets/src/WidgetStyle.cs` | Pre-built style structs with public ref accessors |
| `noz/widgets/src/Widget.cs` | Main class, ElementId, hooks, shared widget state (BeginWidget/EndWidget/WidgetFill/WidgetIcon/WidgetText) |
| `noz/widgets/src/Widget.Button.cs` | Button widget |
| `noz/widgets/src/Widget.Slider.cs` | Slider widget |

## Verification

1. Build the solution - confirm no compile errors
2. Check generated `.g.cs` for NoZ.Widgets project - IDs should start at 8192
3. Check generated `.g.cs` for game project - IDs should still start at 1
4. Run the game, add a test `Widget.Button()` call alongside existing `GameUI.Button()` calls - confirm no ID collisions (both buttons work independently)
5. Test `WidgetStyle` customization - change a color at init, verify it renders correctly
