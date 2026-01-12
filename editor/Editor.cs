//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

internal static class Editor
{
    private static InputSet? _input;
    
    internal static void Init()
    {
        _input = new InputSet();
        _input.EnableButton(InputCode.KeySpace);
        
        Input.PushInputSet(_input);
    }

    internal static void Shutdown()
    {
        _input = null;
    }

    internal static void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeySpace))
            Console.WriteLine("hi!");
    }
} 