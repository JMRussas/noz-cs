//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public class TextureDocument : Document, ISpriteSource
{
    public const float PixelsPerUnit = 256.0f;
    public const float PixelsPerUnitInv = 1.0f / PixelsPerUnit;

    public float Scale { get; set; } = 1f;
    public bool IsSprite { get; set; }
    public Texture? Texture { get; private set; }
    internal AtlasDocument? Atlas { get; set; }
    internal Rect AtlasUV { get; set; }

    ushort ISpriteSource.FrameCount => 1;
    AtlasDocument? ISpriteSource.Atlas { get => Atlas; set => Atlas = value; }
    Vector2Int ISpriteSource.GetFrameAtlasSize(ushort frameIndex) => GetAtlasSize();

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Texture,
            Extension = ".png",
            Factory = () => new TextureDocument(),
            EditorFactory = doc => new TextureEditor((TextureDocument)doc),
        });
    }

    public override void LoadMetadata(PropertySet meta)
    {
        IsEditorOnly = meta.GetBool("texture", "reference", false) || Path.Contains("reference", StringComparison.OrdinalIgnoreCase);
        IsSprite = meta.GetBool("texture", "sprite", false);
        Scale = meta.GetFloat("editor", "scale", 1f);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetFloat("editor", "scale", Scale);
        meta.SetBool("texture", "reference", IsEditorOnly);
        meta.SetBool("texture", "sprite", IsSprite);
    }

    public override void PostLoad()
    {
        Texture = Asset.Load(
            AssetType.Texture,
            Name,
            useRegistry: false,
            libraryPath: EditorApplication.OutputPath) as Texture;
        UpdateBounds();
    }

    public override void Reload()
    {
        Texture?.Dispose();
        Texture = Asset.Load(
            AssetType.Texture,
            Name,
            useRegistry: false,
            libraryPath: EditorApplication.OutputPath) as Texture;
        UpdateBounds();
    }

    public void UpdateBounds()
    {
        if (Texture != null)
        {
            var tsize = new Vector2(Texture.Width, Texture.Height) / PixelsPerUnit;
            Bounds = new Rect(-tsize.X * 0.5f, -tsize.Y * 0.5f, tsize.X, tsize.Y);
        }
        else
        {
            Bounds = new Rect(-0.5f * Scale, -0.5f * Scale, Scale, Scale);
        }

        Bounds = Bounds.Scale(Scale);
    }

    public Vector2Int GetImageSize()
    {
        if (Texture != null)
            return new Vector2Int(Texture.Width, Texture.Height);

        if (!File.Exists(Path))
            return Vector2Int.Zero;

        var info = Image.Identify(Path);
        return info != null ? new Vector2Int(info.Width, info.Height) : Vector2Int.Zero;
    }

    public Vector2Int GetAtlasSize()
    {
        var size = GetImageSize();
        if (size == Vector2Int.Zero)
            return Vector2Int.Zero;

        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        return new Vector2Int(size.X + padding2, size.Y + padding2);
    }

    void ISpriteSource.Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding)
    {
        if (!File.Exists(Path)) return;

        using var srcImage = Image.Load<Rgba32>(Path);
        var w = srcImage.Width;
        var h = srcImage.Height;
        var padding2 = padding * 2;

        var rasterRect = new RectInt(
            rect.Rect.Position + new Vector2Int(padding, padding),
            new Vector2Int(w, h));

        // Blit pixels into the atlas image
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var pixel = srcImage[x, y];
                image[rasterRect.X + x, rasterRect.Y + y] = new Color32(pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }

        var outerRect = new RectInt(rect.Rect.Position, new Vector2Int(w + padding2, h + padding2));

        image.BleedColors(rasterRect);
        for (int p = padding - 1; p >= 0; p--)
        {
            var padRect = new RectInt(
                outerRect.Position + new Vector2Int(p, p),
                outerRect.Size - new Vector2Int(p * 2, p * 2));
            image.ExtrudeEdges(padRect);
        }
    }

    void ISpriteSource.UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding)
    {
        // Get image dimensions from the source PNG since Texture may be null
        // (texture binary is not exported when IsSprite is true)
        var imageSize = GetImageSize();
        if (imageSize == Vector2Int.Zero) return;

        for (int i = 0; i < allRects.Length; i++)
        {
            if (allRects[i].Source != (ISpriteSource)this) continue;

            var ts = (float)EditorApplication.Config.AtlasSize;
            var u = (allRects[i].Rect.Left + padding) / ts;
            var v = (allRects[i].Rect.Top + padding) / ts;
            var s = u + imageSize.X / ts;
            var t = v + imageSize.Y / ts;
            AtlasUV = Rect.FromMinMax(u, v, s, t);
            break;
        }
    }

    void ISpriteSource.ClearAtlasUVs()
    {
        AtlasUV = Rect.Zero;
    }

    public override void Draw()
    {
        if (Texture == null)
            return;

        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTexture(Texture);
            Graphics.SetColor(Color.White);
            Graphics.Draw(Bounds);
        }
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        if (IsSprite)
        {
            ImportAsSprite(outputPath);
            return;
        }

        var image = Image.Load<Rgba32>(Path);
        var filter = meta.GetString("texture", "filter", "linear");
        var clamp = meta.GetString("texture", "clamp", "clamp");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath) ?? "");

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Texture, Texture.Version);

        // Texture format
        var format = TextureFormat.RGBA8;
        var filterEnum = filter is "nearest" or "point" ? TextureFilter.Point : TextureFilter.Linear;
        var clampEnum = clamp == "repeat" ? TextureClamp.Repeat : TextureClamp.Clamp;

        writer.Write((byte)format);
        writer.Write((byte)filterEnum);
        writer.Write((byte)clampEnum);
        writer.Write((uint)image.Width);
        writer.Write((uint)image.Height);

        var dataLength = image.Width * image.Height * Unsafe.SizeOf<Rgba32>();
        using var temp = new NativeArray<byte>(dataLength, dataLength);
        image.CopyPixelDataTo(temp.AsSpan());
        writer.Write(temp.AsSpan());
    }

    private void ImportAsSprite(string outputPath)
    {
        var imageSize = GetImageSize();
        if (imageSize == Vector2Int.Zero) return;

        // Write to the sprite output path instead of texture
        var spritePath = System.IO.Path.Combine(
            DocumentManager.OutputPath, "sprite", DocumentManager.MakeCanonicalName(Name));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(spritePath) ?? "");

        var w = imageSize.X;
        var h = imageSize.Y;
        var ppu = (float)EditorApplication.Config.PixelsPerUnit;
        var halfW = w / 2;
        var halfH = h / 2;

        using var writer = new BinaryWriter(File.Create(spritePath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);

        // Header
        writer.Write((ushort)1);
        writer.Write((ushort)(Atlas?.Index ?? 0));
        writer.Write((short)(-halfW));
        writer.Write((short)(-halfH));
        writer.Write((short)(w - halfW));
        writer.Write((short)(h - halfH));
        writer.Write(ppu);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((short)-1);
        writer.Write((ushort)1);
        writer.Write(12.0f);

        // Mesh
        writer.Write(AtlasUV.Left);
        writer.Write(AtlasUV.Top);
        writer.Write(AtlasUV.Right);
        writer.Write(AtlasUV.Bottom);
        writer.Write((short)0);
        writer.Write((short)-1);
        writer.Write((short)(-halfW));
        writer.Write((short)(-halfH));
        writer.Write((short)w);
        writer.Write((short)h);

        // Frame table
        writer.Write((ushort)0);
        writer.Write((ushort)1);
    }
}
