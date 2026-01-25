//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ;
using NoZ.Platform;
using NoZ.Editor;
using NoZ.Platform.WebGPU;

// Log.Path = "log.txt";

// Find the editor root by walking up from the executable location
var editorPath = Directory.GetCurrentDirectory();
if (!Directory.Exists(Path.Combine(editorPath, "library")))
{
    Log.Info("Searching for editor root...");
    editorPath = Path.GetDirectoryName(editorPath);
    while (editorPath != null)
    {
        Log.Info($"Tying {Path.Combine(editorPath, "library")}");
        if (Directory.Exists(Path.Combine(editorPath, "library")))
            break;

        editorPath = Path.GetDirectoryName(editorPath);
    }

    if (editorPath == null)
    {
        Log.Error($"Could not find editor root (no 'library' folder found above {editorPath})");
        return;
    }
}

string projectPath = editorPath;
var clean = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length)
        projectPath = args[++i];
    else if (args[i] == "--clean")
        clean = true;
}

if (projectPath.StartsWith('.'))
{
    projectPath = Path.Combine(editorPath, projectPath);
    projectPath = Path.GetFullPath(projectPath);
}

if (!File.Exists(Path.Combine(projectPath, "editor.cfg")))
{
    Log.Info("Searching for project path...");
    projectPath = Path.GetDirectoryName(projectPath)!;
    while (projectPath != null)
    {
        Log.Info($"Tying {projectPath}");
        if (File.Exists(Path.Combine(projectPath, "editor.cfg")))
            break;

        projectPath = Path.GetDirectoryName(projectPath)!;
    }

    if (projectPath == null)
    {
        Log.Error($"Could not find project path (no 'editor.cfg' folder found above {projectPath})");
        return;
    }
}

Log.Info($"Editor Path: {editorPath}");
Log.Info($"Project Path: {projectPath}");

EditorApplication.Init(editorPath, projectPath, clean);

Application.Init(new ApplicationConfig
{
    Title = "NoZ Editor",
    Width = 1600,
    Height = 900,
    IconPath = "res/windows/nozed.png",
    Platform = new SDLPlatform(),
    AudioBackend = new SDLAudioDriver(),
    Vtable = new EditorApplicationInstance(),
    AssetPath = Path.Combine(EditorApplication.EditorPath, "library"),

    UI = new UIConfig()
    {
        DefaultFont = EditorAssets.Names.Seguisb
    },
    Graphics = new GraphicsConfig
    {
        Driver = new WebGPUGraphicsDriver(),
        CompositeShader = "composite",
        PixelsPerUnit = EditorApplication.Config.PixelsPerUnit
    }
});

Application.Run();

EditorApplication.Shutdown();
Application.Shutdown();
