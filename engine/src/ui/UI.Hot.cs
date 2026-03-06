//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    private static int _hotId;
    private static int _prevHotId;
    private static int _hotOriginalHash;
    private static int _hotCurrentHash;
    private static int _lastElementId;
    private static bool _valueChanged;
    private static bool _hotClickedThisFrame;

    public static void SetHot<T>(int elementId, T originalValue) where T : notnull
        => SetHot(elementId, originalValue.GetHashCode());

    public static void SetHot(int elementId, ReadOnlySpan<char> originalValue)
        => SetHot(elementId, string.GetHashCode(originalValue));

    public static void SetHot(int elementId, int originalHash)
    {
        if (_prevHotId != elementId && _hotId != elementId)
        {
            _hotOriginalHash = originalHash;
            _hotCurrentHash = originalHash;
        }

        _hotId = elementId;
    }

    public static void SetHot(int elementId)
    {
        _hotId = elementId;
        ref var es = ref GetElementState(elementId);
        es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);
    }

    public static void ClearHot()
    {
        if (_hotId != 0)
        {
            ref var es = ref GetElementState(_hotId);
            es.SetFlags(ElementFlags.Focus, ElementFlags.None);
        }
        _hotId = 0;
    }

    public static bool HasHot() => _hotId != 0;
    internal static int HotId => _hotId;

    public static void NotifyChanged(int currentHash)
    {
        _valueChanged = true;
        _hotCurrentHash = currentHash;
    }

    public static void SetLastElement(int elementId)
    {
        _lastElementId = elementId;
    }

    public static bool IsHot() => _hotId != 0 && _hotId == _lastElementId;
    public static bool WasHot() => _prevHotId != 0 && _prevHotId == _lastElementId;
    public static bool WasChanged() => _valueChanged && _hotId == _lastElementId;

    public static bool IsChanged() => _hotId == _lastElementId
        ? _hotCurrentHash != _hotOriginalHash
        : _prevHotId == _lastElementId && _hotCurrentHash != _hotOriginalHash;

    public static bool HotEnter() => IsHot() && !WasHot();
    public static bool HotExit() => !IsHot() && WasHot();
}
