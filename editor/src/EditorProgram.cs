//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz.Platform;

using noz;

string? projectPath = null;
var clean = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length)
        projectPath = args[++i];
    else if (args[i] == "--clean")
        clean = true;
}

Application.Init(new ApplicationConfig
{
    Title = "NoZ Editor",
    Width = 1600,
    Height = 900,
    IconPath = "res/windows/nozed.png",
    Platform = new SDLPlatform(),
    RenderBackend = new OpenGLRender(),
    AudioBackend = new SDLAudio(),
    Vtable = new EditorVtable(),

    Render = new RenderConfig()
});

Editor.Init(projectPath, clean);

Application.Run();

Editor.Shutdown();
Application.Shutdown();
