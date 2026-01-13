using System.Numerics;
using StbImageSharp;

namespace noz.editor;

public class TextureDocument : Document
{
    private const float PixelsPerUnit = 51.2f;

    public float Scale { get; set; } = 1f;
    public Texture? Texture { get; private set; }

    public static void RegisterDef()
    {
        DocumentDef.Register(new DocumentDef(
            AssetType.Texture,
            ".png",
            () => new TextureDocument()
        ));
    }

    public override void LoadMetadata(PropertySet meta)
    {
        IsEditorOnly = meta.GetBool("texture", "reference", false) || Path.Contains("reference", StringComparison.OrdinalIgnoreCase);
        Scale = meta.GetFloat("editor", "scale", 1f);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetFloat("editor", "scale", Scale);
        meta.SetBool("texture", "reference", IsEditorOnly);
    }

    public override void PostLoad()
    {
        Texture = Asset.Load(AssetType.Texture, Name) as Texture;
        UpdateBounds();
    }

    public void UpdateBounds()
    {
        if (Texture != null)
        {
            var tsize = new Vector2(Texture.Width, Texture.Height) / PixelsPerUnit;
            Bounds = new Rect(-tsize.X * 0.5f * Scale, -tsize.Y * 0.5f * Scale, tsize.X * Scale, tsize.Y * Scale);
        }
        else
        {
            Bounds = new Rect(-0.5f * Scale, -0.5f * Scale, Scale, Scale);
        }
    }

    public override void Draw()
    {
        if (Texture == null)
            return;

        var size = Bounds.Size;
        Render.DrawQuad(
            Position.X - size.X * 0.5f,
            Position.Y - size.Y * 0.5f,
            size.X, size.Y,
            0, 0, 1, 1,
            Texture.Handle,
            Color32.White,
            layer: 64
        );
    }

    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        using var stream = File.OpenRead(Path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        var filter = meta.GetString("texture", "filter", "linear");
        var clamp = meta.GetString("texture", "clamp", "clamp");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath) ?? "");

        using var writer = new BinaryWriter(File.Create(outputPath));

        // Asset header
        writer.Write(Constants.AssetSignature);
        writer.Write((byte)AssetType.Texture);
        writer.Write(Texture.Version);
        writer.Write((ushort)0);

        // Texture format
        var format = TextureFormat.RGBA8;
        var filterEnum = filter is "nearest" or "point" ? TextureFilter.Nearest : TextureFilter.Linear;
        var clampEnum = clamp == "repeat" ? TextureClamp.Repeat : TextureClamp.Clamp;

        writer.Write((byte)format);
        writer.Write((byte)filterEnum);
        writer.Write((byte)clampEnum);
        writer.Write((uint)image.Width);
        writer.Write((uint)image.Height);
        writer.Write(image.Data);
    }
}
