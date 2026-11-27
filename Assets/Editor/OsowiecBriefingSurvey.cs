using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Export the actual playfield geometry for the printed operations sheet.
public static class OsowiecBriefingSurvey
{
    const string Field = "Assets/Osowiec/Osowiec.unity";
    [Serializable] class Survey
    {
        public int left=-60,right=56,bottom=-32,top=34,columns=261,rows=149;
        public float[] elevation;
        public List<Vector2> water=new();
        public List<Vector2> roofs=new();
        public List<string> names=new();
        public List<Vector2> marks=new();
        public List<Vector2> deployment=new();
        public List<Vector2> centralTrench=new();
    }
    // In these two cut banks the imported terrain has additional erosion vertices.
    // Keep the exact contact patches with the Blender source so the dressing can be rebuilt offline.
    [MenuItem("Steel and Ashes/Export trench sack supports")]
    public static void ExportSackSupports()
    {
        var scene=SceneManager.GetSceneByPath(Field);
        bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(Field,OpenSceneMode.Additive);
        try
        {
            var patches=new[]{
                new Bounds(new Vector3(-38.25f,1,-18.82f),new Vector3(6.4f,30,1.4f)),
                new Bounds(new Vector3(-17.85f,1,-5.30f),new Vector3(4.2f,30,1.3f))
            };
            var geometry=new SupportPatches();
            foreach(var tile in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MeshCollider>())
                    .Where(c=>c.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal) && patches.Any(b=>b.Intersects(c.bounds))))
            {
                var mesh=tile.sharedMesh;
                var vertices=mesh.vertices.Select(tile.transform.TransformPoint).ToArray();
                var tris=mesh.triangles;
                for(int i=0;i<tris.Length;i+=3)
                {
                    var a=vertices[tris[i]];var b=vertices[tris[i+1]];var c=vertices[tris[i+2]];
                    var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);
                    if(!patches.Any(p=>p.Intersects(bounds)))continue;
                    geometry.triangles.Add(a);geometry.triangles.Add(b);geometry.triangles.Add(c);
                }
            }
            if(geometry.triangles.Count==0)throw new InvalidOperationException("Missing Unity terrain for sack supports");
            File.WriteAllText("Assets/Osowiec/Art/Source~/trench_sack_supports.json",JsonUtility.ToJson(geometry));
            Debug.Log($"Exported {geometry.triangles.Count/3} terrain triangles for Blender sandbag contacts.");
        }
        finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
    }
    [Serializable] sealed class SupportPatches { public List<Vector3> triangles=new(); }

    static Vector2 XZ(Vector3 v)=>new(v.x,v.z);

    [MenuItem("Steel and Ashes/Export headquarters paper survey")]
    public static void Export()
    {
        var field=SceneManager.GetSceneByPath(Field);
        bool opened=!field.IsValid() || !field.isLoaded;
        if(opened)field=EditorSceneManager.OpenScene(Field,OpenSceneMode.Additive);
        try
        {
            var roots=field.GetRootGameObjects();
            var fort=roots.Single(r=>r.name=="Osowiec · Fort I");
            var survey=new Survey();
            survey.centralTrench.AddRange(OsowiecCentralTrench.Course);
            var bounds=new Bounds(new Vector3((survey.left+survey.right)*.5f,0,(survey.bottom+survey.top)*.5f),
                new Vector3(survey.right-survey.left,60,survey.top-survey.bottom));
            var earth=fort.GetComponentsInChildren<MeshCollider>(true)
                .Where(c=>c.name.StartsWith("Biebrza ground Ground_",StringComparison.Ordinal) && bounds.Intersects(c.bounds)).ToArray();
            if(earth.Length==0)throw new InvalidOperationException("Missing game terrain");
            survey.elevation=new float[survey.rows*survey.columns];
            for(int row=0;row<survey.rows;row++)for(int col=0;col<survey.columns;col++)
            {
                var p=new Vector3(Mathf.Lerp(survey.left,survey.right,col/(float)(survey.columns-1)),25,
                    Mathf.Lerp(survey.top,survey.bottom,row/(float)(survey.rows-1)));
                var ray=new Ray(p,Vector3.down);bool found=false;
                foreach(var tile in earth)
                {
                    if(!tile.bounds.IntersectRay(ray) || !tile.Raycast(ray,out var hit,60))continue;
                    survey.elevation[row*survey.columns+col]=hit.point.y;found=true;break;
                }
                // Cutouts use separate structure meshes. The printer interpolates their surrounding ground.
                if(!found)survey.elevation[row*survey.columns+col]=-99;
            }
            foreach(var mesh in fort.GetComponentsInChildren<MeshFilter>(true))
            {
                bool water=mesh.name.Contains("River_Biebrza_") || mesh.name.Contains("Stillwater_oxbow_");
                bool eastern=mesh.name=="Battery vaults and turf" || (mesh.name.StartsWith("Battery ",StringComparison.Ordinal) && mesh.name.EndsWith(" roof",StringComparison.Ordinal));
                bool roof=eastern || mesh.name.Contains("Roof_Ward_canvas_lining") || mesh.name.Contains("Roof_vault");
                if(!water && !roof)continue;
                if(!bounds.Intersects(mesh.GetComponent<Renderer>().bounds))continue;
                var vertices=mesh.sharedMesh.vertices.Select(p=>mesh.transform.TransformPoint(p)).ToArray();
                var triangles=mesh.sharedMesh.triangles;
                var projected=water?survey.water:survey.roofs;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                    if(eastern && (a.y+b.y+c.y)/3f<3.33f)continue;
                    if(roof && Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.y)<.20f)continue;
                    projected.Add(XZ(a));projected.Add(XZ(b));projected.Add(XZ(c));
                }
            }
            if(survey.water.Count==0)throw new InvalidOperationException("Missing river and pools");
            void MarkAt(string key,Vector3 p){survey.names.Add(key);survey.marks.Add(XZ(p));}
            MarkAt("phone",OsowiecOperation.Telephone);MarkAt("junction",OsowiecOperation.Junction);
            MarkAt("casemate",OsowiecOperation.Casemate);MarkAt("lowland",OsowiecOperation.Lowland);
            MarkAt("yard",OsowiecOperation.Battery);MarkAt("north",OsowiecOperation.NorthLine);
            MarkAt("south",OsowiecOperation.SouthLine);MarkAt("ward",OsowiecReliefColumn.Hospital);
            var gallery=roots.SelectMany(r=>r.GetComponentsInChildren<Unit>(true)).Single(u=>u.name=="Дозор у бастиона");
            MarkAt("gallery",gallery.transform.position);
            var passage=fort.GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name=="Battery passage and chambers");
            var edges=passage.sharedMesh.vertices.Select(p=>passage.transform.TransformPoint(p))
                .Where(p=>Mathf.Abs(Mathf.Abs(p.x-gallery.transform.position.x)-1.65f)<.006f).ToArray();
            if(edges.Length==0)throw new InvalidOperationException("Lower passage slab missing");
            var x=gallery.transform.position.x;
            MarkAt("entrySouth",new Vector3(x,0,edges.Min(p=>p.z)));
            MarkAt("entryNorth",new Vector3(x,0,edges.Max(p=>p.z)));
            var grid=roots.SelectMany(r=>r.GetComponentsInChildren<ChessGrid>(true)).Single();
            foreach(var sector in OsowiecDeploymentSectors.Cells)
                foreach(var cell in sector)survey.deployment.Add(XZ(grid.GridToWorldPosition(cell.x,cell.y)));
            string path=Path.GetFullPath("Library/OsowiecBriefingSurvey.json");
            File.WriteAllText(path,JsonUtility.ToJson(survey));
            Debug.Log($"PASS paper survey: {survey.elevation.Length} ground samples, {survey.water.Count/3} water triangles, two starts, {survey.marks.Count} named posts. {path}");
        }
        finally{if(opened)EditorSceneManager.CloseScene(field,true);}
    }
}
