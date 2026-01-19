//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static class Log
{
    public static void Info(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
    }

    public static void Debug(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
    }

    public static void Warning(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[WARNING] {message}");
    }

    public static void Error(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
    }


    public static string Params((string name, object? value, bool condition)[]? values)
    {
        if (values == null)
            return string.Empty;

        var stringBuilder = new System.Text.StringBuilder(1024);
        foreach (var (name, value, condition) in values)
            if (condition)
                stringBuilder.Append($"  {name}={value}");  

        return stringBuilder.ToString();
    }

    public static string Param(string name, object? value, bool condition=true) =>
        condition ? $"  {name}={value}" : "";
}
