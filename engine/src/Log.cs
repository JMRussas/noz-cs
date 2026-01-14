//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public static class Log
{
    public static void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    public static void Debug(string message)
    {
        Console.WriteLine($"[DEBUG] {message}");
    }

    public static void Warning(string message)
    {
        Console.WriteLine($"[WARNING] {message}");
    }

    public static void Error(string message)
    {
        Console.WriteLine($"[ERROR] {message}");
    }
}
