# NOZ Engine

2D game engine written in C#. Targets .NET 10. Platforms: Desktop (SDL3 + WebGPU), Web (Blazor + WebGPU).

## Project Structure

```
noz/
├── engine/
│   ├── src/                    # Core engine source
│   │   ├── Application.cs      # Entry point, game loop
│   │   ├── graphics/           # Rendering, batching, shaders, meshes, cameras
│   │   ├── ui/                 # Immediate-mode UI framework
│   │   ├── input/              # Input handling, scopes
│   │   ├── audio/              # Audio playback
│   │   ├── physics/            # Collision, raycasts
│   │   ├── vfx/                # Particle system
│   │   ├── collections/        # NativeArray<T>, UnsafeList<T>, UnsafeSpan<T>, UnsafeRef<T>
│   │   ├── math/               # Noise, Rect, MathEx
│   │   ├── network/            # Networking interface
│   │   └── platform/           # IPlatform, IGraphicsDriver, IAudioDriver abstractions
│   └── assets/shader/          # WGSL shaders (sprite, text, ui, texture, composite)
├── editor/
│   ├── src/
│   │   ├── EditorApplication.cs
│   │   ├── document/           # Per-asset-type editors (Sprite, Texture, Font, Animation, Atlas, Vfx, etc.)
│   │   ├── tool/               # Move, Rotate, Scale, Pen, Knife, BoxSelect, etc.
│   │   ├── msdf/               # MSDF generation (sprites and fonts)
│   │   ├── TTF/                # TrueType font parsing
│   │   └── shape/              # Vector shape editing
│   └── assets/                 # Editor UI assets
├── platform/
│   ├── desktop/                # SDL3 window/input/audio (ppy.SDL3-CS)
│   ├── webgpu/                 # WebGPU graphics driver (Silk.NET.WebGPU)
│   └── web/                    # Blazor WebAssembly platform
├── generators/                 # C# source generators
└── docs/                       # Architecture docs
```

## Build & Run

```bash
dotnet build editor/editor.csproj       # Build editor
dotnet build src/noz.csproj             # Build engine only
dotnet run --project editor/editor.csproj  # Run editor
```

Game projects reference noz as a git submodule. Editor `--init --project .` scaffolds a new game.

## Code Style

- **Namespaces**: File-scoped (`namespace NoZ;`). Engine = `NoZ`, Editor = `NoZ.Editor`, Platform = `NoZ.Platform.*`
- **Static partial classes** for systems: `Graphics`, `UI`, `Input`, `Audio`, `Time`, `Application`
- **Primary constructors** on structs: `public readonly struct Color32(byte r, byte g, byte b, byte a = 255)`
- **Expression-bodied members** for simple properties/methods
- **`var`** used liberally
- **Readonly structs** and **ref structs** for value types
- **Unsafe code** with pointers in performance-critical paths (Graphics, collections)
- **Nullable reference types** enabled (`?` suffix)
- **Init-only properties** (`{ get; init; }`) for config objects
- Use `Log.Info`, `Log.Warning`, `Log.Error` for console output, not `Console.WriteLine`

## DO NOT

- Add XML documentation headers (`/// <summary>`, etc.) to functions or types
- Use `Console.WriteLine` — use `Log.*` instead

## Core Systems

### Application & Game Loop (`engine/src/Application.cs`)

Games implement `IApplication` with virtual methods: `Update()`, `UpdateUI()`, `LateUpdate()`, `LoadConfig()`, `LoadAssets()`, `WantsToQuit()`, etc.

Frame loop: `Time.Update → Input.BeginFrame → PollEvents → Input.Update → PreFrame → Graphics.BeginFrame → BeginFrame → Update() → UI.Begin → UpdateUI() → UI.End → LateUpdate() → VfxSystem.Update → Cursor.Update → Graphics.EndFrame`

### Graphics (`engine/src/graphics/`)

Sort-based batched rendering. Key files: `Graphics.cs`, `Graphics.State.cs`, `Graphics.Draw.cs`.

- **Sort key** (64-bit): `Pass(4) | Layer(12) | Group(16) | Order(16) | Index(16)` — built via `MakeSortKey()`
- Commands sorted via `_commands.AsSpan().Sort()`, then `CreateBatches()` merges adjacent commands with matching state
- `SortKeyMergeMask = 0x7FFFFFFFFFFF0000` masks out Index bits for merge checks
- **Render passes**: `RenderTexture = 0` (offscreen), `Scene = 1` (main framebuffer)
- **State management**: Single authority (driver `CachedState`), no redundant diffing. Driver resets state on every pass boundary.
- **Vertex types**: `MeshVertex` (standard), `UIVertex` (UI-specific format)
- **Bone texture**: 128 width x 1024 rows, 64 bones x 2 texels per bone (3x2 affine transform)
- **Limits**: 65536 vertices, 196608 indices, 8 texture slots, 3 max render passes
- **Blend modes**: None, Alpha, Additive, Multiply, Premultiplied
- **Default draw order**: containers=0, images=1 (`ImageStyle`), labels=2 (`LabelStyle`) — groups same-shader draws
- Viewport clamping uses current RT dimensions, not always surface size

### UI System (`engine/src/ui/`)

Immediate-mode hierarchical UI built on `ElementTree`. Facade: `UI.cs`. Core: `ElementTree.cs` (partial class split across files).

**Key files:**
| File | Contents |
|------|----------|
| `ElementTree.cs` | Enums, structs, fields, Init/Shutdown, Begin/End, element allocation |
| `ElementTree.API.cs` | Public element creation (Size, Padding, Fill, Row, Column, Label, Image, Scene, etc.) |
| `ElementTree.Widgets.cs` | Widget state, focus/capture, Button, Toggle, Slider |
| `ElementTree.Layout.cs` | FitAxis, LayoutAxis, GetElementSize, UpdateTransforms |
| `ElementTree.Input.cs` | HandleInput, popup auto-close, scrollable input, cursor |
| `ElementTree.Draw.cs` | Draw, DrawElement, DrawLabel, DrawImage, DrawScene, DrawScrollbar, debug dump |
| `ElementTree.EditableText.cs` | TextBox/TextArea editable text widgets |
| `UI.cs` | Public facade — delegates to ElementTree, manages camera, text buffer |
| `UI.Draw.cs` | `DrawTexturedRect` helper, mesh flush (used by ElementTree) |

- **Element types**: Widget, Size, Padding, Fill, Border, Margin, Row, Column, Flex, Align, Clip, Spacer, Opacity, Label, Image, EditableText, Popup, Cursor, Transform, Grid, Scene, Scrollable
- **Layout**: Flexbox-inspired axis-independent layout. Size modes: Default, Percent, Fixed, Fit. Alignment: Min, Center, Max.
- **Building pattern**: `ElementTree.BeginSize(...)` ... `ElementTree.EndSize()`. UI facade wraps with style structs.
- **Input**: `UI.IsHovered()`, `UI.WasPressed()`, `UI.HasFocus()`, `UI.SetFocus()` — all delegate to ElementTree
- **Input inside popups**: Use `UI.IsDown()` / `UI.WasPressed()` instead of `Input.IsButtonDown()`. When popups are open, the UI system consumes the mouse button via `Input.ConsumeButton()`, making raw `Input.IsButtonDown()` return false. `UI.IsDown()` checks the pre-consumption state.
- **Element positioning**: During the UI build phase, the current frame's elements have uninitialized `WorldToLocal` matrices (lazy-computed on first access for input). Use `UI.GetElementWorldRect(id)` to get the previous frame's laid-out rect.
- **EdgeInsets constructor**: `EdgeInsets(top, left, bottom, right)` — note the TLBR order, **not** TRBL. `L` is the 2nd parameter, `R` is the 4th.
- **Rendering**: Custom `UIVertex` mesh, renders on layer 12. Commands with custom meshes need contiguous index merging.
- **Memory**: 64KB element buffer (rebuilt each frame), 128KB double-buffered state pools (widget persistence), 375KB widget state array (32000 IDs)
- **Limits**: 65535 bytes element tree, 64-depth stack, 4 popups, 32000 widget IDs
- **Scaling**: `UIScaleMode` with reference resolution for responsive design

### Asset System (`engine/src/Asset.cs`, `AssetDef.cs`)

- **Types**: Texture, Sprite, Animation, Skeleton, Font, Shader, Sound, Atlas, Vfx, Lua, Event, Bin
- **Loading**: `Asset.Load<T>(type, name)` — platform stream → embedded resource fallback → per-type deserialization → registry cache
- **Binary format**: Header (4-byte signature + type + version + flags) then type-specific data
- **Key asset classes**: `Texture`, `Shader`, `Font`, `Sprite`, `Atlas`, `Sound`, `Animation`, `Skeleton`, `Vfx`

### Input System (`engine/src/input/Input.cs`)

- **Button states**: Physical, Logical, Pressed, Released, Repeat, Consumed
- **Scoped input**: Stack-based scopes for popup priority (prevents input leakage)
- **Repeat timing**: 0.4s initial delay, 0.05s repeat interval
- **Input codes**: `InputCode` enum covers keyboard, mouse, gamepad

### Audio (`engine/src/audio/Audio.cs`)

- `IAudioDriver` abstraction. `Audio.Play()` with volume, pitch, loop. `SoundHandle` for instance control.
- Music track with cross-fade. Volume hierarchy: Master, Sound, Music.

### VFX System (`engine/src/vfx/`)

- **Pools**: 4096 particles, 2024 emitters, 256 instances (fixed-size, no GC)
- **Hierarchy**: Instance → Emitter → Particle
- **Curves**: Linear, EaseIn/Out, Quadratic, Cubic, Sine
- **Text format**: `.vfx` files with curve syntax (`start => end : ease_type`), parsed by editor, exported as binary
- **API**: `VfxSystem.Play/Stop/Kill/IsPlaying/Clear`, returns `VfxHandle`
- Renders on GameLayer.Vfx (layer 11) using single shader + WhiteTexture + vertex color
- Docs: `noz/docs/VfxSystem.md`

### Physics (`engine/src/physics/`)

Triangle overlap, line intersection, point-in-polygon, raycast, circle cast, collision bounds.

### Animation & Skeleton (`engine/src/graphics/Animation.cs`, `Skeleton.cs`, `Animator.cs`)

- Keyframe-based bone animation with easing/interpolation
- `Animator`: play/pause/stop, speed multiplier, event callbacks
- `Skeleton`: bone hierarchy with parent-child transforms
- GPU bone lookup via bone texture

## Shaders

WGSL (WebGPU Shading Language). Located in `engine/assets/shader/`.

| Shader | Purpose |
|--------|---------|
| `sprite.wgsl` | Standard sprite rendering with bone transforms, UV animation, texture array, vertex color |
| `sprite_sdf.wgsl` | SDF sprite rendering — median(r,g,b) reconstruction with adaptive antialiasing |
| `text.wgsl` | SDF text rendering with outline support |
| `texture.wgsl` | Simple texture display |
| `texture_sdf.wgsl` | SDF texture display (editor single-atlas path) |
| `ui.wgsl` | UI elements with border radius, shadows, gradients |
| `composite.wgsl` | Post-processing / screen-space effects |

Globals uniform: `projection: mat4x4<f32>`, `time: f32`.

## Platform Layer

- **IPlatform** (`engine/src/platform/IPlatform.cs`): Window, events, display scale, clipboard, cursor, persistent data, URL open
- **IGraphicsDriver**: GPU resources, render passes, state management, shader compilation
- **Desktop** (`platform/desktop/`): SDL3 via `ppy.SDL3-CS`. Windows networking via Winsock.
- **WebGPU** (`platform/webgpu/`): `Silk.NET.WebGPU` + Dawn extensions + wgpu native
- **Web** (`platform/web/`): Blazor, WebAudio, LocalStorage, JS interop for WebGPU

## Editor (`editor/src/`)

- **Document editors**: Texture, Sprite, Font, Skeleton, Animation, Atlas, Shader, Sound, Vfx
- **Tools**: Move, Rotate, Scale, Pen, Knife, BoxSelect, SpriteSelect, BoneSelect, Rename, Curve
- **Font import**: TTF parsing (`TTF/TrueTypeFont.Reader.cs`) → SDF generation (`msdf/Msdf.Font.cs`) → binary font asset (RGBA8 atlas)
- **SDF generation**: Multi-channel signed distance fields (MSDF algorithm) for sprites and fonts. Port of msdfgen by Viktor Chlumsky. See `docs/sdf.md`.
- **Asset pipeline**: Source files → Importer → binary assets. AtlasManager for texture packing. AssetManifest for registry.
- **Project init**: `--init --project .` scaffolds game project from embedded templates
- **Style**: `EditorStyle.cs` for editor UI theme

## Dependencies

| Package | Project | Purpose |
|---------|---------|---------|
| `SixLabors.ImageSharp` | Editor | Image processing |
| `ppy.SDL3-CS` | Desktop | Window, input, audio |
| `Silk.NET.WebGPU` | WebGPU | GPU bindings |
| `Silk.NET.WebGPU.Extensions.Dawn` | WebGPU | Dawn backend |
| `Silk.NET.WebGPU.Native.WGPU` | WebGPU | wgpu native lib |
| `Microsoft.AspNetCore.Components.Web` | Web | Blazor framework |

Engine itself has zero external dependencies.
