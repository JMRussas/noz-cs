# AssetRef<T> — Lazy Asset References in Styles

## Problem

Style structs (ButtonStyle, TextInputStyle, etc.) use field initializers for static construction. But assets like `Font`, `Sprite`, `Texture` aren't available until runtime loading completes. This creates two issues:

1. **Font in styles requires nullable + late init**: `ButtonStyle.Font` is `Font?` — must be set after loading, can't be part of a static style definition.
2. **Sprites can't be in styles at all**: They're instance-specific content (like button text), so they're passed as parameters. But some cases blur the line — a "search icon" for a search field style is arguably a style concern.

Current workaround: nullable fields set after `LoadAssets()`. Works but fragile.

## Observation

Assets like `EditorAssets.Sprites.*` are static fields — they have stable identities known at compile time. They just aren't populated until loading. If we had a lightweight reference that resolves lazily, styles could reference assets statically.

## Proposed Design

```csharp
public readonly struct AssetRef<T>(string name) where T : Asset
{
    public static readonly AssetRef<T> None = default;

    public readonly string? Name = name;

    public T? Value => Name != null ? Asset.Get<T>(Name) : null;

    public bool IsNone => Name == null;

    // Ergonomic: allows `AssetRef<Font> font = "Fredoka";`
    public static implicit operator AssetRef<T>(string name) => new(name);

    // Ergonomic: allows using AssetRef<T> where T is expected
    public static implicit operator T?(AssetRef<T> r) => r.Value;
}
```

### Usage in Styles

```csharp
public struct ButtonStyle()
{
    public AssetRef<Font> Font = "Fredoka";   // static, resolves at render time
    public float FontSize = 16;
    // ...
}

// Static style definition — no loading dependency
public static readonly ButtonStyle Primary = new()
{
    Font = "Fredoka",
    FontSize = 18,
    Color = Color.FromRgb(0xE83A3A)
};
```

### Usage at Render Time

```csharp
// In UI.Button:
var font = s.Font.Value ?? _defaultFont!;
// or with implicit conversion:
Font font = s.Font ?? _defaultFont!;
```

## Tradeoffs

**Pros:**
- Styles become fully static — no late-init, no nullable ceremony
- String-based names are compile-time safe if using `nameof()` or constants
- Lightweight (just a string field, same size as current `Font?`)
- `Asset.Get<T>` is presumably a dictionary lookup — fast per frame

**Cons:**
- String-based — no compile-time guarantee the asset exists (could add source generator validation later)
- One dictionary lookup per frame per usage (vs direct reference)
- Implicit conversion to T? may hide the lookup cost

## Open Questions

1. Should `AssetRef` resolve via `Asset.Get<T>()` or have its own registry?
2. Should we support `AssetRef` from an enum/int ID instead of string for faster lookup?
3. Should the implicit `operator T?` exist, or should `.Value` be explicit to make the lookup visible?
4. How does this interact with the existing `GameAssets` / `EditorAssets` pattern where assets are loaded into static fields?
5. Could a source generator validate asset names at compile time?

## Migration Path

If adopted:
1. Add `AssetRef<T>` struct to engine
2. Migrate `ButtonStyle.Font` from `Font?` to `AssetRef<Font>`
3. Migrate other style structs with asset references
4. Eventually remove late-init patterns from style setup code
