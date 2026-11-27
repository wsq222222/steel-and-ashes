using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The landscape is exported in independent culling tiles. FBX keeps their
// boundary positions coincident but computes smoothing separately for each tile.
public sealed class OsowiecSurveyNormals : AssetPostprocessor
{
    const string Survey = "Assets/Osowiec/Art/Fort1916/MarshFort.fbx";
    public const string Revision = "osowiec-welded-banks-3";

    struct Joint
    {
        public int count;
        public Vector3 normal;
        public float cover;
    }

    static bool Terrain(MeshFilter f) =>
        f.name.StartsWith("Ground_",StringComparison.Ordinal) ||
        f.name.StartsWith("Flood_meadow_",StringComparison.Ordinal) ||
        f.name.StartsWith("Event_",StringComparison.Ordinal);

    static Vector3Int Key(Vector3 position,float units)
        => Vector3Int.RoundToInt(position*units);

    public static void EnsureCurrentSurvey()
    {
        var importer=AssetImporter.GetAtPath(Survey) as ModelImporter;
        if(!importer || (importer.userData??"").Contains(Revision)) return;
        importer.userData=(importer.userData+"\n"+Revision).Trim();
        importer.SaveAndReimport();
    }

    // The exchangeable 25 cm crater plates meet a 50 cm bank grid. Their
    // intermediate rim samples have to lie exactly on the coarse neighbours' edges.
    static void StitchEventRims(MeshFilter[] filters,Matrix4x4 inverseSurvey)
    {
        var bank=new Dictionary<Vector2Int,float>();
        foreach(var f in filters.Where(f=>f.name.StartsWith("Ground_",StringComparison.Ordinal)))
        {
            var matrix=inverseSurvey*f.transform.localToWorldMatrix;
            foreach(var v in f.sharedMesh.vertices)
            {
                var p=matrix.MultiplyPoint3x4(v);
                bank[new Vector2Int(Mathf.RoundToInt(2*p.x),Mathf.RoundToInt(2*p.z))]=p.y;
            }
        }
        foreach(var f in filters.Where(f=>f.name.StartsWith("Event_",StringComparison.Ordinal)))
        {
            var matrix=inverseSurvey*f.transform.localToWorldMatrix;
            var inverse=matrix.inverse;
            var mesh=f.sharedMesh;var verts=mesh.vertices;
            var centre=f.name.EndsWith("_0",StringComparison.Ordinal)?new Vector2(-4,13):new Vector2(11,11);
            for(int i=0;i<verts.Length;i++)
            {
                var p=matrix.MultiplyPoint3x4(verts[i]);
                float dx=Mathf.Abs(p.x-centre.x),dz=Mathf.Abs(p.z-centre.y);
                if(Mathf.Abs(Mathf.Max(dx,dz)-2)>.002f)continue;
                int x=Mathf.RoundToInt(p.x*4),z=Mathf.RoundToInt(p.z*4);
                var a=new Vector2Int(Mathf.FloorToInt(x*.5f),Mathf.FloorToInt(z*.5f));
                var b=new Vector2Int(Mathf.CeilToInt(x*.5f),Mathf.CeilToInt(z*.5f));
                if(bank.TryGetValue(a,out float lo)&&bank.TryGetValue(b,out float hi))
                {
                    p.y=(lo+hi)*.5f;
                    verts[i]=inverse.MultiplyPoint3x4(p);
                }
            }
            mesh.vertices=verts;mesh.RecalculateBounds();
        }
    }

    void OnPostprocessModel(GameObject model)
    {
        if(assetPath!=Survey) return;
        var filters=model.GetComponentsInChildren<MeshFilter>(true).Where(Terrain).ToArray();
        var marks=model.GetComponentsInChildren<Transform>(true);
        Vector3 origin=marks.First(t=>t.name=="Survey_origin").position;
        Matrix4x4 survey=Matrix4x4.identity;
        survey.SetColumn(0, marks.First(t=>t.name=="Survey_east").position-origin);
        survey.SetColumn(1, marks.First(t=>t.name=="Survey_up").position-origin);
        survey.SetColumn(2, marks.First(t=>t.name=="Survey_north").position-origin);
        survey.SetColumn(3,new Vector4(origin.x,origin.y,origin.z,1));
        var inverseSurvey=survey.inverse;
        StitchEventRims(filters,inverseSurvey);
        float metre=Vector3.Distance(marks.First(t=>t.name=="Survey_up").position,
                                     marks.First(t=>t.name=="Survey_origin").position);
        float units=1000f/metre;
        var joints=new Dictionary<Vector3Int,Joint>();
        for(int t=0;t<filters.Length;t++)
        {
            var f=filters[t]; var mesh=f.sharedMesh;
            var v=mesh.vertices; var n=mesh.normals; var c=mesh.colors;
            var toWorld=f.transform.localToWorldMatrix;
            var normalToWorld=f.transform.worldToLocalMatrix.transpose;
            for(int i=0;i<v.Length;i++)
            {
                var key=Key(toWorld.MultiplyPoint3x4(v[i]),units);
                joints.TryGetValue(key,out Joint joint);
                joint.normal+=normalToWorld.MultiplyVector(n[i]).normalized;
                joint.cover+=c[i].r; joint.count++; joints[key]=joint;
            }
        }
        int fixedVertices=0;
        foreach(var f in filters)
        {
            var mesh=f.sharedMesh; var v=mesh.vertices; var n=mesh.normals; var c=mesh.colors;
            var toWorld=f.transform.localToWorldMatrix;
            var normalToLocal=toWorld.transpose;
            for(int i=0;i<v.Length;i++)
            {
                var joint=joints[Key(toWorld.MultiplyPoint3x4(v[i]),units)];
                if(joint.count<=1) continue;
                n[i]=normalToLocal.MultiplyVector(joint.normal).normalized;
                float w=joint.cover/joint.count; c[i]=new Color(w,w,w,c[i].a);
                fixedVertices++;
            }
            mesh.normals=n; mesh.colors=c;
        }

        int added=0;
        foreach(var f in filters.Where(tile=>tile.name.StartsWith("Ground_",StringComparison.Ordinal))) {
            int original=f.sharedMesh.vertexCount;
            OsowiecBankErosion.Refine(f.sharedMesh, inverseSurvey*f.transform.localToWorldMatrix);
            added+=f.sharedMesh.vertexCount-original;
        }
        Debug.Log($"Osowiec ground: baked {added} relief vertices into reachable banks.");
        Debug.Log($"Osowiec ground: reconciled {fixedVertices} imported border vertices across {filters.Length} terrain tiles.");
    }
}
