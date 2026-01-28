//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class CollectionDef(string id, string name, int index)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public int Index { get; } = index;
    public Vector2 CameraPosition { get; set; }
    public float CameraZoom { get; set; } = 1f;
}

public static class CollectionManager
{
    private static readonly List<CollectionDef> _collections = [];
    private static readonly Dictionary<string, CollectionDef> _byId = [];
    private static readonly Dictionary<int, CollectionDef> _byIndex = [];
    private static readonly HashSet<Type> _alwaysVisibleTypes = [];
    private static string _defaultId = "";
    private static int _visibleIndex = 1;

    public static IReadOnlyList<CollectionDef> Collections => _collections;
    public static int VisibleIndex => _visibleIndex;
    public static CollectionDef? VisibleCollection => GetByIndex(_visibleIndex);

    public static void Init(EditorConfig config)
    {
        _collections.Clear();
        _byId.Clear();
        _byIndex.Clear();
        _alwaysVisibleTypes.Clear();
        _visibleIndex = 1;

        var index = 1;
        foreach (var id in config.GetCollectionIds())
        {
            var name = config.GetCollectionName(id);
            var def = new CollectionDef(id, name, index);
            _collections.Add(def);
            _byId[id.ToLowerInvariant()] = def;
            _byIndex[index] = def;
            index++;
        }

        _defaultId = _collections.Count > 0 ? _collections[0].Id : "";
    }

    public static void Shutdown()
    {
        _collections.Clear();
        _byId.Clear();
        _byIndex.Clear();
        _alwaysVisibleTypes.Clear();
        _visibleIndex = 1;
    }

    public static CollectionDef? GetById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        return _byId.TryGetValue(id.ToLowerInvariant(), out var def) ? def : null;
    }

    public static CollectionDef? GetByIndex(int index)
    {
        return _byIndex.TryGetValue(index, out var def) ? def : null;
    }

    public static string GetIdOrDefault(string id)
    {
        if (!string.IsNullOrEmpty(id) && _byId.ContainsKey(id.ToLowerInvariant()))
            return id;
        return _defaultId;
    }

    public static void SetVisible(int index)
    {
        if (!_byIndex.ContainsKey(index))
            return;
        _visibleIndex = index;
    }

    public static bool IsVisible(int index) => _visibleIndex == index;

    public static string GetVisibleId()
    {
        return _byIndex.TryGetValue(_visibleIndex, out var def) ? def.Id : _defaultId;
    }

    public static bool IsDocumentVisible(Document doc)
    {
        if (_alwaysVisibleTypes.Contains(doc.GetType()))
            return true;
        var docCollection = GetById(doc.CollectionId);
        return docCollection != null && docCollection.Index == _visibleIndex;
    }

    public static void ShowAlways(Type type, bool value)
    {
        if (value)
            _alwaysVisibleTypes.Add(type);
        else
            _alwaysVisibleTypes.Remove(type);
    }

    public static void LoadUserSettings(PropertySet props)
    {
        // Load visible collection
        var visibleId = props.GetString("collection", "visible", "");
        if (!string.IsNullOrEmpty(visibleId))
        {
            var def = GetById(visibleId);
            if (def != null)
                _visibleIndex = def.Index;
        }

        // Load per-collection camera position and zoom
        foreach (var collection in _collections)
        {
            var section = $"collection.{collection.Id}";
            collection.CameraPosition = props.GetVector2(section, "camera_position", Vector2.Zero);
            collection.CameraZoom = props.GetFloat(section, "camera_zoom", 1f);
        }
    }

    public static void SaveUserSettings(PropertySet props)
    {
        // Save visible collection
        if (_byIndex.TryGetValue(_visibleIndex, out var visible))
            props.SetString("collection", "visible", visible.Id);

        // Save per-collection camera position and zoom
        foreach (var collection in _collections)
        {
            var section = $"collection.{collection.Id}";
            props.SetVec2(section, "camera_position", collection.CameraPosition);
            props.SetFloat(section, "camera_zoom", collection.CameraZoom);
        }
    }
}
