//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Collections.Concurrent;

namespace NoZ.Editor;

public enum NotificationType
{
    Info,
    Error
}

public static class Notifications
{
    private const int MaxNotifications = 8;
    private const float NotificationDuration = 3.0f;
    
    private struct Notification
    {
        public string Text;
        public float Elapsed;
        public NotificationType Type;
    }

    private static readonly Notification[] _notifications = new Notification[MaxNotifications];
    private static readonly ConcurrentQueue<(NotificationType Type, string Text)> _pending = new();
    private static int _head;
    private static int _count;

    public static void Init()
    {
        _head = 0;
        _count = 0;

        Importer.OnImported += OnImported;
    }

    public static void Shutdown()
    {
        Importer.OnImported -= OnImported;
    }

    private static void OnImported(Document doc)
    {
        AddDeferred(NotificationType.Info, $"imported '{doc.Name}'");
    }

    public static void Add(NotificationType type, string text)
    {
        if (_count == MaxNotifications)
            PopFront();

        var index = (_head + _count) % MaxNotifications;
        _notifications[index] = new Notification
        {
            Text = text,
            Elapsed = 0.0f,
            Type = type
        };
        _count++;
    }

    public static void Add(string text) => Add(NotificationType.Info, text);
    public static void AddError(string text) => Add(NotificationType.Error, text);

    public static void AddDeferred(NotificationType type, string text)
    {
        _pending.Enqueue((type, text));
    }

    public static void Update()
    {
        while (_pending.TryDequeue(out var item))
            Add(item.Type, item.Text);

        if (_count <= 0)
            return;

        var dt = Time.DeltaTime;
        var removed = 0;

        for (var i = 0; i < _count; i++)
        {
            var index = (_head + i) % MaxNotifications;
            _notifications[index].Elapsed += dt;
            if (_notifications[index].Elapsed > NotificationDuration)
                removed++;
        }

        for (var i = 0; i < removed; i++)
            PopFront();
    }

    public static void UpdateUI()
    {
        if (_count <= 0)
            return;

        using (UI.BeginCanvas())
        using (UI.BeginColumn(EditorStyle.Notifications.Root))
        {
            for (var i = 0; i < _count; i++)
            {
                var index = (_head + i) % MaxNotifications;
                ref var n = ref _notifications[index];

                var color = n.Type == NotificationType.Error
                    ? EditorStyle.ErrorColor
                    : EditorStyle.Overlay.AccentTextColor;

                using (UI.BeginContainer(EditorStyle.Notifications.Notification))
                    UI.Label(n.Text, EditorStyle.Notifications.NotificationText with { Color = color });
            }
        }
    }

    private static void PopFront()
    {
        if (_count <= 0)
            return;

        _head = (_head + 1) % MaxNotifications;
        _count--;
    }
}
