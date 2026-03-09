//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;

namespace NoZ.Editor;

public class GenStyleDocument : Document
{
    public static readonly AssetType AssetTypeGenStyle = AssetType.FromString("GNST");

    public override bool CanSave => true;

    public string Prompt = "";
    public string NegativePrompt = "";
    public int DefaultSteps = 30;
    public float DefaultStrength = 0.8f;
    public float DefaultGuidanceScale = 6.0f;
    public string RefinePrompt = "";
    public string RefineNegativePrompt = "";
    public int RefineSteps = 30;
    public float RefineStrength = 0.64f;
    public float RefineGuidanceScale = 6.0f;
    public List<(string TextureName, float Strength)> StyleReferences = new();

    public GenStyleDocument()
    {
        IsEditorOnly = true;
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetTypeGenStyle,
            Name = "GenStyle",
            Extension = ".genstyle",
            Factory = () => new GenStyleDocument(),
            EditorFactory = doc => new GenStyleEditor((GenStyleDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("prompt \"\"");
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Parse(ref tk);
        Loaded = true;
    }

    private void Parse(ref Tokenizer tk)
    {
        StyleReferences.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("prompt"))
                Prompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("prompt_neg"))
                NegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("steps"))
                DefaultSteps = tk.ExpectInt(30);
            else if (tk.ExpectIdentifier("strength"))
                DefaultStrength = tk.ExpectFloat(0.8f);
            else if (tk.ExpectIdentifier("guidance_scale"))
                DefaultGuidanceScale = tk.ExpectFloat(6.0f);
            else if (tk.ExpectIdentifier("refine_prompt"))
                RefinePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("refine_prompt_neg"))
                RefineNegativePrompt = tk.ExpectQuotedString() ?? "";
            else if (tk.ExpectIdentifier("refine_steps"))
                RefineSteps = tk.ExpectInt(30);
            else if (tk.ExpectIdentifier("refine_strength"))
                RefineStrength = tk.ExpectFloat(0.64f);
            else if (tk.ExpectIdentifier("refine_guidance_scale"))
                RefineGuidanceScale = tk.ExpectFloat(6.0f);
            else if (tk.ExpectIdentifier("style_ref"))
            {
                var name = tk.ExpectQuotedString() ?? "";
                var strength = tk.ExpectFloat(0.5f);
                if (!string.IsNullOrEmpty(name))
                    StyleReferences.Add((name, strength));
            }
            else
            {
                tk.ExpectToken(out var badToken);
                Log.Error($"GenStyleDocument.Load: Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
    }

    public override void Save(StreamWriter writer)
    {
        if (!string.IsNullOrEmpty(Prompt))
            writer.WriteLine($"prompt \"{Prompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(NegativePrompt))
            writer.WriteLine($"prompt_neg \"{NegativePrompt.Replace("\"", "\\\"")}\"");
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "steps {0}", DefaultSteps));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "strength {0}", DefaultStrength));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "guidance_scale {0}", DefaultGuidanceScale));
        if (!string.IsNullOrEmpty(RefinePrompt))
            writer.WriteLine($"refine_prompt \"{RefinePrompt.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrEmpty(RefineNegativePrompt))
            writer.WriteLine($"refine_prompt_neg \"{RefineNegativePrompt.Replace("\"", "\\\"")}\"");
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "refine_steps {0}", RefineSteps));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "refine_strength {0}", RefineStrength));
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "refine_guidance_scale {0}", RefineGuidanceScale));

        foreach (var (name, strength) in StyleReferences)
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "style_ref \"{0}\" {1}", name, strength));
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconSprite);
        }
    }
}
