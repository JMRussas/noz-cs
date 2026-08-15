//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NoZ.Editor;

public static class Program
{
    public static void Main(string[] args)
    {
        var editorPath = FindEditorPath();
        if (editorPath == null)
        {
            Console.WriteLine("Could not find editor directory. Expected to find library/ folder and NoZ.Editor.csproj");
            return;
        }

        if (args.Length > 0 && !args[0].StartsWith("--"))
        {
            switch(args[0])
            {
                case "import":
                    ImportProject(editorPath!, args);
                    return;

                case "host":
                    RunRemoteHost(args).Wait();
                    return;

                case "init":
                    InitProject(editorPath!, args);
                    break;
                    
                default:
                    Console.WriteLine($"Unknown command: {args[0]}");
                    return;
            }
        }
        else
        {
            OpenProject(editorPath, args);
        }

    }

    private static void ImportProject(string editorPath, string[] args)
    {
        AttachParentConsole();

        string projectPath = Environment.CurrentDirectory;
        var clean = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length)
                projectPath = args[++i];
            else if (args[i] == "--clean")
                clean = true;
        }        

        var configProps = PropertySet.LoadFile(Path.Combine(projectPath, "editor.cfg"));
        if (configProps == null)
        {
            Console.WriteLine("error: editor.cfg not found");
            return;
        }

        var config = new EditorConfig(configProps);
        var outputPath = Path.Combine(projectPath, config.OutputPath);
        Console.WriteLine("project.path = " + projectPath);
        Console.WriteLine("output.path = " + outputPath);

        if (clean)
        {
            Console.WriteLine("cleaning...");
            
            if (Directory.Exists(outputPath))
            {
                var di = new DirectoryInfo(outputPath);
                foreach (FileInfo file in di.GetFiles())
                    file.Delete(); 
                foreach (DirectoryInfo dir in di.GetDirectories())
                    dir.Delete(true);
            }
        }

        // Use the headless project load so a CLI import matches the GUI: palettes and
        // collections initialized, documents loaded, atlases packed, assets exported and
        // the generated asset manifest (config.GenerateCs) written.
        if (!EditorApplication.LoadProjectHeadless(projectPath, editorPath))
            return;

        Project.Shutdown();
    }


    private static void InitProject(string editorPath, string[] args)
    {
        AttachParentConsole();

        var projectPath = Environment.CurrentDirectory;
        string? projectName = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length)
                projectPath = args[++i];
            else if (args[i] == "--name" && i + 1 < args.Length)
                projectName = args[++i];
        }

        projectName ??= Path.GetFileName(Path.GetFullPath(projectPath));

        try
        {
            ProjectCreator.Create(projectPath, projectName, editorPath);
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"error: failed to initialize project: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static async Task RunRemoteHost(string[] args)
    {
        var remotePort = RemoteProtocol.DefaultPort;
        var projectPath = Environment.CurrentDirectory;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
                remotePort = int.Parse(args[++i]);
            else if (args[i] == "--project" && i + 1 < args.Length)
                projectPath = args[++i];
        }

        AttachParentConsole();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        await RemoteHost.RunAsync(Path.GetFullPath(projectPath), remotePort, cts.Token);
    }

    private static void OpenProject(string editorPath, string[] args)
    {
        var forwardArgs = new List<string>();
        var projectPath = Environment.CurrentDirectory;
        var tablet = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length)
                projectPath = args[++i];
            else if (args[i] == "--tablet")
                tablet = true;
            else
                forwardArgs.Add(args[i]);
        }

        EditorApplication.Run(new EditorApplicationConfig
        {
            ProjectPath = Path.GetFullPath(projectPath),
            EditorPath = editorPath,
            IsTablet = tablet,
        }, [.. forwardArgs]);

        Application.Shutdown();
    }

    private static string? FindEditorPath()
    {
        string? editorPath = AppContext.BaseDirectory;
        while (editorPath != null)
        {
            if (Directory.Exists(Path.Combine(editorPath, "library")) &&
                File.Exists(Path.Combine(editorPath, "NoZ.Editor.csproj")))
                break;

            editorPath = Path.GetDirectoryName(editorPath)!;
        }

        return editorPath;
    }

    [SupportedOSPlatform("windows")]
    private static void AttachParentConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const int ATTACH_PARENT_PROCESS = -1;
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
            AllocConsole();

        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput()));
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool AllocConsole();
}


