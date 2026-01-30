//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class DocumentDef
{
    public required AssetType Type { get; init; }
    public required string Extension { get; init; }
    public required Func<Document> Factory { get; init; }
    public Action<StreamWriter>? NewFile { get; init; }
    public Func<Document, DocumentEditor>? EditorFactory { get; init; }
    public Func<Document, bool>? CanEdit { get; init; }
    public Func<Sprite?>? Icon { get; init; }
}
