//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ;
using NoZ.Platform;
using NoZ.Editor;

string? projectPath = null;
var clean = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length)
        projectPath = args[++i];
    else if (args[i] == "--clean")
        clean = true;
}

EditorApplication.Init(projectPath, clean);

Application.Init(new ApplicationConfig
{
    Title = "NoZ Editor",
    Width = 1600,
    Height = 900,
    IconPath = "res/windows/nozed.png",
    Platform = new SDLPlatform(),
    AudioBackend = new SDLAudioDriver(),
    Vtable = new EditorVtable(),

    UI = new UIConfig()
    {
        DefaultFont = EditorAssets.Names.Seguisb
    },
    Graphics = new GraphicsConfig
    {
        //Driver = new DirectX12RenderDriver(),
        Driver = new OpenGLGraphicsDriver(),
        CompositeShader = "composite"
    }
});

Application.Run();

EditorApplication.Shutdown();
Application.Shutdown();
