//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal class AtlasEditor : DocumentEditor
{
    public new AtlasDocument Document => (AtlasDocument)base.Document;

    private readonly Command[] _commands;

    public AtlasEditor(AtlasDocument document) : base(document)
    {
        _commands =
        [
            new Command { Name = "Rebuild Atlas", Handler = RebuildAtlas, Key = InputCode.KeyR, Ctrl = true }
        ];

        Commands = _commands;
    }

    public override void Update()
    {
        Document.Draw();
        DrawOutlines();
    }

    public override void UpdateUI()
    {
        using (UI.BeginCanvas(id: EditorStyle.CanvasId.DocumentEditor))
        using (UI.BeginContainer(EditorStyle.Overlay.Root with
        {
            AlignX = Align.Center,
            AlignY = Align.Max,
            Width = Size.Fit,
            Height = Size.Fit,
            Margin = EdgeInsets.Bottom(20f)
        }))
        using (UI.BeginColumn(ContainerStyle.Default with { Spacing = 8f }))
        {
            using (UI.BeginRow(ContainerStyle.Default with { AlignX = Align.Center, Spacing = 6f }))
            {
                if (EditorUI.Button(1, "Rebuild"))
                    RebuildAtlas();
            }

            using (UI.BeginContainer(ContainerStyle.Default with { AlignX = Align.Center }))
            {
                var stats = $"{Document.RectCount} sprites";
                UI.Label(stats, new LabelStyle
                {
                    FontSize = EditorStyle.Overlay.TextSize,
                    Color = EditorStyle.Overlay.TextColor,
                    AlignX = Align.Center,
                    AlignY = Align.Center
                });
            }
        }
    }

    private void RebuildAtlas()
    {
        Document.Rebuild();
        Notifications.Add($"Atlas '{Document.Name}' rebuilt");
    }

    private void DrawOutlines()
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Gizmos.SetColor(Color.Yellow);

            var lineWidth = EditorStyle.Workspace.DocumentBoundsLineWidth;
            var atlasSize = EditorApplication.Config.AtlasSize;
            var ppu = (float)EditorApplication.Config.PixelsPerUnit;

            foreach (var rect in Document.Rects)
            {
                if (rect.Sprite == null)
                    continue;

                var x = (rect.Rect.X + 1 - atlasSize * 0.5f) / ppu;
                var y = (rect.Rect.Y + 1 - atlasSize * 0.5f) / ppu;
                var w = (rect.Rect.Width - 2) / ppu;
                var h = (rect.Rect.Height - 2) / ppu;

                var bounds = new Rect(x, y, w, h);
                Gizmos.DrawRect(bounds, lineWidth);
            }
        }
    }
}
