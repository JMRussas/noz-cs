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
    Postprocess = 1 << 3,
    UiComposite = 1 << 4,
    PremultipliedAlpha = 1 << 5,
}

public enum ShaderBindingType : byte
{
    UniformBuffer = 0,
    Texture2D = 1,
    Texture2DArray = 2,
    Sampler = 3
}

public struct ShaderBinding
{
    public uint Binding;
    public ShaderBindingType Type;
    public string Name;
}

public class Shader : Asset
{
    internal const byte Version = 2;

    public ShaderFlags Flags { get; private set; }
    internal nuint Handle { get; private set; }
    public List<ShaderBinding> Bindings { get; private set; } = new();
    public string VertexSource { get; private set; } = "";
    public string FragmentSource { get; private set; } = "";

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

        // Read binding metadata if present
        var bindings = new List<ShaderBinding>();
        if (stream.Position < stream.Length)
        {
            var bindingCount = reader.ReadUInt32();
            for (uint i = 0; i < bindingCount; i++)
            {
                var binding = new ShaderBinding
                {
                    Binding = reader.ReadUInt32(),
                    Type = (ShaderBindingType)reader.ReadByte(),
                    Name = reader.ReadString()
                };
                bindings.Add(binding);
            }
        }

        // Auto-detect shader types by name
        if (name.Contains("postprocess_ui_composite"))
            flags |= ShaderFlags.UiComposite;
        else if (name.Contains("postprocess"))
            flags |= ShaderFlags.Postprocess;

        var shader = new Shader(name)
        {
            Flags = flags,
            VertexSource = vertexSource,
            FragmentSource = fragmentSource,
            Bindings = bindings
        };

        // Use metadata-based shader creation if bindings are available and driver supports it
        if (bindings.Count > 0)
        {
            // Use reflection to check if driver has CreateShaderFromMetadata method (WebGPU driver)
            var driverType = Graphics.Driver.GetType();
            var method = driverType.GetMethod("CreateShaderFromMetadata",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            if (method != null)
            {
                shader.Handle = (nuint)method.Invoke(Graphics.Driver, new object[] { name, vertexSource, fragmentSource, bindings })!;
            }
            else
            {
                // Driver doesn't support metadata, use legacy path
                shader.Handle = Graphics.Driver.CreateShader(name, vertexSource, fragmentSource);
            }
        }
        else
        {
            // No bindings metadata, use legacy path
            shader.Handle = Graphics.Driver.CreateShader(name, vertexSource, fragmentSource);
        }

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
        RegisterDef(new AssetDef(AssetType.Shader, typeof(Shader), Load));
    }
}
