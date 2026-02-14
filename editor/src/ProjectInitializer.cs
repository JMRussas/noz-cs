//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;
using System.Text;

namespace NoZ.Editor;

/// <summary>
/// Handles initialization of new NoZ game projects
/// </summary>
internal static class ProjectInitializer
{
    public static void Initialize(string projectPath, string projectName, string editorPath)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name cannot be empty", nameof(projectName));

        if (!IsValidProjectName(projectName))
            throw new ArgumentException(
                "Project name must be a valid C# identifier (letters, digits, underscore)",
                nameof(projectName));

        // Check if directory exists and validate contents
        if (Directory.Exists(projectPath))
        {
            // Allow certain expected files/directories from git init and submodule workflow
            var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                "noz",
                ".gitmodules",
                ".gitignore",
                "library",
                "assets"
            };

            var entries = Directory.GetFileSystemEntries(projectPath)
                .Select(Path.GetFileName)
                .Where(name => !allowedNames.Contains(name!))
                .ToArray();

            if (entries.Length > 0)
                throw new InvalidOperationException(
                    $"Directory '{projectPath}' contains unexpected files or directories: {string.Join(", ", entries)}. " +
                    "The directory should be empty or only contain .git, noz submodule, and git-related files.");
        }
        else
        {
            Directory.CreateDirectory(projectPath);
        }

        Log.Info($"Initializing project '{projectName}' at {projectPath}...");

        // Create directory structure
        CreateDirectoryStructure(projectPath);

        // Generate GUIDs for projects
        var guids = new Dictionary<string, Guid>
        {
            ["GAME"] = Guid.NewGuid(),
            ["DESKTOP"] = Guid.NewGuid(),
            ["WEB"] = Guid.NewGuid(),
            ["NOZ"] = Guid.NewGuid(),
            ["NOZ_DESKTOP"] = Guid.NewGuid(),
            ["NOZ_WEBGPU"] = Guid.NewGuid(),
            ["NOZ_WEB"] = Guid.NewGuid(),
            ["PLATFORM_FOLDER"] = Guid.NewGuid(),
            ["SOLUTION"] = Guid.NewGuid(),
        };

        // Create template context
        var context = new TemplateContext
        {
            ProjectName = projectName,
            Namespace = projectName,
            GameAssemblyName = $"{projectName}.Game",
            DesktopAssemblyName = projectName,
            WebAssemblyName = $"{projectName}.Web",
            Guids = guids
        };

        // Generate files from templates
        GenerateFile(projectPath, $"{projectName}.sln", "solution.sln.template", context);
        GenerateFile(Path.Combine(projectPath, "game"), $"{projectName}.csproj", "game.csproj.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "desktop"), $"{projectName}.Desktop.csproj", "desktop.csproj.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "web"), $"{projectName}.Web.csproj", "web.csproj.template", context);
        GenerateFile(Path.Combine(projectPath, "game"), "Game.cs", "GameCs.template", context);
        GenerateFile(Path.Combine(projectPath, "game"), "GameConfig.cs", "GameConfigCs.template", context);
        GenerateFile(Path.Combine(projectPath, "game"), $"{projectName}Assets.cs", "GameAssetsCs.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "desktop"), "Program.cs", "ProgramDesktopCs.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "desktop", "Properties"), "launchSettings.json", "launchSettings.json.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "web"), "Program.cs", "ProgramWebCs.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "web"), "App.razor", "AppRazor.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "web"), "_Imports.razor", "ImportsRazor.template", context);
        GenerateFile(Path.Combine(projectPath, "platform", "web", "wwwroot"), "index.html", "IndexHtml.template", context);
        GenerateFile(projectPath, "editor.cfg", "editor.cfg.template", context);
        GenerateFile(projectPath, ".gitignore", "gitignore.template", context);
        GenerateFile(projectPath, ".gitmodules", "gitmodules.template", context);
        GenerateFile(projectPath, "nuget.config", "nuget.config.template", context);
        GenerateFile(projectPath, ".editorconfig", "editorconfig.template", context);

        // Copy segui font from editor
        CopyFont(editorPath, projectPath);

        Log.Info("Project structure created successfully");
    }

    private static void CreateDirectoryStructure(string root)
    {
        var dirs = new List<string>
        {
            "game",
            "platform/desktop",
            "platform/desktop/Properties",
            "platform/web",
            "platform/web/wwwroot",
            "library",
            "library/font",
            "library/sprite",
            "library/texture",
            "library/shader",
            "library/sound",
            "library/font",
            "library/atlas",
            "library/animation",
            "library/skeleton",
            "library/vfx",
            "assets",
        };

        foreach (var dir in dirs)
        {
            var path = Path.Combine(root, dir);
            Directory.CreateDirectory(path);
            Log.Info($"  Created directory: {dir}");
        }
    }

    private static void GenerateFile(string directory, string filename, string templateName, TemplateContext context)
    {
        var content = LoadTemplate(templateName);
        content = ProcessTemplate(content, context);

        var filePath = Path.Combine(directory, filename);
        File.WriteAllText(filePath, content, Encoding.UTF8);

        var relativePath = Path.GetRelativePath(Directory.GetParent(directory)?.FullName ?? directory, filePath);
        Log.Info($"  Generated: {relativePath}");
    }

    private static string LoadTemplate(string templateName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"ProjectTemplates.{templateName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
            throw new InvalidOperationException($"Template '{templateName}' not found in embedded resources");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ProcessTemplate(string template, TemplateContext context)
    {
        var result = template;

        result = result.Replace("{{PROJECT_NAME}}", context.ProjectName);
        result = result.Replace("{{NAMESPACE}}", context.Namespace);
        result = result.Replace("{{GAME_ASSEMBLY_NAME}}", context.GameAssemblyName);
        result = result.Replace("{{DESKTOP_ASSEMBLY_NAME}}", context.DesktopAssemblyName);
        result = result.Replace("{{WEB_ASSEMBLY_NAME}}", context.WebAssemblyName);

        foreach (var kvp in context.Guids)
        {
            result = result.Replace($"{{{{GUID_{kvp.Key}}}}}", kvp.Value.ToString("B").ToUpper());
        }

        return result;
    }

    private static void CopyFont(string editorPath, string projectPath)
    {
        var sourceFontDir = Path.Combine(editorPath, "library", "font");
        var destFontDir = Path.Combine(projectPath, "library", "font");

        if (!Directory.Exists(sourceFontDir))
        {
            Log.Warning($"  Warning: Font directory not found at {sourceFontDir}");
            return;
        }

        var fontFiles = Directory.GetFiles(sourceFontDir, "segui.*");
        if (fontFiles.Length == 0)
        {
            Log.Warning("  Warning: segui font files not found in editor directory");
            return;
        }

        foreach (var fontFile in fontFiles)
        {
            var fileName = Path.GetFileName(fontFile);
            var destFile = Path.Combine(destFontDir, fileName);
            File.Copy(fontFile, destFile, overwrite: true);
            Log.Info($"  Copied font: {fileName}");
        }
    }

    private static bool IsValidProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private class TemplateContext
    {
        public string ProjectName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string GameAssemblyName { get; set; } = "";
        public string DesktopAssemblyName { get; set; } = "";
        public string WebAssemblyName { get; set; } = "";
        public Dictionary<string, Guid> Guids { get; set; } = new();
    }
}
