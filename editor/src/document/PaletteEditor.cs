//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class PaletteEditor : DocumentEditor
{
    private static partial class WidgetIds 
    {
        public static partial WidgetId InspectorRoot { get; }
        public static partial WidgetId InspectorScroll { get; }
        public static partial WidgetId ColorItem { get; }
    }

    public new PaletteDocument Document => (PaletteDocument)base.Document;

    public PaletteEditor(PaletteDocument doc) : base(doc)
    {
        Commands =
        [
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
        ];
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.DrawBounds(selected: false);
        Document.Draw();
    }

    public override void UpdateUI()
    {
        using (UI.BeginColumn(WidgetIds.InspectorRoot, EditorStyle.Inspector.Root))
        {
            UI.Spacer(EditorStyle.Control.Spacing / 2);
            using (UI.BeginRow(EditorStyle.Inspector.SectionHeader))
                UI.Text($"{Document.ColorCount} colors", EditorStyle.Inspector.SectionText);
            UI.Container(EditorStyle.Inspector.Separator);

            using (UI.BeginScrollable(WidgetIds.InspectorScroll, new ScrollableStyle
            {
                ScrollSpeed = 40
            }))
            {
                using (UI.BeginGrid(new GridStyle
                {
                    CellHeight = EditorStyle.Control.Height,
                    CellWidth = EditorStyle.Control.Height,
                    Columns = 6
                }))
                {
                    var nextId = WidgetIds.ColorItem;
                    for (int i = 0; i < Document.ColorCount; i++)
                    {
                        var color = Document.Colors[i];
                        var itemId = nextId++;

                        using (UI.BeginContainer(itemId, new ContainerStyle
                        {
                            Padding = EdgeInsets.All(3),
                            BorderRadius = EditorStyle.Control.BorderRadius
                        }))
                        {
                            UI.Container(new ContainerStyle
                            {
                                Color = color,
                                BorderRadius = EditorStyle.Control.BorderRadius - 2
                            });

                            // TODO: tooltip on hover with color name
                        }
                    }
                }
            }
        }
    }
}
