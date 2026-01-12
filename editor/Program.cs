//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz;

Application.Init(new ApplicationConfig
{
    Title = "Noz Editor",
    Width = 1600,
    Height = 900,
    
    Render = new RenderConfig
    {
        MaxCommands = 2048
    },
    
    Vtable = new ApplicationVtable
    {
        Update = Editor.Update
    }
});

Editor.Init();

Application.Run();
Application.Shutdown();
