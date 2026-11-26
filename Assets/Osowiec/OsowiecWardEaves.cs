using UnityEngine;
using UnityEngine.Rendering;

// Six sewn low points drain the rain fly. They follow the actual baked edge,
// including when the renderer is cut away above the selected patient room.
[RequireComponent(typeof(MeshRenderer))]
public sealed class OsowiecWardEaves : MonoBehaviour
{
    public Vector3[] outlets;
    public Material droplet;
    MeshRenderer cloth;
    ParticleSystem drips;
    ParticleSystemRenderer streaks;
    float[] nextDrop;
    int sequence;

    void Start()
    {
        cloth=GetComponent<MeshRenderer>();
        if(!droplet || outlets==null || outlets.Length==0){enabled=false;return;}
        var host=new GameObject("Water falling from the canvas eaves");
        host.transform.SetParent(transform,false);
        drips=host.AddComponent<ParticleSystem>();
        drips.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=drips.main;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.scalingMode=ParticleSystemScalingMode.Shape;
        main.gravityModifier=.82f;
        main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles=64;
        main.startSize=.018f;
        main.startLifetime=.63f;
        main.startColor=new Color(.79f,.86f,.86f,.62f);
        var emission=drips.emission;emission.enabled=false;
        var shape=drips.shape;shape.enabled=false;
        streaks=host.GetComponent<ParticleSystemRenderer>();
        streaks.sharedMaterial=droplet;
        streaks.renderMode=ParticleSystemRenderMode.Stretch;
        streaks.lengthScale=1.15f;
        streaks.velocityScale=.025f;
        streaks.shadowCastingMode=ShadowCastingMode.Off;
        streaks.receiveShadows=false;
        nextDrop=new float[outlets.Length];
        for(int i=0;i<nextDrop.Length;i++)nextDrop[i]=Time.time+.13f*i;
        drips.Play();
    }

    void LateUpdate()
    {
        if(!drips)return;
        var view=CameraManager.Instance;
        var camera=view ? (view.IsActionMode() ? view.GetActionCamera() : view.GetTacticalCamera()) : null;
        if(!camera || !camera.isActiveAndEnabled)camera=Camera.main;
        // The planning camera rises to 70 m after deployment while the ward is still in view.
        // Its height is unrelated to how far the soldier is from the cloth in first person.
        bool tactical=view && camera==view.GetTacticalCamera();
        bool visible=cloth.enabled && camera && (tactical || cloth.bounds.SqrDistance(camera.transform.position)<70f*70f);
        if(!visible)
        {
            if(streaks.enabled){ streaks.enabled=false; drips.Clear(); }
            return;
        }
        streaks.enabled=true;
        if(Time.deltaTime<=0)return;
        for(int i=0;i<outlets.Length;i++)
        {
            if(Time.time<nextDrop[i])continue;
            float irregular=Mathf.PerlinNoise(i*13.71f,sequence++*.712f);
            nextDrop[i]=Time.time+(.09f+.25f*irregular)*(OsowiecFieldSettings.ReducedEffects?2f:1f);
            var drop=new ParticleSystem.EmitParams
            {
                position=transform.TransformPoint(outlets[i])+new Vector3((irregular-.5f)*.05f,0,0),
                velocity=new Vector3(.09f,-.58f-.12f*irregular,-.04f),
                startSize=.014f+.012f*irregular
            };
            drips.Emit(drop,1);
        }
    }
}
