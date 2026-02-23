//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

[Flags]
public enum ShaderFlags : byte
{
    None = 0,
    Blend = 1 << 0,
    Depth = 1 << 1,
    DepthLess = 1 << 2,
    PremultipliedAlpha = 1 << 3,
}

public enum ShaderBindingType : byte
{
    UniformBuffer = 0,
    Texture2D = 1,
    Texture2DArray = 2,
    Sampler = 3,
    Texture2DUnfilterable = 4  // For RGBA32F textures that use textureLoad
}

public struct ShaderBinding
{
    public uint Binding;
    public ShaderBindingType Type;
    public string Name;
}

public class Shader : Asset
{
    internal const byte Version = 3;

    public ShaderFlags Flags { get; private set; }
    public List<ShaderBinding> Bindings { get; private set; } = new();
    public string Source { get; private set; } = "";
    public uint VertexFormatHash { get; private set; }

    private Shader(string name) : base(AssetType.Shader, name)
    {
    }

    private static Asset? Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var sourceLength = reader.ReadUInt32();
        var source = System.Text.Encoding.UTF8.GetString(reader.ReadBytes((int)sourceLength));
        var flags = (ShaderFlags)reader.ReadByte();
        var bindingCount = (int)reader.ReadByte();
        var bindings = new List<ShaderBinding>(bindingCount);
        for (uint i = 0; i < bindingCount; i++)
        {
            var binding = new ShaderBinding
            {
                Binding = reader.ReadByte(),
                Type = (ShaderBindingType)reader.ReadByte(),
                Name = reader.ReadString()
            };
            bindings.Add(binding);
        }
        var vertexFormatHash = reader.ReadUInt32();

        var shader = new Shader(name)
        {
            Flags = flags,
            Source = source,
            Bindings = bindings,
            VertexFormatHash = vertexFormatHash,
        };

        shader.Handle = Graphics.Driver.CreateShader(name, source, source, bindings);
        return shader;
    }

    public override void Dispose()
    {
        if (Handle != nuint.Zero)
        {
            Graphics.Driver.DestroyShader(Handle);
            Handle = nuint.Zero;
        }

        base.Dispose();
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Shader, "Shader", typeof(Shader), Load, Version));
    }
}
