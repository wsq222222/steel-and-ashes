using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Rasterise the actual underside of each roof once, while the source meshes are readable.
public static class OsowiecCeilings
{
    public static void Bake(OsowiecFortCutaway field)
    {
        var all=field.GetComponentsInChildren<MeshRenderer>(true);
        Renderer Named(string id)=>all.Single(r=>r.name==id);
        var north=Named("Battery north pavilion roof");
        var south=Named("Battery south pavilion roof");
        var gallery=Named("Battery gallery roof");
        var lower=Named("Battery vaults and turf");
        var bank=Named("Battery gallery earthwork");
        var deck=Named("Battery gallery and ramp");
        var tops=new[]{north,south,gallery,lower};
        field.batteryRoofs=tops.Select(r=>r.GetComponent<MeshCollider>()).ToArray();
        var old=field.surfaces.Where(r=>r && !r.name.StartsWith("Battery ",StringComparison.Ordinal)).ToArray();
        field.surfaces=old.Concat(tops).Append(deck).Append(bank).Distinct().ToArray();
        var rooms=new List<OsowiecFortCutaway.Room>();
        void Add(string name,Renderer ceiling,IEnumerable<Renderer> hide,float min=-12)
        {
            rooms.Add(Survey(name,ceiling,hide.Distinct().ToArray(),min));
        }
        var ward=old.Where(r=>r.name.Contains("Ward_")).ToArray();
        var bridge=old.Where(r=>r.name.Contains("maintenance_bridge")).ToArray();
        var central=old.Except(ward).Except(bridge).ToArray();
        Add("Каземат",Named("Roof_vault"),central);
        Add("Лазарет",Named("Roof_Ward_canvas_lining"),ward);
        if(bridge.Length!=0)Add("Мостик траншеи",bridge[0],bridge);
        Add("Северный зал",north,new[]{north});
        Add("Южный зал",south,new[]{south});
        Add("Верхняя галерея",gallery,new[]{gallery},3.15f);
        Add("Нижний ход",lower,new[]{lower,bank,deck,gallery});
        field.rooms=rooms.ToArray();
        EditorUtility.SetDirty(field);
    }

    static OsowiecFortCutaway.Room Survey(string name,Renderer roof,Renderer[] hide,float min)
    {
        const float spacing=.18f;
        var bounds=roof.bounds;
        var room=new OsowiecFortCutaway.Room{name=name,hide=hide,minFoot=min,spacing=spacing,
            origin=new Vector2(bounds.min.x,bounds.min.z),width=Mathf.CeilToInt(bounds.size.x/spacing),
            depth=Mathf.CeilToInt(bounds.size.z/spacing)};
        room.ceiling=Enumerable.Repeat(-1000f,room.width*room.depth).ToArray();
        var mesh=roof.GetComponent<MeshFilter>().sharedMesh;
        var points=mesh.vertices.Select(roof.transform.TransformPoint).ToArray();var tris=mesh.triangles;
        int GridX(float x)=>Mathf.Clamp(Mathf.FloorToInt((x-room.origin.x)/spacing),0,room.width-1);
        int GridZ(float z)=>Mathf.Clamp(Mathf.FloorToInt((z-room.origin.y)/spacing),0,room.depth-1);
        for(int i=0;i<tris.Length;i+=3)
        {
            var a=points[tris[i]];var b=points[tris[i+1]];var c=points[tris[i+2]];
            var ab=b-a;var ac=c-a;float det=ab.x*ac.z-ab.z*ac.x;
            if(Mathf.Abs(det)<.00001f || Mathf.Abs(Vector3.Cross(ab,ac).normalized.y)<.30f)continue;
            int x0=GridX(Mathf.Min(a.x,b.x,c.x)),x1=GridX(Mathf.Max(a.x,b.x,c.x));
            int z0=GridZ(Mathf.Min(a.z,b.z,c.z)),z1=GridZ(Mathf.Max(a.z,b.z,c.z));
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {
                float dx=room.origin.x+(x+.5f)*spacing-a.x,dz=room.origin.y+(z+.5f)*spacing-a.z;
                float u=(dx*ac.z-dz*ac.x)/det,v=(ab.x*dz-ab.z*dx)/det;
                if(u<0 || v<0 || u+v>1)continue;
                var height=a.y+ab.y*u+ac.y*v;
                int sample=z*room.width+x;
                if(room.ceiling[sample]<-990f || height<room.ceiling[sample])room.ceiling[sample]=height;
            }
        }
        if(!room.ceiling.Any(y=>y>-990f))throw new InvalidOperationException("Empty roof "+name);
        return room;
    }
}
