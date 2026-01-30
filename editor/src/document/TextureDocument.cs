//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public class TextureDocument : Document
{
    public const float PixelsPerUnit = 256.0f;
    public const float PixelsPerUnitInv = 1.0f / PixelsPerUnit;

    public float Scale { get; set; } = 1f;
    public Texture? Texture { get; private set; }

    public override bool CanSave => false;

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Texture,
            Extension = ".png",
            Factory = () => new TextureDocument(),
            EditorFactory = doc => new TextureEditor((TextureDocument)doc),
            CanEdit = doc => ((TextureDocument)doc).IsEditorOnly
        });
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
}
