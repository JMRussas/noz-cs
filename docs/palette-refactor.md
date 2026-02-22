# Shape & Color Refactor

## Overview

Multi-phase refactor to move from palette-indexed colors to direct Color32 throughout the sprite editing pipeline, culminating in an HSV color picker replacing the old palette swatch grid.

## Phase 1: Shape — Replace palette indices with Color32

**Before:** `Shape.Path` stored `byte FillColor` and `byte StrokeColor` as palette indices. `Shape.Rasterize()` accepted a `Color[] palette` parameter.

**After:** `Shape.Path` stores `Color32 FillColor` and `Color32 StrokeColor` directly. `Shape.Rasterize()` reads colors straight from paths.

## Phase 2: Shape — Fold opacity into Color32.A

**Before:** `Shape.Path` had separate `float FillOpacity` and `float StrokeOpacity`. Subtract mode was a sentinel value (`FillOpacity <= float.MinValue`).

**After:** Alpha is baked into `Color32.A`. Subtract mode uses `PathFlags.Subtract` flag with `SetPathSubtract(pathIndex, bool)`.

## Phase 3: HSV Color Picker (`EditorUI.ColorPicker`)

Added a full HSV color picker as a reusable widget in `EditorUI.ColorPicker.cs`.

### Layout
```
┌──────────────────────────────┐
│  ┌──────────────┐  ┌──┐     │
│  │  SV rect     │  │H │     │
│  │  160x160     │  │u │     │
│  │  (drag)      │  │e │     │
│  └──────────────┘  └──┘     │
│  [====== Alpha bar ======]  │
│  Preview: [old] → [new]     │
│  Hex: #FF8040               │
│  [None]                     │
│  ── Swatches (optional) ──  │
│  ████ ████ ████ ████        │
└──────────────────────────────┘
```

### Architecture

`EditorUI.ColorPicker` is a nested static class inside `EditorUI`. Public API stays on `EditorUI`:

```csharp
public static bool ColorPickerButton(
    int id,
    ref Color32 color,
    Action<Color32>? onPreview = null,
    Color[]? swatches = null,
    int swatchCount = 0,
    Sprite? icon = null)
```

- Returns `true` only on **commit** (popup closed with a change, or swatch clicked).
- Calls `onPreview` each frame during drag for live preview (no undo recorded).
- Callers should only call `Undo.Record()` on commit (when method returns true).

### Internal state

- HSV floats (`_hue`, `_sat`, `_val`, `_alpha`) — source of truth while picker is open
- SV gradient texture (64x64, regenerated when hue changes) via `PixelData<Color32>` (native memory, no GC)
- Hue bar texture (1x256, generated once) via `PixelData<Color32>`
- Hex string cache to avoid per-frame allocation
- `_committed` flag distinguishes preview drags from final commits
- `_originalColor` captured on open for change detection

### Key implementation details

- **No closures**: Button content uses a static method + static fields (`_buttonColor`, `_buttonIcon`) to avoid per-frame delegate allocation.
- **Native memory**: `PixelData<Color32>` for pixel buffers instead of `byte[]` — no GC pressure.
- **Mouse coordinates**: Uses `UI.GetElementWorldRect(id)` + `UI.MouseWorldPosition` for hit-testing the SV rect and hue/alpha bars.
- **Input in popups**: Uses `UI.IsDown()` instead of `Input.IsButtonDown()` (UI system consumes mouse button in popups).
- **Popup close commit**: On the close frame, `Popup()` sets `ref color` to the final HSV color before returning, so the caller sees the change.
- **Rounding**: `HsvToColor32` uses `(byte)(x * 255f + 0.5f)` to avoid truncation (e.g., alpha=1.0 → 255).
- **None button**: Sets alpha to 0 and commits. Button icon shows `IconNofill` when active.

## Phase 4: SpriteDocument & SpriteEditor — Remove palette indirection

### SpriteDocument changes

**Removed:**
- `byte Palette` — document no longer references a palette row
- `byte CurrentFillColorIndex` / `byte CurrentStrokeColorIndex` — palette indices
- `byte CurrentFillAlpha` / `byte CurrentStrokeAlpha` — separate alpha bytes

**Added:**
- `Color32 CurrentFillColor` — direct fill color (default: white)
- `Color32 CurrentStrokeColor` — direct stroke color (default: transparent)

**Save format:** No longer writes `palette "..."` line. Colors are always written as `#RRGGBB` or `rgba(r,g,b,a)`.

**Load compat:** The `palette` keyword is parsed but ignored. Legacy integer fill/stroke indices are resolved against palette row 0.

### SpriteEditor changes

**Toolbar:**
- Fill button: `ColorPickerButton` with `Document.CurrentFillColor` directly
- Stroke button: `ColorPickerButton` with `Document.CurrentStrokeColor` directly
- Stroke width: Separate button showing `{n}px`, popup with sizes 1-8

**Removed:**
- `PaletteButtonUI` / `PalettePopupUI` — palette selector
- `SetFillColor(byte colorIndex, float opacity)` — palette-based overload
- All `PaletteManager.FindColorIndex` / `PaletteManager.GetColor(Document.Palette, ...)` calls

**Updated:**
- `SetFill(Color32)` / `SetStroke(Color32)` — store color directly, record undo, apply to selected paths
- `SetStrokeWidth(byte)` — separate from stroke color
- `PreviewFillColor` / `PreviewStrokeColor` — apply to shape without undo (called during drag)
- `UpdateSelection` — reads `path.FillColor` / `path.StrokeColor` into document directly
- `BeginPenTool`, `BeginRectangleTool`, `BeginCircleTool` — use `Document.CurrentFillColor` directly
- `Clone` — copies `CurrentFillColor`, `CurrentStrokeColor`, `CurrentStrokeWidth`

## Files Modified (cumulative)

### Engine
- `Shape.cs` — Color32 fill/stroke, PathFlags.Subtract, no palette awareness
- `Shape.Rasterize.cs` — Removed palette parameter
- `UI.Input.cs` — Added `MouseWorldPosition` property

### Editor
- `EditorUI.ColorPicker.cs` — **New file.** HSV color picker widget
- `EditorUI.cs` — Old `ColorButton`/`ColorPopup` still present (used by other editors)
- `SpriteDocument.cs` — Color32 state, removed Palette field, legacy load compat
- `SpriteEditor.cs` — ColorPickerButton for fill/stroke, removed palette UI
- `SpriteEditor.Mesh.cs` — Uses path.FillColor directly
- `ShapeTool.cs` — Takes Color32 fillColor
- `PenTool.cs` — Takes Color32 fillColor
- `KnifeTool.cs` — Passes subtract flag directly
- `Clipboard.cs` — Color32 in PathData
- `PaletteManager.cs` — FindColorIndex for reverse lookup (still used by old ColorButton)

## Serialization Format

### Current format (written on save)
```
path
fill #FF8040
anchor 0.5 0.5 0

path
subtract true
fill rgba(255,128,64,0.5)
stroke rgba(0,0,0,0.8) 2
anchor ...
```

### Legacy format (still readable)
```
palette "default"
path
fill 5 1.0
stroke 0 0.5 2
anchor ...
```
Legacy integer values resolve via `PaletteManager.GetColor(0, index)`. The trailing float folds into `Color32.A`. The `palette` keyword is parsed and ignored.

## Architecture

```
  SpriteEditor (UI)
    |
    | Color32 directly (no palette lookup)
    v
  SpriteDocument
    |
    | CurrentFillColor / CurrentStrokeColor
    v
  Shape (Color32 fill/stroke per path)
    |
    v
  Shape.Rasterize / RasterizeSDF / RasterizeMSDF
```

## What's Next

- Palette as a first-class asset type (`.palette` files with named colors, PNG import)
- PaletteManager rework to load from `.palette` documents
- AssetManifest generates named color constants (`GameAssets.Palettes.Default.Skin`)
- Gradient fill support (now feasible since Shape owns its colors)
- Per-anchor color / vertex coloring
