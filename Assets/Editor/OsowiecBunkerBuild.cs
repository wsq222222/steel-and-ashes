using System;
using System.IO;
using UnityEditor;

public static class OsowiecBunkerBuild
{
    public static void GenerateScenes()
    {
        OsowiecHeadquartersBuilder.Generate();
        OsowiecBunkerLocalisation.BuildTables();
        AssetDatabase.SaveAssets();
    }

    public static void Windows()
    {
        OsowiecBunkerLocalisation.FinaliseMenuArtwork();
        BuildPlayer();
    }

    static void BuildPlayer()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-bunker-output");
        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Pass -bunker-output followed by the Windows executable path.");
        OsowiecOperationBuilder.BuildWindows(Path.GetFullPath(args[index + 1]));
    }

    public static void BuildForPhotography()
    {
        GenerateScenes();
        BuildPlayer();
    }

    public static void BuildWithPhotograph()
    {
        OsowiecBunkerMenuPhoto.Capture();
        Windows();
    }
}
