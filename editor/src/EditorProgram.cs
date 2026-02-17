//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NoZ;
using NoZ.Platform;
using NoZ.Editor;
using NoZ.Platform.WebGPU;

[assembly: SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "<Pending>", Scope = "member", Target = "~M:NoZ.Editor.RenameTool.Dispose")]

// Log.Path = "log.txt";

// Check if running in init mode first
var initProject = false;
var importOnly = false;
string? initProjectName = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--init")
    {
        initProject = true;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            initProjectName = args[++i];
    }
    else if (args[i] == "--import")
    {
        importOnly = true;
    }
}

// For CLI modes on Windows, attach to parent console so Console.WriteLine output is visible
// (WinExe has no console by default; Linux/Mac don't need this)
if ((importOnly || initProject) && OperatingSystem.IsWindows())
    AttachConsole(-1);

// Find editor path by walking up from assembly location
// Look for directory with BOTH library/ AND NoZ.Editor.csproj
var assemblyPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
var editorPath = assemblyPath;

while (editorPath != null)
{
    if (Directory.Exists(Path.Combine(editorPath, "library")) &&
        File.Exists(Path.Combine(editorPath, "NoZ.Editor.csproj")))
    {
        break;
    }
    editorPath = Path.GetDirectoryName(editorPath);
}

if (editorPath == null)
{
    if (initProject)
    {
        // For --init mode, we don't need library/ to exist yet
        // Just walk up from bin/Debug/net10.0 to editor directory
        editorPath = Path.GetFullPath(Path.Combine(assemblyPath, "../../.."));

        // Create library folder for init mode
        Directory.CreateDirectory(Path.Combine(editorPath, "library"));
    }
    else
    {
        Log.Error("Could not find editor directory. Expected to find library/ folder and NoZ.Editor.csproj");
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
    // Resolve relative paths relative to current directory
    projectPath = Path.Combine(Directory.GetCurrentDirectory(), projectPath);
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

// Import-only mode: import assets and exit without launching GUI
if (importOnly)
{
    Log.Info("Import complete.");
    Importer.Shutdown();
    return;
}

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

partial class Program
{
    [LibraryImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial void AttachConsole(int dwProcessId);
}
