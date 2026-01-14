//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz.editor;

public class DocumentDef(AssetType type, string extension, Func<Document> factory)
{
    public AssetType Type { get; } = type;
    public string Extension { get; } = extension.ToLowerInvariant();
    public Func<Document> Factory { get; } = factory;
}
