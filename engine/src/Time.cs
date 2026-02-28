//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ;

public static class Time
{
    private static long _startTicks;
    private static long _lastFrameTicks;
    private static float _timeScale = 1f;
    private static float _fixedAccumulator;

    public static float DeltaTime { get; set; }
    public static float UnscaledDeltaTime { get; private set; }
    public static float FixedDeltaTime { get; set; } = 1f / 60f;
    public static float TotalTime { get; private set; }
    public static int FrameCount { get; private set; }

    public static float TimeScale
    {
        get => _timeScale;
        set => _timeScale = MathF.Max(0, value);
    }

    public static float Fps => UnscaledDeltaTime > 0 ? 1f / UnscaledDeltaTime : 0;

    public static float AvergeFps { get; private set; }

    internal static void Init()
    {
        _startTicks = Stopwatch.GetTimestamp();
        _lastFrameTicks = _startTicks;
        DeltaTime = 0;
        UnscaledDeltaTime = 0;
        TotalTime = 0;
        FrameCount = 0;
        _fixedAccumulator = 0;
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

        _fixedAccumulator += DeltaTime;

        if (AvergeFps > 0)
            AvergeFps = AvergeFps * 0.95f + Fps * 0.05f;
        else
            AvergeFps = Fps;
    }

    internal static bool ConsumeFixedStep()
    {
        if (_fixedAccumulator < FixedDeltaTime)
            return false;

        _fixedAccumulator -= FixedDeltaTime;
        return true;
    }

    internal static void Shutdown()
    {
    }
}
