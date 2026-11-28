using System;
using UnityEngine;

/// Sample-aligned arrangement. Activation changes gains while the phrase continues.
[DisallowMultipleComponent]
public sealed class OsowiecFieldScore : MonoBehaviour
{
    const double Bar=3*.51;
    readonly AudioSource[] voices=new AudioSource[4];
    readonly string[] parts={"FieldAir","FieldBows","FieldIron","FieldBrass"};
    static readonly bool Trace=Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-audio-trace")>=0;
    static readonly Vector4 March=new(.30f,.065f,0,0);
    static readonly Vector4 Deployment=new(.21f,.08f,0,.11f);
    static readonly Vector4 Alert=new(.17f,.30f,.27f,0);
    static readonly Vector4 Convoy=new(.17f,.23f,.10f,.24f);
    static readonly Vector4 Hold=new(.14f,.34f,.31f,.34f);
    readonly float[] fades=new float[4];
    double start, nextBar;
    float dangerUntil, warningUntil, arrivalUntil;
    int delivered;
    Vector4 target;

    void Start()
    {
        for(int i=0;i<voices.Length;i++)
        {
            var voice=OsowiecSoundBank.Source(transform,parts[i],"Music",0f,145);
            voice.clip=OsowiecSoundBank.Clip(parts[i]); voice.loop=true; voice.volume=0;
            voices[i]=voice;
        }
        start=AudioSettings.dspTime+.16;
        nextBar=start;
        foreach(var voice in voices) voice.PlayScheduled(start);
    }

    public void MarkContact() => dangerUntil=Time.time+6.5f;
    public void DuckWarning() { warningUntil=Time.time+2.2f; dangerUntil=Time.time+8f; }

    void Update()
    {
        var game=GameManager.Instance;
        var operation=OsowiecOperation.Instance;
        if(game==null || operation==null || voices[0]==null) return;
        var cameras=CameraManager.Instance;
        var unit=cameras!=null ? cameras.GetCurrentControlledUnit() : null;
        if(unit!=null && operation.Pressure(unit)>.40f) dangerUntil=Time.time+6.5f;
        var column=operation.relief;
        int carts=column!=null ? column.Delivered : 0;
        if(carts>delivered) arrivalUntil=Time.time+4f;
        delivered=carts;
        if(AudioSettings.dspTime>=nextBar)
        {
            nextBar=start+(Math.Floor((AudioSettings.dspTime-start)/Bar)+1)*Bar;
            Vector4 next=game.IsArmyDeploymentPhase() ? Deployment :
                carts>=3 ? Hold : column!=null && column.Started ? Convoy : Time.time<dangerUntil ? Alert : March;
            if(column!=null && column.Started && carts<3)
            {
                next.y+=carts*.035f;
                next.z+=carts*.025f;
                if(Time.time<arrivalUntil) { next.w+=.09f; next.z*=.5f; }
            }
            if(Trace && (next-target).sqrMagnitude>.00001f)
                Debug.Log($"[Osowiec score] t={Time.time:F2} mix={next} carts={carts} phase={AudioSettings.dspTime-start:F2}");
            target=next;
        }
        bool finished=game.IsGameOver();
        float user=AudioManager.Instance!=null ? AudioManager.Instance.MusicVolume : PlayerPrefs.GetFloat("MusicVolume",1);
        float duck=Time.time<warningUntil ? .18f : game.IsPaused() ? .40f : 1f;
        float smooth=1-Mathf.Exp(-Time.unscaledDeltaTime*(finished||duck<.2f ? 8f : 1.7f));
        for(int i=0;i<voices.Length;i++)
        {
            fades[i]=Mathf.Lerp(fades[i],finished ? 0 : target[i]*duck,smooth);
            voices[i].volume=fades[i]*user;
        }
    }
}
