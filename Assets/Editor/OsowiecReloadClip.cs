using UnityEditor;
using UnityEngine;

// Curves are authored in camera metres and sampled at the actual weapon's reload phase.
public static class OsowiecReloadClip
{
    public static AnimationClip Build()
    {
        const string path = "Assets/Osowiec/Art/Cast/Game/CarbineReload-FPS.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!clip)
        {
            clip = new AnimationClip { name = "CarbineReload-FPS", frameRate = 60 };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        float[] t = { 0, .14f, .28f, .43f, .54f, .68f, .77f, .88f, 1f };
        void Track(string part, string property, float[] values)
        {
            var keys = new Keyframe[t.Length];
            for (int i=0;i<t.Length;i++) keys[i] = new Keyframe(t[i], values[i]);
            var curve = new AnimationCurve(keys);
            for (int i=0;i<t.Length;i++) curve.SmoothTangents(i,0);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(part,typeof(Transform),property),curve);
        }
        Track("Presentation","localEulerAnglesRaw.z",new[]{0f,-7,-24,-27,-26,-22,-15,-8,0});
        Track("Presentation","localEulerAnglesRaw.x",new[]{0f,-8,-12,-12,-9,-7,-6,-3,0});
        Track("Presentation","m_LocalPosition.x",new[]{0f,-.04f,-.10f,-.12f,-.12f,-.10f,-.08f,-.04f,0});
        Track("Presentation","m_LocalPosition.y",new[]{0f,.018f,.035f,.032f,.030f,.038f,.033f,.024f,0});
        Track("Presentation/Support","m_LocalPosition.z",new[]{0f,-.08f,-.249f,-.262f,-.256f,-.247f,-.244f,-.137f,0});
        Track("Presentation/Support","m_LocalPosition.y",new[]{0f,-.018f,-.039f,-.130f,-.153f,-.069f,-.035f,-.023f,0});
        Track("Presentation/Support","m_LocalPosition.x",new[]{0f,-.016f,.002f,-.018f,-.025f,-.008f,.002f,-.021f,0});
        // The hand at the grip bears the weight while the other arm travels. Pivot about the grip to retain contact.
        Track("Presentation/Brace","localEulerAnglesRaw.z",new[]{0f,3,8,10,8,4,1,-2,0});
        Track("Presentation/Brace","localEulerAnglesRaw.x",new[]{0f,-2,-4,-5,-3,1,3,2,0});
        Track("Presentation/Brace","m_LocalPosition.y",new[]{0f,-.002f,-.006f,-.007f,-.006f,-.003f,.001f,.002f,0});
        Track("Presentation/Cassette","m_LocalPosition.y",new[]{0f,0,0,-.091f,-.115f,-.032f,0,0,0});
        Track("Presentation/Cassette","m_LocalPosition.x",new[]{0f,0,0,-.020f,-.027f,-.010f,0,0,0});
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
