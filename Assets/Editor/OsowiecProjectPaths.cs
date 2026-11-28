using System;
using UnityEditor;

internal static class OsowiecProjectPaths
{
    private const string Root = "Assets/Osowiec";
    internal const string SceneFolder = Root + "/Scenes";
    internal const string NavigationFolder = Root + "/Navigation";
    internal const string PrefabFolder = Root + "/Prefabs";
    internal const string HeadquartersScene = SceneFolder + "/OsowiecHQ.unity";
    internal const string OperationScene = SceneFolder + "/Osowiec.unity";
    internal const string OperationNavMesh = NavigationFolder + "/OsowiecNavMesh.asset";

    internal static string Prefab(string name) => PrefabFolder + "/" + name + ".prefab";

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string guid = AssetDatabase.CreateFolder(path.Substring(0, separator), path.Substring(separator + 1));
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException("Could not create Unity asset folder " + path);
    }
}
