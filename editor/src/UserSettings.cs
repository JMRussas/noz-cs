//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class UserSettings
{
    private static string UserConfigPath => Path.Combine(EditorApplication.ProjectPath, ".noz", "user.cfg");

    public static void Load()
    {
        var props = PropertySet.LoadFile(UserConfigPath);
        if (props == null)
            return;

        CollectionManager.LoadUserSettings(props);
        Workspace.LoadUserSettings(props);
    }

    public static void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(UserConfigPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var props = new PropertySet();
        Workspace.SaveUserSettings(props);
        CollectionManager.SaveUserSettings(props);
        props.Save(UserConfigPath);
    }
}
