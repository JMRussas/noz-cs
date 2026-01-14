//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace noz;

public static class Time
{
    private static long _startTicks;
    private static long _lastFrameTicks;
    private static float _timeScale = 1f;
    public static float DeltaTime { get; private set; }
    public static float UnscaledDeltaTime { get; private set; }
    public static float TotalTime { get; private set; }
    public static int FrameCount { get; private set; }
    
    public static float TimeScale
    {
        get => _timeScale;
        set => _timeScale = MathF.Max(0, value);
    }

    public static float Fps => UnscaledDeltaTime > 0 ? 1f / UnscaledDeltaTime : 0;

    internal static void Init()
    {
        _startTicks = Stopwatch.GetTimestamp();
        _lastFrameTicks = _startTicks;
        DeltaTime = 0;
        UnscaledDeltaTime = 0;
        TotalTime = 0;
        FrameCount = 0;
    }

    internal static void Update()
    {
        var currentTicks = Stopwatch.GetTimestamp();
        UnscaledDeltaTime = (float)(currentTicks - _lastFrameTicks) / Stopwatch.Frequency;
        _lastFrameTicks = currentTicks;

        if (UnscaledDeltaTime > 0.1f)
            UnscaledDeltaTime = 0.1f;

        DeltaTime = UnscaledDeltaTime * _timeScale;
        TotalTime += DeltaTime;
        FrameCount++;
    }

    internal static void Shutdown()
    {
    }
}
