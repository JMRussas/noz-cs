using StbImageSharp;

namespace noz.editor;

public class TextureDocument : Document
{
    public float Scale { get; set; } = 1f;

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
