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
        public Sprite? Icon;
    }

    private static readonly Notification[] _notifications = new Notification[MaxNotifications];
    private static readonly ConcurrentQueue<(NotificationType Type, string Text, Sprite? Icon)> _pending = new();
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

    public static void Add(NotificationType type, string text, Sprite? icon = null)
    {
        if (_count == MaxNotifications)
            PopFront();

        var index = (_head + _count) % MaxNotifications;
        _notifications[index] = new Notification
        {
            Text = text,
            Elapsed = 0.0f,
            Type = type,
            Icon = icon
        };
        _count++;
    }

    public static void Add(string text, Sprite? icon = null) => Add(NotificationType.Info, text, icon);
    public static void AddError(string text, Sprite? icon = null) => Add(NotificationType.Error, text, icon);

    public static void AddDeferred(NotificationType type, string text, Sprite? icon = null)
    {
        _pending.Enqueue((type, text, icon));
    }

    public static void Update()
    {
        while (_pending.TryDequeue(out var item))
            Add(item.Type, item.Text, item.Icon);

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

        using (UI.BeginColumn(EditorStyle.Notifications.Root))
        {
            for (var i = 0; i < _count; i++)
            {
                var index = (_head + i) % MaxNotifications;
                ref var n = ref _notifications[index];                
                using (UI.BeginContainer(EditorStyle.Notifications.Notification))
                {
                    if (n.Icon != null)
                        UI.Image(n.Icon, EditorStyle.Notifications.NotificationIcon);
                    UI.Label(
                        n.Text,
                        n.Type == NotificationType.Error
                            ? EditorStyle.Notifications.NotificationErrorText
                            : EditorStyle.Notifications.NotificationText);
                }
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
