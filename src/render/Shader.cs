//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

[Flags]
public enum ShaderFlags : byte
{
    None = 0,
    Blend = 1 << 0,
    Depth = 1 << 1,
    DepthLess = 1 << 2,
    Postprocess = 1 << 3,
    UiComposite = 1 << 4,
    PremultipliedAlpha = 1 << 5,
}

public class Shader : Asset
{
    internal const byte Version = 2;

    public ShaderFlags Flags { get; private set; }
    internal ShaderHandle Handle { get; private set; }

    private Shader(string name) : base(AssetType.Shader, name)
    {
    }

    private static Asset? Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var vertexLength = reader.ReadUInt32();
        var vertexSource = System.Text.Encoding.UTF8.GetString(reader.ReadBytes((int)vertexLength));

        var fragmentLength = reader.ReadUInt32();
        var fragmentSource = System.Text.Encoding.UTF8.GetString(reader.ReadBytes((int)fragmentLength));

        var flags = (ShaderFlags)reader.ReadByte();

        // Auto-detect shader types by name
        if (name.Contains("postprocess_ui_composite"))
            flags |= ShaderFlags.UiComposite;
        else if (name.Contains("postprocess"))
            flags |= ShaderFlags.Postprocess;

        var shader = new Shader(name)
        {
            Flags = flags,
            Handle = Application.RenderBackend.CreateShader(vertexSource, fragmentSource)
        };

        return shader;
    }

    public override void Dispose()
    {
        if (Handle.IsValid)
        {
            Application.RenderBackend.DestroyShader(Handle);
            Handle = default;
        }

        base.Dispose();
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Shader, typeof(Shader), Load));
    }
}
