//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static class Log
{
    private static readonly object _lock = new();
    private static bool _initialized;

    public static string? Path { get; set; }

    private static void EnsureInitialized()
    {
        if (_initialized || Path == null)
            return;

        lock (_lock)
        {
            if (_initialized || Path == null)
                return;

            File.WriteAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log started\n");
            _initialized = true;
        }
    }

    private static void WriteToFile(string message)
    {
        EnsureInitialized();
        lock (_lock)
        {
            File.AppendAllText(Path!, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
    }

    private static void Write(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        if (Path != null)
            WriteToFile(message);
    }

    public static void Info(string message) => Write($"[INFO] {message}");

    public static void Debug(string message) => Write($"[DEBUG] {message}");

    public static void Warning(string message) => Write($"[WARNING] {message}");

    public static void Error(string message) => Write($"[ERROR] {message}");


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
