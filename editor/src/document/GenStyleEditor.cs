//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class GenStyleEditor : DocumentEditor
{
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

        Document.Prompt = Inspector.StringProperty(Document.Prompt, handler: Document, placeholder: "Prompt", multiLine: true);
        Document.NegativePrompt = Inspector.StringProperty(Document.NegativePrompt, handler: Document, placeholder: "Negative Prompt", multiLine: true);
        Document.DefaultStrength = Inspector.SliderProperty(Document.DefaultStrength, handler: Document);
        Document.DefaultGuidanceScale = Inspector.SliderProperty(Document.DefaultGuidanceScale, minValue: 1.0f, maxValue: 20.0f, handler: Document);
    }

    private void RefineDefaultsUI()
    {
        using var _ = Inspector.BeginSection("REFINE DEFAULTS");
        if (Inspector.IsSectionCollapsed) return;

        Document.RefinePrompt = Inspector.StringProperty(Document.RefinePrompt, handler: Document, placeholder: "Refine Prompt", multiLine: true);
        Document.RefineNegativePrompt = Inspector.StringProperty(Document.RefineNegativePrompt, handler: Document, placeholder: "Negative Prompt", multiLine: true);
        Document.RefineStrength = Inspector.SliderProperty(Document.RefineStrength, handler: Document);
        Document.RefineGuidanceScale = Inspector.SliderProperty(Document.RefineGuidanceScale, minValue: 1.0f, maxValue: 20.0f, handler: Document);
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
                UI.Label(name, EditorStyle.Text.Primary);
                var newStrength = Inspector.SliderProperty(strength, handler: Document);
                if (MathF.Abs(newStrength - strength) > float.Epsilon)
                    Document.StyleReferences[i] = (name, newStrength);
            }
        }
    }
}
