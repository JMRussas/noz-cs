//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class BinDocument : Document
{
    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Bin,
            Extension = ".bin",
            Factory = () => new BinDocument(),
            Icon = () => EditorAssets.Sprites.AssetIconBin
        });
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        var data = File.ReadAllBytes(Path);
        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Bin, Bin.Version);
        writer.Write(data.Length);
        writer.Write(data);
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconBin);
        }
    }
}
