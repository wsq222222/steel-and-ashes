using System;
using UnityEngine;

/// Foot contacts share stride timing across camera modes; sound originates at the planted shoe.
[DisallowMultipleComponent]
public sealed class OsowiecBootContact : MonoBehaviour
{
    static readonly bool Trace = Array.IndexOf(Environment.GetCommandLineArgs(), "-osowiec-audio-trace") >= 0;
    readonly RaycastHit[] hits = new RaycastHit[12];
    Unit actor;
    Animator body;
    Transform left, right;
    AudioSource boots, kit;
    Vector3 previous;
    float walked, nextContact;
    int lastHalf = -1, contacts;
    bool lastWasLeft;
    public event Action<RaycastHit,bool> ImpressionPlanted;

    void Start()
    {
        actor = GetComponent<Unit>();
        body = GetComponent<OsowiecCastMotion>().body;
        foreach (var joint in body.GetComponentsInChildren<Transform>(true))
        {
            if (joint.name=="LFoot") left=joint;
            if (joint.name=="RFoot") right=joint;
        }
        boots=OsowiecSoundBank.Source(transform,"Boot contact","Foley",1,76);
        boots.rolloffMode=AudioRolloffMode.Linear;
        boots.minDistance=2f; boots.maxDistance=80f;
        kit=OsowiecSoundBank.Source(transform,"Field harness","Foley",1,116);
        kit.rolloffMode=AudioRolloffMode.Linear; kit.maxDistance=69f;
        previous=transform.position;
    }

    void LateUpdate()
    {
        var game=GameManager.Instance;
        var delta=transform.position-previous;
        previous=transform.position;
        delta.y=0;
        float length=delta.magnitude;
        if (game==null || game.IsPaused() || game.IsGameOver() || game.IsArmyDeploymentPhase() ||
            actor.GetHealth()<=0 || length>1.3f)
        {
            walked=0; lastHalf=-1; return;
        }
        if (length<=.000005f) return;
        walked+=length;
        var pose=body.GetCurrentAnimatorStateInfo(0);
        bool inWalk=pose.IsName("Base Layer.Walk") || pose.IsName("Base Layer.Run");
        int half=Mathf.FloorToInt(pose.normalizedTime*2f);
        float stride=actor.chessType==ChessUnitType.Hammer ? .73f : .82f;
        if (Time.time<nextContact) return;
        bool useLeft=(half&1)==0;
        bool newPlant=inWalk && half!=lastHalf && (contacts==0 || useLeft!=lastWasLeft);
        if (newPlant && walked<.28f) return;
        if (!newPlant)
        {
            // Hidden bodies can hold an animation frame while the capsule keeps moving.
            // Use distance after a missed plant and continue alternating the boots.
            float missedPlant=inWalk ? stride*1.5f : stride;
            if (walked<missedPlant) return;
            useLeft=contacts==0 || !lastWasLeft;
        }
        lastHalf=inWalk ? half : -1;
        walked=0;
        nextContact=Time.time+.22f;
        lastWasLeft=useLeft;
        var foot=useLeft ? left : right;
        Vector3 at=foot ? foot.position : previous;
        string family=Surface(at,out Vector3 contact,out var floorHit);
        if(OsowiecWetTracks.Boot(floorHit,transform.forward,useLeft))
            ImpressionPlanted?.Invoke(floorHit,useLeft);
        var clip=OsowiecSoundBank.Variant("Step"+family,8);
        float volume=OsowiecEnvironmentAudio.EffectsVolume;
        var camera=CameraManager.Instance;
        bool personal=camera!=null && camera.IsActionMode() && camera.GetCurrentControlledUnit()==actor;
        boots.transform.position=contact+Vector3.up*.09f;
        boots.spatialBlend=personal ? .23f : 1f;
        boots.pitch=UnityEngine.Random.Range(.97f,1.025f);
        boots.PlayOneShot(clip,volume*(personal ? .69f : .46f));
        if (++contacts%7==1)
        {
            string id=actor.chessType switch {
                ChessUnitType.Hammer=>"KitHarness", ChessUnitType.Hound=>"KitGaiter", _=>"KitOpticCase" };
            kit.transform.position=previous+Vector3.up*.84f;
            kit.PlayOneShot(OsowiecSoundBank.Clip(id),volume*.12f);
        }
        if (Trace) Debug.Log($"[Osowiec foley] t={Time.time:F2} unit={actor.name}/{actor.GetEntityId()} owner={actor.owner} shoe={foot?.name} surface={family} clip={clip.name} target={contact}");
    }

    string Surface(Vector3 foot,out Vector3 contact,out RaycastHit floorHit)
    {
        int count=Physics.RaycastNonAlloc(foot+Vector3.up*.46f,Vector3.down,hits,1.56f,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        contact=foot;
        floorHit=default;
        float best=float.MaxValue;
        OsowiecAcousticSurface surface=null;
        for (int i=0; i<count; i++)
        {
            var hit=hits[i];
            if (hit.normal.y<.4f || hit.distance>best || hit.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<Unit>() || hit.collider.GetComponentInParent<OsowiecCartMotion>()) continue;
            var candidate=hit.collider.GetComponentInParent<OsowiecAcousticSurface>();
            if (candidate==null && Mathf.Abs(hit.distance-best)<.001f && surface!=null) continue;
            best=hit.distance; surface=candidate; contact=hit.point; floorHit=hit;
        }
        if (surface!=null && surface.surface==OsowiecGroundSurface.Timber) return "Timber";
        if (surface!=null && surface.surface==OsowiecGroundSurface.Brick) return "Brick";
        if (OsowiecGroundEvents.TryWaterAt(contact,out var water)) { contact=water; return "Water"; }
        return "Mud";
    }
}
