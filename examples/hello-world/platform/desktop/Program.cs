//
//  NoZ Hello World Example
//

using NoZ;
using NoZ.Platform;
using NoZ.Platform.WebGPU;

using HelloWorld;

Application.Init(new ApplicationConfig
{
    Title = "Hello World",
    Platform = new SDLPlatform(),
    AudioBackend = new SDLAudioDriver(),
    Vtable = new HelloWorldApp(),
    ResourceAssembly = typeof(HelloWorldApp).Assembly,
    UI = new UIConfig
    {
        DefaultFont = "inter",
        ScaleMode = UIScaleMode.ConstantPixelSize,
        ReferenceResolution = new(1920, 1080),
        ScreenMatchMode = ScreenMatchMode.MatchWidthOrHeight,
        MatchWidthOrHeight = 0.5f
    },
    Graphics = new GraphicsConfig
    {
        Driver = new WebGPUGraphicsDriver()
    }
});

Application.Run();
Application.Shutdown();
