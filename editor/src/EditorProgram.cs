//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics.CodeAnalysis;
using NoZ;
using NoZ.Platform;
using NoZ.Editor;
using NoZ.Platform.WebGPU;

[assembly: SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "<Pending>", Scope = "member", Target = "~M:NoZ.Editor.RenameTool.Dispose")]

// Log.Path = "log.txt";

// Check if running in init mode first
var initProject = false;
string? initProjectName = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--init")
    {
        initProject = true;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            initProjectName = args[++i];
        break;
    }
}

// Find the editor root - try current directory first, then assembly location
var editorPath = Directory.GetCurrentDirectory();
if (!Directory.Exists(Path.Combine(editorPath, "library")))
{
    // Try assembly location (for running from bin/Debug when IDE sets cwd there)
    editorPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
}

if (!Directory.Exists(Path.Combine(editorPath, "library")))
{
    // If still not found, walk up to find library folder
    if (initProject)
    {
        // For --init mode, walk up from assembly location to find editor directory
        editorPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        editorPath = Path.GetDirectoryName(editorPath)!; // Debug
        editorPath = Path.GetDirectoryName(editorPath)!; // bin
        editorPath = Path.GetDirectoryName(editorPath)!; // editor
        Directory.CreateDirectory(Path.Combine(editorPath, "library"));
    }
    else
    {
        Log.Info("Searching for editor root...");
        var searchPath = editorPath;
        while (searchPath != null)
        {
            Log.Info($"Tying {Path.Combine(searchPath, "library")}");
            if (Directory.Exists(Path.Combine(searchPath, "library")))
            {
                editorPath = searchPath;
                break;
            }
            searchPath = Path.GetDirectoryName(searchPath);
        }

        if (!Directory.Exists(Path.Combine(editorPath, "library")))
        {
            Log.Error($"Could not find editor root (no 'library' folder found)");
            return;
        }
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
    // For --init mode, resolve relative to current directory
    // For normal editor mode, resolve relative to editor path (for IDE launch settings)
    var basePath = initProject ? Directory.GetCurrentDirectory() : editorPath;
    projectPath = Path.Combine(basePath, projectPath);
    projectPath = Path.GetFullPath(projectPath);
}

// Handle project initialization before normal startup
if (initProject)
{
    try
    {
        var projectName = initProjectName ?? Path.GetFileName(Path.GetFullPath(projectPath));
        ProjectInitializer.Initialize(projectPath, projectName, editorPath);

        // Import initial assets from noz engine
        Log.Info("");
        Log.Info("Importing assets...");
        EditorApplication.Init(editorPath, projectPath, clean: false);

        Log.Info($"Project '{projectName}' initialized successfully at {projectPath}");
        Log.Info("");
        Log.Info("Next steps:");
        Log.Info("  1. cd " + projectPath);
        Log.Info("  2. git init");
        Log.Info("  3. git submodule add https://github.com/nozgames/noz-cs noz");
        Log.Info("  4. git submodule update --init --recursive");
        Log.Info("  5. dotnet restore");
        Log.Info("  6. dotnet build");
        Log.Info("");
        Log.Info("To open in the editor:");
        Log.Info("  noz --project " + projectPath);
        return;
    }
    catch (Exception ex)
    {
        Log.Error($"Failed to initialize project: {ex.Message}");
        Environment.Exit(1);
    }
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
        DefaultFont = EditorAssets.Names.Seguisb,
        ScaleMode = UIScaleMode.ConstantPixelSize,
    },
    Graphics = new GraphicsConfig
    {
        Driver = new WebGPUGraphicsDriver(),
        PixelsPerUnit = EditorApplication.Config.PixelsPerUnit
    }
});

Application.Run();

EditorApplication.Shutdown();
Application.Shutdown();
