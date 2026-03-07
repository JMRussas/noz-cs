# Editor Component Style Guide

Font: Segoe UI Semibold (seguisb) — engine default

## Colors

### Surface Backgrounds

| Name | Hex | Usage |
|------|-----|-------|
| Canvas | #161616 | Workspace / 2D viewport background |
| Page BG | #1A1A1A | Inspector panel background, input field fills |
| Body | #212121 | Section body (expanded foldout content) |
| Grid | #222222 | Canvas grid lines |
| Header | #2D2D2D | Section header bars, context menu bg, inactive toggle bg |
| Button 2nd | #333333 | Secondary button fill (Add Component) |
| Active | #3D3D3D | Selected toggle bg, hover for secondary buttons |
| Primary | #E83A3A | Primary action button (Generate/Apply) |
| Primary Hover | #F04848 | Primary button hover state |

### Text & Icon Colors

| Name | Hex | Usage |
|------|-----|-------|
| Content | #FFFFFF | Primary content text, active input values |
| Header Text | #AAAAAA | Section header titles, icons, chevrons |
| Btn 2nd Text | #999999 | Secondary button text/icons |
| Secondary | #777777 | Field labels, ellipsis menus, param icons, negative prompt text |
| Disabled (dark bg) | #666666 | Disabled items on dark backgrounds (context menus, headers) |
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
| Content Text | 400 | 11px | #FFFFFF |
| Field Label | 500 | 9px | #777777 |
| Placeholder | 400 | 11px | #555555 |

## Components

### Section Header (Foldout)

- Height: 28px
- Fill: #2D2D2D
- Padding: 0 8px
- Layout: horizontal, center-aligned, gap 6
- Children: chevron (12x12) + icon (12x12) + title text + flex spacer + ellipsis menu (14x14)
- Expanded: chevron-down
- Collapsed: chevron-right
- For named sections (GenSprite, Refine): icon + bold title
- For layers: icon + prompt preview text

### Buttons

**Primary**
- Height: 36px, cornerRadius: 4, fill: #E83A3A
- Text: 12px 600, #FFFFFF
- Icon: 16x16, #FFFFFF
- Hover: #F04848
- Disabled: #E83A3A44, text #FFFFFF44

**Secondary (Add Component)**
- Height: 28px, cornerRadius: 4, fill: #333333
- Text: 12px 500, #999999
- Icon: 12x12, #999999
- Hover: fill #3D3D3D, text #CCCCCC
- Disabled: text #555555

### Toggle Group (Icon-only)

- Container: #1A1A1A, cornerRadius 4, padding 2, gap 2
- Each toggle: 28x28
- Selected: fill #3D3D3D, cornerRadius 3, icon #FFFFFF
- Unselected: no fill, icon #555555
- Hover (off): fill #2D2D2D, icon #AAAAAA
- Disabled: icon #333333

### Toggle Button (Single Icon)

- 28x28, cornerRadius 4
- On: fill #3D3D3D, icon #FFFFFF
- Off: no fill, icon #555555
- Hover (off): fill #2D2D2D, icon #AAAAAA

### Dropdown

- Height: 28px, fill: #1A1A1A, cornerRadius 4, padding 0 10px
- Layout: horizontal, center-aligned, gap 6
- Children: icon (12x12, #777777) + value text (11px, #FFFFFF) + flex spacer + chevron-down (12x12, #777777)
- Placeholder: all elements #555555, text "None"
- Disabled: all elements #333333

### Input Fields

**Param Field (icon + value)**
- Height: 28px, fill: #1A1A1A, cornerRadius 4, padding 0 8px, gap 4
- Icon: 12x12, #777777
- Value: 11px, #FFFFFF
- Placeholder: icon #333333, value #555555
- Focused: 1px #E83A3A inside border, white caret
- Disabled: icon #333333, value #333333

**Text Field (label + multiline)**
- Fill: #1A1A1A, cornerRadius 4, padding 8 10px, vertical layout, gap 4
- Label: 9px 500, #777777
- Value: 11px, #FFFFFF, fixed-width text growth
- Placeholder: value #555555
- Focused: 1px #E83A3A inside border, white caret
- Text selection: #E83A3A44 background on selected text
- Disabled: label #555555, value #333333

### List Row

- Height: 28px, fill: #1A1A1A, cornerRadius 4, padding 0 10px, gap 6
- Name: 11px, #FFFFFF
- Value: 11px, #777777
- Remove icon: x, 12x12, #777777

### Context Menu

- Fill: #2D2D2D, cornerRadius 6, padding 4 0
- Item height: 28px, padding 0 10px, gap 8
- Icon: 14x14, #777777
- Text: 11px, #FFFFFF
- Shortcut: 10px, #555555
- Separator: 1px #3D3D3D
- Destructive: icon + text #E83A3A
- Hover: item fill #3D3D3D, icon brightens to #AAAAAA, shortcut to #777777
- Disabled: icon + text #666666

### Popup Menu

- Same as context menu but simpler (no shortcuts)
- Used for: Add Component popup, dropdown menus

## Hover States

| Component | Default | Hover |
|-----------|---------|-------|
| Primary button | #E83A3A | #F04848 |
| Secondary button | #333333 / text #999999 | #3D3D3D / text #CCCCCC |
| Toggle off | no fill / icon #555555 | #2D2D2D / icon #AAAAAA |
| Section header | #2D2D2D | #333333 |
| Inputs/rows | #1A1A1A | #222222 |
| Icon actions | #777777 | #AAAAAA |
| Context menu item | transparent | #3D3D3D |

## Spacing

| Context | Value |
|---------|-------|
| Inspector width | 280px |
| Section gap | 2px |
| Body padding | 10px 12px |
| Body gap | 6px |
| Param row gap | 4px |
| Header padding | 0 8px |
| Header gap | 6px |
| Add Component margin | 10px 16px top/sides |
| Generate button padding | 12px 16px wrapper |
| Corner radius (inputs) | 4px |
| Corner radius (context menu) | 6px |

## Canvas / Workspace

| Element | Color |
|---------|-------|
| Background | #161616 |
| Grid lines | #222222 |
| Major grid / origin | #2A2A2A |
| Selection outline | #E83A3A66 (1px outside stroke) |
| Selection fill | #E83A3A11 |
