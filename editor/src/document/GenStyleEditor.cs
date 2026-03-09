//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class GenStyleEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId LayerPrompt { get; }
        public static partial WidgetId LayerNegativePrompt { get; }
        public static partial WidgetId LayerStrength { get; }
        public static partial WidgetId LayerGuidance { get; }
        public static partial WidgetId LayerSteps { get; }
        public static partial WidgetId RefinePrompt { get; }
        public static partial WidgetId RefineNegativePrompt { get; }
        public static partial WidgetId RefineStrength { get; }
        public static partial WidgetId RefineGuidance { get; }
        public static partial WidgetId RefineSteps { get; }
        public static partial WidgetId StyleRefStrength { get; }
        public static partial WidgetId StyleRefDelete { get; }
        public static partial WidgetId AddStyleRefDropDown { get; }
    }

    public new GenStyleDocument Document => (GenStyleDocument)base.Document;

    public override bool ShowInspector => true;

    public GenStyleEditor(GenStyleDocument doc) : base(doc)
    {
        Commands =
        [
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
        ];
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.Draw();
    }

    public override void InspectorUI()
    {
        LayerDefaultsUI();
        RefineDefaultsUI();
        StyleReferencesUI();
    }

    private void LayerDefaultsUI()
    {
        using var _ = Inspector.BeginSection("LAYER DEFAULTS");
        if (Inspector.IsSectionCollapsed) return;

        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            using (UI.BeginFlex())
            {
                var steps = Document.DefaultSteps;
                if (UI.NumberInput(WidgetIds.LayerSteps, ref steps, EditorStyle.TextInput, min: 1, max: 100, icon: EditorAssets.Sprites.IconSort))
                {
                    Undo.Record(Document);
                    Document.DefaultSteps = steps;
                }
            }

            using (UI.BeginFlex())
            {
                var strength = Document.DefaultStrength;
                if (UI.NumberInput(WidgetIds.LayerStrength, ref strength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00", icon: EditorAssets.Sprites.IconOpacity))
                {
                    Undo.Record(Document);
                    Document.DefaultStrength = strength;
                }
            }

            using (UI.BeginFlex())
            {
                var guidance = Document.DefaultGuidanceScale;
                if (UI.NumberInput(WidgetIds.LayerGuidance, ref guidance, EditorStyle.TextInput, min: 0f, max: 30f, step: 0.1f, format: "0.0", icon: EditorAssets.Sprites.IconConstraint))
                {
                    Undo.Record(Document);
                    Document.DefaultGuidanceScale = guidance;
                }
            }
        }

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.Prompt = UI.TextInput(WidgetIds.LayerPrompt, Document.Prompt, EditorStyle.TextArea, "Prompt", Document);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.NegativePrompt = UI.TextInput(WidgetIds.LayerNegativePrompt, Document.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document);
    }

    private void RefineDefaultsUI()
    {
        using var _ = Inspector.BeginSection("REFINE DEFAULTS");
        if (Inspector.IsSectionCollapsed) return;

        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            using (UI.BeginFlex())
            {
                var steps = Document.RefineSteps;
                if (UI.NumberInput(WidgetIds.RefineSteps, ref steps, EditorStyle.TextInput, min: 1, max: 100, icon: EditorAssets.Sprites.IconSort))
                {
                    Undo.Record(Document);
                    Document.RefineSteps = steps;
                }
            }

            using (UI.BeginFlex())
            {
                var strength = Document.RefineStrength;
                if (UI.NumberInput(WidgetIds.RefineStrength, ref strength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00", icon: EditorAssets.Sprites.IconOpacity))
                {
                    Undo.Record(Document);
                    Document.RefineStrength = strength;
                }
            }

            using (UI.BeginFlex())
            {
                var guidance = Document.RefineGuidanceScale;
                if (UI.NumberInput(WidgetIds.RefineGuidance, ref guidance, EditorStyle.TextInput, min: 0f, max: 30f, step: 0.1f, format: "0.0", icon: EditorAssets.Sprites.IconConstraint))
                {
                    Undo.Record(Document);
                    Document.RefineGuidanceScale = guidance;
                }
            }
        }

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.RefinePrompt = UI.TextInput(WidgetIds.RefinePrompt, Document.RefinePrompt, EditorStyle.TextArea, "Refine Prompt", Document);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            Document.RefineNegativePrompt = UI.TextInput(WidgetIds.RefineNegativePrompt, Document.RefineNegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document);
    }

    private void StyleReferencesUI()
    {
        using var _ = Inspector.BeginSection("STYLE REFERENCES");
        if (Inspector.IsSectionCollapsed) return;

        for (int i = 0; i < Document.StyleReferences.Count; i++)
        {
            var (name, strength) = Document.StyleReferences[i];
            using (Inspector.BeginRow())
            {
                UI.Text(name, EditorStyle.Text.Primary with { AlignY = Align.Center });
                UI.Flex();

                using (UI.BeginContainer(new ContainerStyle { Width = 60 }))
                {
                    if (UI.NumberInput(WidgetIds.StyleRefStrength + i, ref strength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00"))
                    {
                        Undo.Record(Document);
                        Document.StyleReferences[i] = (name, strength);
                    }
                }

                if (UI.Button(WidgetIds.StyleRefDelete + i, EditorAssets.Sprites.IconDelete, EditorStyle.Button.SmallIconOnly))
                {
                    Undo.Record(Document);
                    Document.StyleReferences.RemoveAt(i);
                    Document.IncrementVersion();
                }
            }
        }

        AddStyleRefUI();
    }

    private void AddStyleRefUI()
    {
        var existing = new HashSet<string>(Document.StyleReferences.Select(r => r.TextureName));
        var names = new List<string>();

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not TextureDocument) continue;
            if (existing.Contains(doc.Name)) continue;
            names.Add(doc.Name);
        }

        if (names.Count == 0) return;
        names.Sort(StringComparer.OrdinalIgnoreCase);

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
        {
            var selected = AssetBrowser.Show(WidgetIds.AddStyleRefDropDown, names.ToArray());
            if (selected != null)
            {
                Undo.Record(Document);
                Document.StyleReferences.Add((selected, 0.5f));
                Document.IncrementVersion();
            }
        }
    }
}
