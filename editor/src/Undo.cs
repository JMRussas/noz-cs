//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class Undo
{
    private const int MaxUndo = 128;

    private class UndoItem
    {
        public Document Doc = null!;
        public Document Snapshot = null!;
        public int GroupId = -1;
    }

    private static readonly List<UndoItem> _undoStack = new(MaxUndo);
    private static readonly List<UndoItem> _redoStack = new(MaxUndo);
    private static readonly List<Document> _pendingCallbacks = new(MaxUndo);
    private static int _nextGroupId = 1;
    private static int _currentGroupId = -1;

    public static bool CanUndo => _undoStack.Count > 0;
    public static bool CanRedo => _redoStack.Count > 0;

    public static void BeginGroup()
    {
        _currentGroupId = _nextGroupId++;
    }

    public static void EndGroup()
    {
        _currentGroupId = -1;
    }

    public static void Record(Document doc)
    {
        if (_undoStack.Count >= MaxUndo)
            _undoStack.RemoveAt(0);

        var snapshot = CloneDocument(doc);
        _undoStack.Add(new UndoItem
        {
            Doc = doc,
            Snapshot = snapshot,
            GroupId = _currentGroupId
        });

        _redoStack.Clear();
    }

    public static bool DoUndo()
    {
        if (_undoStack.Count == 0)
            return false;

        var lastItem = _undoStack[^1];
        var groupId = lastItem.GroupId;

        while (_undoStack.Count > 0)
        {
            var item = _undoStack[^1];
            if (groupId != -1 && item.GroupId != groupId)
                break;

            var doc = item.Doc;
            var currentState = CloneDocument(doc);

            RestoreDocument(doc, item.Snapshot);

            if (_redoStack.Count >= MaxUndo)
                _redoStack.RemoveAt(0);

            _redoStack.Add(new UndoItem
            {
                Doc = doc,
                Snapshot = currentState,
                GroupId = item.GroupId
            });

            doc.MarkModified();
            _pendingCallbacks.Add(doc);

            var itemGroupId = item.GroupId;
            _undoStack.RemoveAt(_undoStack.Count - 1);

            if (itemGroupId == -1)
                break;
        }

        CallPendingCallbacks();
        return true;
    }

    public static bool DoRedo()
    {
        if (_redoStack.Count == 0)
            return false;

        var lastItem = _redoStack[^1];
        var groupId = lastItem.GroupId;

        while (_redoStack.Count > 0)
        {
            var item = _redoStack[^1];
            if (groupId != -1 && item.GroupId != groupId)
                break;

            var doc = item.Doc;
            var currentState = CloneDocument(doc);

            RestoreDocument(doc, item.Snapshot);

            if (_undoStack.Count >= MaxUndo)
                _undoStack.RemoveAt(0);

            _undoStack.Add(new UndoItem
            {
                Doc = doc,
                Snapshot = currentState,
                GroupId = item.GroupId
            });

            doc.MarkModified();
            _pendingCallbacks.Add(doc);

            var itemGroupId = item.GroupId;
            _redoStack.RemoveAt(_redoStack.Count - 1);

            if (itemGroupId == -1)
                break;
        }

        CallPendingCallbacks();
        return true;
    }

    public static void Cancel()
    {
        if (_undoStack.Count == 0)
            return;

        var lastItem = _undoStack[^1];
        var groupId = lastItem.GroupId;

        while (_undoStack.Count > 0)
        {
            var item = _undoStack[^1];
            if (groupId != -1 && item.GroupId != groupId)
                break;

            var doc = item.Doc;
            RestoreDocument(doc, item.Snapshot);

            doc.MarkModified();
            _pendingCallbacks.Add(doc);

            var itemGroupId = item.GroupId;
            _undoStack.RemoveAt(_undoStack.Count - 1);

            if (itemGroupId == -1)
                break;
        }

        CallPendingCallbacks();
    }

    public static void RemoveDocument(Document doc)
    {
        for (var i = _undoStack.Count - 1; i >= 0; i--)
        {
            if (_undoStack[i].Doc == doc)
                _undoStack.RemoveAt(i);
        }

        for (var i = _redoStack.Count - 1; i >= 0; i--)
        {
            if (_redoStack[i].Doc == doc)
                _redoStack.RemoveAt(i);
        }
    }

    public static void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pendingCallbacks.Clear();
        _currentGroupId = -1;
        _nextGroupId = 1;
    }

    private static void CallPendingCallbacks()
    {
        foreach (var doc in _pendingCallbacks)
            doc.OnUndoRedo();

        Workspace.ActiveEditor?.OnUndoRedo();
        _pendingCallbacks.Clear();
    }

    private static Document CloneDocument(Document source)
    {
        var clone = source.Def.Factory();
        clone.Def = source.Def;
        clone.Name = source.Name;
        clone.Path = source.Path;
        clone.SourcePathIndex = source.SourcePathIndex;
        clone.Position = source.Position;
        clone.SavedPosition = source.SavedPosition;
        clone.Bounds = source.Bounds;
        clone.Clone(source);
        return clone;
    }

    private static void RestoreDocument(Document target, Document snapshot)
    {
        var wasEditing = target.IsEditing;
        target.Position = snapshot.Position;
        target.SavedPosition = snapshot.SavedPosition;
        target.Bounds = snapshot.Bounds;
        target.Name = snapshot.Name;
        target.Path = snapshot.Path;
        target.SourcePathIndex = snapshot.SourcePathIndex;
        target.Clone(snapshot);
        target.IsEditing = wasEditing;
    }
}
