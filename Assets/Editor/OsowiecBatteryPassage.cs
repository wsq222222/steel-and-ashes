using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class OsowiecBatteryFort
{
    const string Landings = "Battery court passage landings";

    // The lower passage, both pavilions and the gallery piers share the same collider asset.
    // Rebuild just this mesh, keeping the placed scene objects, renderer overrides and references.
    public static void RepairPassageInLoadedScene()
    {
        var fort=GameObject.Find("Osowiec · Fort I")?.transform;
        var root=fort ? fort.Find(Name) : null;
        if(!root) throw new InvalidOperationException("Eastern battery missing in loaded scene.");
        Physics.SyncTransforms();
        float south=Ground(TunnelX,-13)+.025f, north=Ground(TunnelX,15)+.025f;
        var existing=root.Find("Battery passage and chambers");
        if(!existing)throw new InvalidOperationException("Missing lower masonry.");
        var lower=new Masonry();
        var scratch=new Masonry();
        Corridor(lower,scratch,south,north);
        Chamber(lower,scratch,scratch,32,9,1.19f);
        Chamber(lower,scratch,scratch,40,-8,2.10f);
        Gallery(lower,scratch,scratch,scratch,scratch,Ground(35.3f,1.1f)+.04f);
        var temporary=lower.Save(root,existing.name);
        var collider=existing.GetComponent<MeshCollider>();
        collider.sharedMesh=null;
        collider.sharedMesh=temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temporary.gameObject);
        foreach(string name in new[]{"Battery court unloading and tea bench","Freight pallet","Upper crate","Side case","Handcart body","Tea bench"})
        {
            var child=root.Find(name);
            if(child)Object.DestroyImmediate(child.gameObject);
        }
        InstallYardStores(root,false);
        DressPassageApproaches(fort,root,south,north);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    public static void VerifyPassageBothWays()
    {
        Debug.Log(OsowiecFortPassageCheck.Traverse());
    }

    static void DressPassageApproaches(Transform fort,Transform root,float south,float north)
    {
        var old=root.Find(Landings);
        if(old)Object.DestroyImmediate(old.gameObject);
        var dress=new Masonry();
        foreach(int end in new[]{-1,1})
        {
            float lip=end<0 ? -12 : 14;
            float top=end<0 ? south : north;
            // Climb out onto a visible lateral path, well short of the north retaining wall.
            // Survey each stone's corners so this paving follows the bank and never floats.
            for(int course=0;course<3;course++)
            for(int column=0;column<10;column++)
            {
                float x0=39.25f+column*.82f+(course%2)*.28f;
                float x1=x0+.795f;
                float z0=lip+end*(.07f+course*.45f), z1=z0+end*.425f;
                Vector3 P(float x,float z)=>V(x,Ground(x,z)+.033f,z);
                var a=P(x0,z0);var b=P(x0,z1);var c=P(x1,z1);var d=P(x1,z0);
                dress.Skin((course+column)%6==0 ? Finish.BrokenBrick : Finish.Concrete,U,a,b,c);
                dress.Skin((course+column)%6==0 ? Finish.BrokenBrick : Finish.Concrete,U,a,c,d);
            }
            // Timber edging and pale inset crosspieces read from the planning camera down into the cut.
            float arch=end<0 ? -4.50f : 5.50f;
            for(int i=0;i<10;i++)
            {
                float t=(i+.5f)/10;
                float z=Mathf.Lerp(lip,arch,t);
                float slope=(Bottom-top)/(arch-lip);
                var bed=V(TunnelX,Mathf.Lerp(top,Bottom,t)+.013f,z);
                dress.Box(i%3==0 ? Finish.Concrete : Finish.Timber,bed,R*2.34f,U*.036f,(F+U*slope)*.105f);
            }
            // Reveal the sides of the arch at night without narrowing its playable opening.
            var p=V(TunnelX-1.33f,Bottom+1.44f,arch+end*.17f);
            dress.Block(Finish.Iron,p+V(-.13f,.12f,0),.27f,.05f,.14f);
            dress.Block(Finish.Glass,p,.10f,.20f,.12f);
            dress.Block(Finish.Brass,p+U*.12f,.16f,.044f,.16f);
            dress.Block(Finish.Brass,p-U*.12f,.16f,.044f,.16f);
        }
        var landings=dress.Save(root,Landings,false).transform;
        foreach(int end in new[]{-1,1})
            Lamp(landings,V(TunnelX-.99f,Bottom+1.57f,(end<0?-4.5f:5.5f)+end*.38f),2.45f,4.3f);
        ClearCoverAndMoveCabinet(fort);
    }

    static void ClearCoverAndMoveCabinet(Transform fort)
    {
        const string prefix="Battery cleared ";
        var originals=AssetDatabase.LoadAllAssetsAtPath(Art+"MarshFort.fbx").OfType<Mesh>().ToLookup(m=>m.name);
        foreach(var filter in fort.GetComponentsInChildren<MeshFilter>(true))
        {
            bool cabinet=filter.name=="Battery_line_boxes";
            if(!cabinet && !filter.name.StartsWith("Marsh_cover_",StringComparison.Ordinal))continue;
            if(!cabinet && !filter.GetComponent<Renderer>().bounds.Intersects(new Bounds(V(43.65f,1,1),V(10,14,31))))continue;
            var source=filter.sharedMesh;
            if(source.name.StartsWith(prefix,StringComparison.Ordinal))source=originals[source.name.Substring(prefix.Length)].Single();
            var verts=source.vertices;
            var world=verts.Select(filter.transform.TransformPoint).ToArray();
            Mesh edited=null;
            Mesh Output()
            {
                if(edited)return edited;
                string label=prefix+source.name;
                string path=Art+label+".asset";
                edited=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!edited){edited=Object.Instantiate(source);AssetDatabase.CreateAsset(edited,path);}
                else EditorUtility.CopySerialized(source,edited);
                edited.name=label;
                return edited;
            }
            if(cabinet)
            {
                // The southern line cabinet was directly below the static exhibit in the old survey.
                // Keep both interact points and the northern cabinet in their original positions.
                var offset=V(-1.30f,Ground(36.35f,-4.22f)-Ground(37.65f,-4.56f),.34f);
                for(int i=0;i<verts.Length;i++)
                    if(world[i].z<0)verts[i]=filter.transform.InverseTransformPoint(world[i]+offset);
                Output().vertices=verts;
            }
            else for(int slot=0;slot<source.subMeshCount;slot++)
            {
                var indices=source.GetTriangles(slot);
                var kept=new List<int>(indices.Length);
                for(int i=0;i<indices.Length;i+=3)
                {
                    var p=(world[indices[i]]+world[indices[i+1]]+world[indices[i+2]])/3;
                    bool cut=Mathf.Abs(p.x-TunnelX)<2.24f && p.z> -12.60f && p.z<14.60f;
                    bool paved=p.x>39.05f && p.x<47.95f && ((p.z> -13.64f && p.z< -11.88f) || (p.z>13.88f && p.z<15.64f));
                    if(cut || paved)continue;
                    kept.Add(indices[i]);kept.Add(indices[i+1]);kept.Add(indices[i+2]);
                }
                if(kept.Count!=indices.Length)Output().SetTriangles(kept,slot);
            }
            if(!edited)continue;
            edited.RecalculateBounds();EditorUtility.SetDirty(edited);
            filter.sharedMesh=edited;EditorUtility.SetDirty(filter);
            var collider=filter.GetComponent<MeshCollider>();
            if(collider){collider.sharedMesh=null;collider.sharedMesh=edited;EditorUtility.SetDirty(collider);}
        }
    }
}
