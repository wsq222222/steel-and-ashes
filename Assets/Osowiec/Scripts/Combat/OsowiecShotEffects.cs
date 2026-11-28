using UnityEngine;

// Independent emitters share one soft-edged particle atlas for the life of this scene.
public sealed class OsowiecShotEffects : MonoBehaviour
{
    static OsowiecShotEffects live;
    ParticleSystem flash, smoke, flecks, mist;
    Texture2D texture;
    Material material;
    public Material softParticles;
    Light glint;
    float endLight;
    readonly LineRenderer[] rings = new LineRenderer[6];
    readonly float[] rippleStart = new float[6];
    readonly float[] rippleLimit = new float[6];
    int nextRipple;

    void Awake()
    {
        live=this;
        texture=new Texture2D(48,48,TextureFormat.RGBA32,false);
        texture.wrapMode=TextureWrapMode.Clamp;
        for(int y=0;y<48;y++) for(int x=0;x<48;x++)
        {
            var p=new Vector2((x-23.5f)/23.5f,(y-23.5f)/23.5f);
            float edge=Mathf.Clamp01(1-p.sqrMagnitude);
            texture.SetPixel(x,y,new Color(1,1,1,edge*edge*(.7f+.3f*Mathf.PerlinNoise(x*.17f,y*.17f))));
        }
        texture.Apply(false,true);
        material=new Material(softParticles);
        material.SetTexture("_BaseMap",texture);
        material.SetFloat("_Surface",1);
        material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite",0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue=3000;
        flash=Emitter("Muzzle core",0);
        smoke=Emitter("Drifting powder",-.05f);
        flecks=Emitter("Impact fragments",.75f);
        mist=Emitter("Surface spray",.08f);
        glint=new GameObject("Muzzle bounce").AddComponent<Light>();
        glint.transform.SetParent(transform,false);
        glint.type=LightType.Point;
        glint.range=2.6f;
        glint.color=new Color(1,.59f,.24f);
        glint.enabled=false;
        for (int i=0; i<rings.Length; i++)
        {
            var ring=new GameObject("Water contact ring").AddComponent<LineRenderer>();
            ring.transform.SetParent(transform,false);
            ring.sharedMaterial=material;
            ring.useWorldSpace=false;
            ring.loop=true;
            ring.positionCount=32;
            ring.widthMultiplier=.013f;
            ring.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int j=0; j<ring.positionCount; j++)
            {
                float a=2*Mathf.PI*j/ring.positionCount;
                ring.SetPosition(j,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));
            }
            ring.enabled=false;
            rings[i]=ring;
        }
    }

    ParticleSystem Emitter(string label,float gravity)
    {
        var obj=new GameObject(label);
        obj.transform.SetParent(transform,false);
        var ps=obj.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.playOnAwake=false;main.maxParticles=180;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.gravityModifier=gravity;
        var emission=ps.emission;emission.enabled=false;
        var shape=ps.shape;shape.enabled=false;
        var fade=ps.colorOverLifetime;fade.enabled=true;
        var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                         new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.72f,.32f),new GradientAlphaKey(0,1)});
        fade.color=gradient;
        var sr=ps.GetComponent<ParticleSystemRenderer>();sr.sharedMaterial=material;
        sr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }
    static void Puff(ParticleSystem emitter,Vector3 p,Vector3 velocity,Color color,float size,float time)
    {
        var shot=new ParticleSystem.EmitParams{position=p,velocity=velocity,startColor=color,startSize=size,startLifetime=time};
        emitter.Emit(shot,1);
    }
    public static bool AtMuzzle(Vector3 p,Vector3 dir)
    {
        if(!live) return false;
        dir=dir.normalized;
        for(int i=0;i<6;i++)
            Puff(live.flash,p+dir*i*.024f,dir*.8f+Random.insideUnitSphere*.14f,
                Color.Lerp(new Color(1,.93f,.57f),new Color(1,.40f,.07f),i/5f),
                (.085f-i*.011f)*Random.Range(.86f,1.15f),.050f+i*.004f);
        for(int i=0;i<4;i++)
            Puff(live.smoke,p+dir*.08f,dir*.45f+Vector3.up*.23f+Random.insideUnitSphere*.24f,
                new Color(.53f,.50f,.43f,.40f),Random.Range(.07f,.13f),Random.Range(.32f,.58f));
        live.glint.transform.position=p;live.glint.intensity=1.1f;live.glint.enabled=true;live.endLight=Time.time+.065f;
        return true;
    }
    public static bool AtSurface(RaycastHit hit)
    {
        if(!live || !hit.collider) return false;
        if(hit.collider.GetComponentInParent<Unit>()) return true;
        Vector3 p=hit.point;
        var tag=hit.collider.GetComponentInParent<OsowiecAcousticSurface>();
        string name=hit.collider.name;
        bool water=OsowiecGroundEvents.TryWaterAt(p,out var level);
        bool wood=tag && tag.surface==OsowiecGroundSurface.Timber || name.Contains("Timber") || name.Contains("Crate");
        bool brick=tag && tag.surface==OsowiecGroundSurface.Brick || name.Contains("asemate") || name.Contains("rick");
        bool metal=name.Contains("Battery_statuary") || name.Contains("Doorleaf") || name.Contains("ron");
        Color colour=water?new Color(.47f,.49f,.41f,.8f):wood?new Color(.46f,.31f,.15f):
            brick?new Color(.44f,.24f,.16f):metal?new Color(1,.75f,.27f):new Color(.23f,.18f,.12f);
        if(water)
        {
            p=level;
            live.StartRipple(level);
        }
        var normal=water?Vector3.up:hit.normal;
        for(int i=0;i<12;i++)
        {
            var v=Random.onUnitSphere;
            if(Vector3.Dot(v,normal)<0) v=-v;
            Puff(live.flecks,p+normal*.015f,(normal*.65f+v)*Random.Range(.6f,2.2f),colour,
                Random.Range(.012f,.038f),Random.Range(.15f,.42f));
        }
        Puff(live.mist,p+normal*.045f,normal*.4f, new Color(colour.r,colour.g,colour.b,.54f),
            water?.25f:.21f,water?.23f:.51f);
        return true;
    }
    public static void WalkingRipple(Vector3 point)
    {
        if (live && live.rings[0]) live.StartRipple(point);
    }

    void StartRipple(Vector3 point)
    {
        // Constrain the outer edge to the authored shoreline.
        float limit=.48f;
        for(int i=0;i<8;i++)
        {
            float a=Mathf.PI*i*.25f;
            var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            while(limit>.075f && !OsowiecGroundEvents.TryWaterAt(point+radial*limit,out _))
                limit-=.05f;
        }
        if(limit<.08f) return;
        int slot=nextRipple;
        nextRipple=(slot+1)%rings.Length;
        rippleStart[slot]=Time.time;
        rippleLimit[slot]=limit;
        var ring=rings[slot];
        ring.transform.position=point+Vector3.up*.009f;
        ring.transform.localScale=Vector3.one*.032f;
        ring.enabled=true;
    }

    void Update()
    {
        if(glint.enabled && Time.time>=endLight) glint.enabled=false;
        for(int i=0;i<rings.Length;i++)
        {
            var ring=rings[i];
            if(!ring.enabled) continue;
            float t=Mathf.Clamp01((Time.time-rippleStart[i])/.72f);
            ring.transform.localScale=Vector3.one*Mathf.Lerp(.032f,rippleLimit[i],t*(2-t));
            var colour=new Color(.72f,.72f,.63f,.58f*(1-t));
            ring.startColor=colour;
            ring.endColor=colour;
            if(t>=1) ring.enabled=false;
        }
    }
    void OnDestroy(){if(live==this) live=null;Destroy(material);Destroy(texture);}
}
