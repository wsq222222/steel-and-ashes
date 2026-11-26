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
    public const string Revision = "osowiec-eroded-playable-banks-2";

    struct Joint
    {
        public int tile, count;
        public bool shared;
        public Vector3 normal;
        public float cover;
    }

    static bool Terrain(MeshFilter f) =>
        f.name.StartsWith("Ground_",StringComparison.Ordinal) ||
        f.name.StartsWith("Flood_meadow_",StringComparison.Ordinal);

    static Vector3Int Key(Vector3 position,float units)
        => Vector3Int.RoundToInt(position*units);

    void OnPostprocessModel(GameObject model)
    {
        if(assetPath!=Survey) return;
        var filters=model.GetComponentsInChildren<MeshFilter>(true).Where(Terrain).ToArray();
        var marks=model.GetComponentsInChildren<Transform>(true);
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
                if(!joints.TryGetValue(key,out Joint joint)) joint.tile=t;
                else if(joint.tile!=t) joint.shared=true;
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
                if(!joint.shared) continue;
                n[i]=normalToLocal.MultiplyVector(joint.normal).normalized;
                float w=joint.cover/joint.count; c[i]=new Color(w,w,w,c[i].a);
                fixedVertices++;
            }
            mesh.normals=n; mesh.colors=c;
        }

        Vector3 origin=marks.First(t=>t.name=="Survey_origin").position;
        Matrix4x4 survey=Matrix4x4.identity;
        survey.SetColumn(0, marks.First(t=>t.name=="Survey_east").position-origin);
        survey.SetColumn(1, marks.First(t=>t.name=="Survey_up").position-origin);
        survey.SetColumn(2, marks.First(t=>t.name=="Survey_north").position-origin);
        survey.SetColumn(3,new Vector4(origin.x,origin.y,origin.z,1));
        var inverseSurvey=survey.inverse;
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
