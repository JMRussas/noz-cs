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

Immediate-mode hierarchical UI. Key files: `UI.cs`, `UI.Layout.cs`, `UI.Draw.cs`, `UI.Input.cs`, `UI.Measure.cs`.

- **Element types**: Container, Column, Row, Grid, Label, Image, TextBox, TextArea, Scrollable, Popup, Scene, Flex, Spacer, Transform, Opacity, Cursor
- **Layout**: Flexbox-inspired. Size modes: Default, Percent, Fixed, Fit. Alignment: Min, Center, Max.
- **Building pattern**: `UI.BeginContainer(id, style)` ... `UI.EndContainer()`. Supports `using` auto-dispose.
- **Style structs**: `ContainerStyle`, `LabelStyle`, `ImageStyle`, `TextBoxStyle`, etc.
- **Input**: `UI.IsHovered()`, `UI.WasPressed()`, `UI.HasFocus()`, `UI.SetFocus()`
- **Rendering**: Custom `UIVertex` mesh, renders on layer 12. Commands with custom meshes need contiguous index merging.
- **Limits**: 4096 elements, 128-depth stack, 4 popups, 64KB text buffer
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
