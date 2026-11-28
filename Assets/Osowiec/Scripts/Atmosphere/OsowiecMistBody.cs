using UnityEngine;
using UnityEngine.Rendering;

// One bounded draw per lowland. Camera depth clips the march at masonry and at the river bank.
public sealed class OsowiecMistBody : MonoBehaviour
{
    Material material;
    Texture3D noise;
    MeshRenderer body;
    bool temporary;
    static Texture3D sharedNoise;
    static int users;
    static Color32[] billows;
    static OsowiecMistBody lowland, causeway;

    // Match the render shader's trilinear billows, feathered edge and thinning upper surface.
    // This integral is used only by the player's sight, for rays ending on a potential silhouette.
    internal static float OpticalThickness(Vector3 from,Vector3 to)
    {
        return (lowland ? lowland.Integrate(from,to) : 0f)+(causeway ? causeway.Integrate(from,to) : 0f);
    }

    float Integrate(Vector3 from,Vector3 to)
    {
        if(!body || !body.enabled || billows==null)return 0;
        var bounds=body.bounds;
        Vector3 delta=to-from;
        float length=delta.magnitude;
        if(length<.001f)return 0;
        Vector3 dir=delta/length;
        float entry=0,exit=0;
        if(!bounds.Contains(from) && !bounds.IntersectRay(new Ray(from,dir),out entry))return 0;
        if(!bounds.Contains(to) && !bounds.IntersectRay(new Ray(to,-dir),out exit))return 0;
        float span=length-entry-exit;
        if(span<=0)return 0;
        int steps=Mathf.Clamp(Mathf.CeilToInt(span*2.4f),6,16);
        float stride=span/steps, density=0;
        var drift=new Vector3(-.0019f,-.0008f,.00055f)*(Time.timeSinceLevelLoad*material.GetFloat("_Motion"));
        var inv=new Vector3(1/bounds.extents.x,1/bounds.extents.y,1/bounds.extents.z);
        float amount=material.GetFloat("_MistAmount");
        for(int i=0;i<steps;i++)
        {
            var world=from+dir*(entry+(i+.5f)*stride);
            var local=Vector3.Scale(world-bounds.center,inv);
            var q=Vector3.Scale(world,new Vector3(.013f,.026f,.019f))+drift;
            float v=Sample(q)*.64f+Sample(q*2.07f+Vector3.one*.39f)*.26f+Sample(q*4.1f+Vector3.one*.21f)*.10f;
            float x=Mathf.Abs(local.x),z=Mathf.Abs(local.z);
            float radius=Mathf.Pow(x*x*x+z*z*z,1f/3);
            float h=Mathf.Clamp01(local.y*.5f+.5f);
            float edge=1-Smooth(.47f+v*.14f,.98f,radius+(v-.5f)*.13f);
            density+=Smooth(.27f,.69f,v)*edge*Smooth(0,.24f,.35f+v*.67f-h)*Smooth(0,.12f,h);
        }
        return density*amount*stride*.80f;
    }

    static float Smooth(float a,float b,float x)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));
    static float Sample(Vector3 p)
    {
        Vector3 grid=p*32f-Vector3.one*.5f;
        int x=Mathf.FloorToInt(grid.x),y=Mathf.FloorToInt(grid.y),z=Mathf.FloorToInt(grid.z);
        Vector3 t=grid-new Vector3(x,y,z);
        float Row(int j,int k)
        {
            int start=((k&31)*32+(j&31))*32;
            return Mathf.Lerp(billows[start+(x&31)].r,billows[start+((x+1)&31)].r,t.x);
        }
        return Mathf.Lerp(Mathf.Lerp(Row(y,z),Row(y+1,z),t.y),Mathf.Lerp(Row(y,z+1),Row(y+1,z+1),t.y),t.z)/255f;
    }

    internal static Bounds VisibleVolume(Bounds bounds, bool temporary)
    {
        if (temporary) return bounds;
        bounds.Expand(new Vector3(0, 1.75f, 0));
        bounds.center += Vector3.up * .7875f;
        return bounds;
    }

    public static OsowiecMistBody Install(Transform parent, Bounds bounds, bool temporary)
    {
        var host=GameObject.CreatePrimitive(PrimitiveType.Cube);
        host.name=temporary ? "Mist over the causeway" : "Suspended mist in the lowland";
        host.transform.SetParent(parent,false);
        host.GetComponent<Collider>().enabled=false;
        var mist=host.AddComponent<OsowiecMistBody>();
        var volume=VisibleVolume(bounds,temporary);
        Vector3 size=volume.size;
        Vector3 center=volume.center;
        host.transform.SetPositionAndRotation(center,Quaternion.identity);
        host.transform.localScale=size;
        mist.temporary=temporary;
        if(temporary)causeway=mist;else lowland=mist;
        mist.body=host.GetComponent<MeshRenderer>();
        mist.body.shadowCastingMode=ShadowCastingMode.Off;
        mist.body.receiveShadows=false;
        mist.material=new Material(Resources.Load<Shader>("BiebrzaMist"));
        if (!sharedNoise)
        {
            const int side=32;
            sharedNoise=new Texture3D(side,side,side,TextureFormat.RGBA32,false) {
                name="Wind eddies",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear
            };
            var rng=new System.Random(8153);
            var voxels=new Color32[side*side*side];
            for(int i=0;i<voxels.Length;i++)
            {
                byte density=(byte)rng.Next(256);
                voxels[i]=new Color32(density,density,density,255);
            }
            billows=voxels;
            sharedNoise.SetPixels32(voxels); sharedNoise.Apply(false,true);
        }
        users++;
        mist.noise=sharedNoise;
        mist.material.SetTexture("_Billows",mist.noise);
        mist.material.SetVector("_MistCenter",center);
        mist.material.SetVector("_MistSize",size);
        mist.body.sharedMaterial=mist.material;
        mist.Apply(null,false);
        return mist;
    }

    public void Apply(OsowiecReliefColumn column,bool reduced)
    {
        body.enabled=!temporary || (column && column.MistVisible);
        if(!body.enabled) return;
        var op=OsowiecOperation.Instance;
        bool masked=op && op.MaskOn(op.LocalViewUnit());
        material.SetFloat("_MistAmount",temporary && !column.MistActive ? .65f : 1.35f);
        material.SetFloat("_Motion",reduced ? .27f : 1f);
        material.SetFloat("_Glass",reduced || masked?0f:.70f);
    }

    void OnDestroy()
    {
        if(material) Destroy(material);
        if(lowland==this)lowland=null;
        if(causeway==this)causeway=null;
        if(noise && --users==0) { Destroy(sharedNoise); sharedNoise=null;billows=null; }
    }
}
