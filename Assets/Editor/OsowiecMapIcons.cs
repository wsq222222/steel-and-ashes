using UnityEditor;
using UnityEngine;

public static class OsowiecMapIcons
{
    public static void Apply()
    {
        var icons = Object.FindFirstObjectByType<TacticalWorldIconsController>(FindObjectsInactive.Include);
        var atlas = OsowiecFieldCast.Art<Texture2D>("FieldPortraitAtlas.png");
        if (icons == null || atlas == null) return;
        var serialized = new SerializedObject(icons);
        serialized.FindProperty("fieldPortraitAtlas").objectReferenceValue = atlas;
        serialized.FindProperty("worldOffsetY").floatValue = 1.3f;
        serialized.FindProperty("nearIconSizePixels").floatValue = 52f;
        serialized.FindProperty("farIconSizePixels").floatValue = 33f;
        serialized.FindProperty("nearOrthographicSize").floatValue = 23f;
        serialized.FindProperty("farOrthographicSize").floatValue = 52f;
        serialized.FindProperty("rangeRingColor").colorValue = new Color(.78f, .59f, .30f, .70f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(icons);
    }
}
