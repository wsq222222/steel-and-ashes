using System;
using UnityEditor;
using UnityEngine;

/// A fast integration check for the actual imported clips, the state used by the mission, and near-miss geometry.
public static class OsowiecStressReview
{
    [MenuItem("Steel and Ashes/Verify mask and close impact")]
    public static void Verify()
    {
        var visor = new OsowiecVisorState();
        for (int i=0; i<1200; i++) visor.Step(true,true,false,1f/60f);
        if (visor.Fog < .95f) throw new InvalidOperationException("Moving in the respirator did not mist the glass");
        if (!visor.Step(true,true,true,.05f) || visor.WipeProgress <= 0 || visor.Step(true,true,true,.01f))
            throw new InvalidOperationException("The lens rub must begin exactly once");
        for (int i=0; i<26; i++) visor.Step(true,true,false,1f/60f);
        if (visor.Fog > .03f || visor.WipeProgress != 0 || visor.Step(false,false,true,.01f))
            throw new InvalidOperationException("Wiping or removing the mask left stale condensation");
        foreach (var cue in new[] { "MaskWipe", "PressurePulse" })
        {
            var clip=Resources.Load<AudioClip>("OsowiecSound/" + cue);
            if (!clip || clip.frequency != 48000 || clip.channels != 1 || clip.length < .35f)
                throw new InvalidOperationException("Incorrectly imported cue " + cue);
        }
        var source=Vector3.zero;
        var end=Vector3.forward*7;
        float along=OsowiecStressEffects.DistanceToPath(new Vector3(1,0,3),source,end,out var nearest);
        if (Mathf.Abs(along-1)>.001f || Vector3.Distance(nearest,new Vector3(0,0,3))>.001f ||
            OsowiecStressEffects.DistanceToPath(new Vector3(0,0,15),source,end,out _) < 7.99f)
            throw new InvalidOperationException("A distant impact behind cover was inside the near miss path");
        Debug.Log("PASS Osowiec visor / wipe / close-impact: two 48 kHz masters, 20 s travel, one wipe, finite segment.");
    }
}
