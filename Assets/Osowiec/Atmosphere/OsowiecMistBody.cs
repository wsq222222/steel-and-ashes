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
        if(noise && --users==0) { Destroy(sharedNoise); sharedNoise=null; }
    }
}
