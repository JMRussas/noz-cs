# Editor Component Style Guide

Font: Inter (engine default)

## Colors

### Surface Backgrounds

| Name | Hex | Usage |
|------|-----|-------|
| Canvas | #161616 | Workspace / 2D viewport background |
| Page BG | #1A1A1A | Inspector panel background, input field fills |
| Body | #212121 | Section body (expanded foldout content) |
| Grid | #222222 | Canvas grid lines |
| Header | #2D2D2D | Section header bars, context menu bg |
| Button 2nd | #333333 | Secondary button fill (Add Component) |
| Active | #3D3D3D | Selected toggle bg, hover for secondary buttons |
| Primary | #E83A3A | Primary action button (Generate/Apply) |
| Primary Hover | #F04848 | Primary button hover state |

### Text & Icon Colors

| Name | Hex | Usage |
|------|-----|-------|
| Content | #E0E0E0 | Primary content text, active input values |
| Header Text | #AAAAAA | Section header titles, icons, chevrons |
| Btn 2nd Text | #999999 | Secondary button text/icons |
| Secondary | #777777 | Field labels, ellipsis menus, param icons |
| Disabled | #666666 | Disabled items on dark backgrounds |
| Placeholder | #555555 | Placeholder text, disabled on light bg, empty dropdown text/icons |
| Disabled (light bg) | #333333 | Disabled text/icons on input fields (#1A1A1A bg) |

### Alpha / State Colors

| Name | Hex | Usage |
|------|-----|-------|
| Text Selection | #E83A3A44 | Selected text highlight background |
| Selection Outline | #E83A3A66 | Sprite/object selection border on canvas |
| Focus Ring | #E83A3A | Input field border when focused |

## Typography

| Role | Weight | Size | Color |
|------|--------|------|-------|
| Section Header | 600 | 12px | #AAAAAA |
| Content Text | 400 | 16px | #E0E0E0 |
| Field Label | 500 | 9px | #777777 |

## Components

### Section Header (Foldout)

- Height: 40px
- Fill: #2D2D2D
- Padding: 0 8px
- Layout: horizontal, center-aligned, gap 6
- Children: chevron (14x14) + icon (14x14) + title text (16px 600) + flex spacer + ellipsis menu (14x14)
- Expanded: chevron-down, Collapsed: chevron-right
- Active: 1px #E83A3A inside border, all text/icons #FFFFFF
- Hover: fill #333333

### Buttons

**Primary**
- Height: 40px, cornerRadius: 4, fill: #E83A3A
- Text: 16px 600, #FFFFFF
- Icon: 16x16, #FFFFFF
- Gap: 8, justify: center
- Hover: #F04848
- Disabled: fill #E83A3A44, text/icon #FFFFFF44

**Secondary (Add Component)**
- Height: 40px, cornerRadius: 4, fill: #333333
- Text: 16px 500, #999999
- Icon: 14x14, #999999
- Gap: 6, justify: center
- Hover: fill #3D3D3D, text/icon #CCCCCC
- Disabled: text/icon #555555

### Toggle Group (Icon-only)

- Container: #1A1A1A, cornerRadius 4, padding 2, gap 2
- Each toggle: 40x40
- Selected: fill #3D3D3D, cornerRadius 4, icon #FFFFFF
- Unselected: no fill, icon #555555
- Hover (off): fill #2D2D2D, icon #AAAAAA
- Disabled: icon #333333

### Toggle Button (Single Icon)

- 40x40, cornerRadius 4
- On: fill #3D3D3D, icon #FFFFFF (14x14)
- Off: no fill, icon #555555
- Hover (off): fill #2D2D2D, icon #AAAAAA

### Dropdown

- Height: 40px, fill: #1A1A1A, cornerRadius 4, padding 0 10px
- Layout: horizontal, center-aligned, gap 6
- Children: icon (14x14, #777777) + value text (16px, #E0E0E0) + flex spacer + chevron-down (14x14, #777777)
- Placeholder: all elements #555555, text "None"
- Hover: fill #3D3D3D, icons #AAAAAA
- Open: fill #3D3D3D, chevron-up, icons #AAAAAA
- Disabled: all elements #333333

### Input Fields

**Param Field (icon + value)**
- Height: 40px, fill: #1A1A1A, cornerRadius 4, padding 0 8px, gap 4
- Icon: 14x14, #777777
- Value: 16px, #E0E0E0
- Placeholder: icon #333333, value #555555
- Focused: 1px #E83A3A inside border, white caret
- Disabled: icon #333333, value #333333

**Text Field (label + multiline)**
- Fill: #1A1A1A, cornerRadius 4, padding 8 10px, vertical layout, gap 4
- Label: 9px 500, #777777
- Value: 16px, #E0E0E0, fixed-width text growth
- Placeholder: value #555555
- Focused: 1px #E83A3A inside border, white caret
- Text selection: #E83A3A44 background on selected text
- Disabled: label #555555, value #333333

### List Row

- Height: 40px, fill: #1A1A1A, cornerRadius 4, padding 0 10px, gap 6
- Name: 16px, #E0E0E0
- Value: 16px, #777777
- Remove icon: x, 14x14, #777777

### Context Menu

- Fill: #2D2D2D, cornerRadius 6, padding 4 0
- Item height: 40px, padding 0 10px, gap 8
- Icon: 14x14, #777777
- Text: 16px, #E0E0E0
- Separator: 1px #3D3D3D
- Destructive: icon + text #E83A3A
- Hover: item fill #3D3D3D, icon brightens to #AAAAAA
- Disabled: icon + text #666666

### Popup Menu

- Same as context menu but simpler (no shortcuts)
- Used for: Add Component popup, dropdown menus
- Item height: 40px, padding 0 12px, gap 8

### Popup Dialogs

- Fill: #2D2D2D, cornerRadius 6, padding 16 20px, gap 12
- Title: 12px 600, #FFFFFF, center-aligned
- Message: 11px, #777777, line-height 1.5, center-aligned
- Separator: 1px #3D3D3D
- Width: 280px

### Notifications (Toast)

- Height: 36px, fill: #2D2D2D, cornerRadius 6, padding 0 12px, gap 8, width 300px
- Border: 1px inside #3D3D3D (info), #E83A3A66 (error), #D4930066 (warning), #2ECC7166 (success)
- Icon: 14x14 — #AAAAAA (info), #E83A3A (error), #D49300 (warning), #2ECC71 (success)
- Text: 11px, #E0E0E0
- Close: x, 12x12, #555555

## Hover States

| Component | Default | Hover |
|-----------|---------|-------|
| Primary button | #E83A3A | #F04848 |
| Secondary button | #333333 / text #999999 | #3D3D3D / text #CCCCCC |
| Toggle off | no fill / icon #555555 | #2D2D2D / icon #AAAAAA |
| Section header | #2D2D2D | #333333 |
| Inputs/rows | #1A1A1A | — |
| Icon actions | #777777 | #AAAAAA |
| Context menu item | transparent | #3D3D3D |

## Spacing

| Context | Value |
|---------|-------|
| Inspector width | 280–300px |
| Section gap | 2px |
| Body padding | 10px 12px |
| Body gap | 6px |
| Param row gap | 4px |
| Header padding | 0 8px |
| Header gap | 6px |
| Corner radius (inputs) | 4px |
| Corner radius (context menu) | 6px |

### Asset Browser

A searchable popup for selecting assets (textures, etc.) from the project. Replaces plain dropdowns when the asset list is too large.

**Structure:**
- Trigger: same as Dropdown (height 40, fill #1A1A1A, cornerRadius 4, padding 0 10px)
- Popup: fill #2D2D2D, cornerRadius 6, padding 4 0, width 260, clip overflow
- Search bar: height 40, padding 0 10px, gap 6, search icon (14x14 #777777) + editable text (14px)
  - Focused: 1px #E83A3A inside border, search icon #E83A3A, clear "x" icon appears (#777777)
  - Placeholder: "Search textures..." #555555
- Separator: 1px #3D3D3D between search and list
- Item list: vertical, padding 4 0, scrollable when items exceed max height
  - Item: height 36, padding 0 12, gap 8, horizontal center-aligned
  - Icon: image icon 14x14 #777777
  - Name: 14px #E0E0E0
  - Hover: fill #3D3D3D cornerRadius 2, icon brightens to #AAAAAA
- Max visible items: ~8 (288px), scroll for more
- Clicking an item selects it and closes the popup
- Typing filters the list in real-time (case-insensitive substring match)

**Behavior:**
- Opens on click of the trigger button
- Search field auto-focuses on open
- Escape or click-outside closes
- Empty search shows all available assets
- No results: show "No matches" text (#555555, 14px, centered)

## Canvas / Workspace

| Element | Color |
|---------|-------|
| Background | #161616 |
| Grid lines | #222222 |
| Selection outline | #E83A3A66 (1px outside stroke) |
| Selection fill | #E83A3A11 |
