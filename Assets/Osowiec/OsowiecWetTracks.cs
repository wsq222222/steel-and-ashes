using UnityEngine;
using UnityEngine.Rendering;

// Soil deforms in its own opaque material. Hard floors only receive a wet transfer.
[DefaultExecutionOrder(200)]
public sealed class OsowiecWetTracks : MonoBehaviour
{
    const int Capacity = 1536;
    static OsowiecWetTracks active;
    readonly Vector3[] vertices = new Vector3[Capacity * 4];
    readonly Vector2[] shapes = new Vector2[Capacity * 4];
    readonly Vector2[] times = new Vector2[Capacity * 4];
    readonly Color32[] colors = new Color32[Capacity * 4];
    readonly RaycastHit[] contacts = new RaycastHit[20];
    Mesh mesh;
    Material material;
    OsowiecMudField mud;
    Collider galleryBank;
    int[] dirtStart,dirtEnd;
    int cursor;
    bool changed;
    float rainClock;
    public float RainTime => rainClock;
    public int PlantedBoots { get; private set; }
    public int WheelSegments { get; private set; }
    MeshRenderer transfers;
    public bool ImpressionsVisible => mud.Visible;
    public float SoilHeightAt(Vector3 at,bool gallery=false) => mud.ReadHeight(at,rainClock,gallery);
    public void ShowImpressions(bool visible)
    {
        mud.Flush(rainClock);
        transfers.enabled=visible;
        mud.SetVisible(visible);
    }

    void Awake()
    {
        active = this;
        mud=new OsowiecMudField();
        BindGalleryBank();
        var bed = new GameObject("Мокрая земля — следы и колеи");
        bed.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Следы в грунте" };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        var indices = new int[Capacity * 6];
        for (int i = 0; i < Capacity; i++)
        {
            int v = i*4, j = i*6;
            indices[j] = v; indices[j+1] = v+2; indices[j+2] = v+1;
            indices[j+3] = v+2; indices[j+4] = v+3; indices[j+5] = v+1;
        }
        mesh.triangles = indices;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(500,100,500));
        bed.AddComponent<MeshFilter>().sharedMesh = mesh;
        var paint = bed.AddComponent<MeshRenderer>();
        transfers = paint;
        paint.shadowCastingMode = ShadowCastingMode.Off;
        var shader = Resources.Load<Shader>("OsowiecTrackWash");
        if (!shader) { enabled = false; return; }
        material = new Material(shader);
        paint.sharedMaterial = material;
    }

    void BindGalleryBank()
    {
        var gallery=GameObject.Find("Battery gallery earthwork");
        var survey=FindFirstObjectByType<OsowiecGroundEvents>();
        if (!gallery || !survey || survey.intact==null || survey.intact.Length==0 || !survey.intact[0]) return;
        var renderer=gallery.GetComponent<MeshRenderer>();
        var collider=gallery.GetComponent<MeshCollider>();
        if (!renderer || !collider || !collider.sharedMesh) return;
        var soil=survey.intact[0].GetComponent<MeshRenderer>().sharedMaterial;
        var materials=renderer.sharedMaterials;
        dirtStart=new int[materials.Length];dirtEnd=new int[materials.Length];
        var blocks=new MaterialPropertyBlock[materials.Length];
        for (int slot=0;slot<materials.Length;slot++)
        {
            bool turf=materials[slot].name=="Sod";
            if (!turf && materials[slot].name!="Earth") continue;
            var sub=collider.sharedMesh.GetSubMesh(slot);
            dirtStart[slot]=sub.indexStart/3;
            dirtEnd[slot]=dirtStart[slot]+sub.indexCount/3;
            materials[slot]=soil;
            var block=blocks[slot]=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block,slot);
            block.SetFloat("_OsowiecGallerySoil",1);
            block.SetFloat("_OsowiecBankCover",turf ? .81f : .14f);
        }
        renderer.sharedMaterials=materials;
        for(int slot=0;slot<blocks.Length;slot++)
            if(blocks[slot]!=null) renderer.SetPropertyBlock(blocks[slot],slot);
        galleryBank=collider;
    }

    public static void Boot(Vector3 foot, Vector3 forward, bool left)
    {
        if (active && active.Ground(foot, out var hit, .65f))
            active.PlantBoot(hit, forward, left);
    }

    public static bool Boot(RaycastHit hit, Vector3 forward, bool left)
        => active && active.enabled && hit.collider && active.PlantBoot(hit, forward, left);

    bool Ground(Vector3 tip, out RaycastHit ground, float tolerance)
    {
        ground = default;
        var n = Physics.RaycastNonAlloc(tip+Vector3.up*tolerance, Vector3.down, contacts,
            tolerance*2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < n; ++i)
        {
            var h = contacts[i];
            if (h.normal.y < .56f || h.distance >= best || h.collider.GetComponentInParent<Unit>()) continue;
            if (h.collider.GetComponentInParent<OsowiecCartMotion>()) continue;
            ground = h; best = h.distance;
        }
        return ground.collider;
    }

    bool PlantBoot(RaycastHit hit, Vector3 forward, bool left)
    {
        var floor = hit.collider.GetComponentInParent<OsowiecAcousticSurface>();
        if ((!floor || floor.surface==OsowiecGroundSurface.Mud) && OsowiecGroundEvents.TryWaterAt(hit.point, out var level))
        {
            OsowiecShotEffects.WalkingRipple(level);
            return false;
        }
        Vector3 f = Vector3.ProjectOnPlane(forward, hit.normal).normalized;
        if (f.sqrMagnitude < .8f) return false;
        if (IsSoil(hit))
        {
            if(!mud.Stamp(hit.point,f,left,rainClock,hit.collider==galleryBank)) return false;
            ++PlantedBoots;
            return true;
        }
        if (!floor || floor.surface==OsowiecGroundSurface.Mud) return false;
        Vector3 s = Vector3.Cross(hit.normal,f)*.092f;
        Vector3 mid = hit.point+hit.normal*.0015f;
        float life = floor.surface == OsowiecGroundSurface.Brick ? 38f : 76f;
        Quad(mid-f*.164f,mid+f*.164f,s,s,left ? 0 : 1,life,.55f);
        ++PlantedBoots;
        return true;
    }

    public static void Wheel(Vector3 from, Vector3 to)
    {
        if (active) active.PlantWheel(from,to);
    }

    internal static bool IsSoil(RaycastHit hit)
    {
        if(!hit.collider) return false;
        if(active && hit.collider==active.galleryBank)
        {
            int t=hit.triangleIndex;
            for(int i=0;i<active.dirtStart.Length;i++)
                if(t>=active.dirtStart[i] && t<active.dirtEnd[i]) return true;
            return false;
        }
        return hit.collider.name.StartsWith("Biebrza ground ",System.StringComparison.Ordinal);
    }

    void PlantWheel(Vector3 from, Vector3 to)
    {
        if ((from-to).sqrMagnitude > 1.6f || !Ground(from,out var a,.65f) || !Ground(to,out var b,.65f)) return;
        var track = b.point-a.point;
        if (track.sqrMagnitude < .0016f || Mathf.Abs(track.y) > .36f) return;
        var mid = (a.point+b.point)*.5f;
        var floor = b.collider.GetComponentInParent<OsowiecAcousticSurface>();
        if ((!floor || floor.surface==OsowiecGroundSurface.Mud) && OsowiecGroundEvents.TryWaterAt(mid,out _)) return;
        var f = track.normalized;
        if(IsSoil(b) && IsSoil(a) && (a.collider==galleryBank)==(b.collider==galleryBank))
        {
            if(mud.Stamp(mid,f,true,rainClock,b.collider==galleryBank,new Vector2(track.x,track.z).magnitude)) ++WheelSegments;
            return;
        }
        if (!floor || floor.surface==OsowiecGroundSurface.Mud || a.collider.GetComponentInParent<OsowiecAcousticSurface>()!=floor) return;
        var sa = Vector3.Cross(a.normal,f).normalized*.049f;
        var sb = Vector3.Cross(b.normal,f).normalized*.049f;
        Quad(a.point+a.normal*.0015f-f*.008f,b.point+b.normal*.0015f+f*.008f,sa,sb,2,52f,.36f);
        ++WheelSegments;
    }

    void Quad(Vector3 heel,Vector3 toe,Vector3 heelSide,Vector3 toeSide,int shape,float life,float intensity)
    {
        int i = cursor*4;
        vertices[i] = heel-heelSide; vertices[i+1] = heel+heelSide;
        vertices[i+2] = toe-toeSide; vertices[i+3] = toe+toeSide;
        float left = shape==1 ? 1f : 0f, right = 1f-left;
        float baseU = shape==2 ? 2f : 0;
        shapes[i] = new Vector2(baseU+left,0); shapes[i+1] = new Vector2(baseU+right,0);
        shapes[i+2] = new Vector2(baseU+left,1); shapes[i+3] = new Vector2(baseU+right,1);
        var tint = new Color(1f,1f,1f,intensity);
        for (int k=0;k<4;k++) { times[i+k]=new Vector2(rainClock,life); colors[i+k]=tint; }
        cursor=(cursor+1)%Capacity;
        changed=true;
    }

    void LateUpdate()
    {
        var game=GameManager.Instance;
        if(!game || !game.IsPaused()) rainClock+=Time.deltaTime;
        mud.Flush(rainClock);
        material.SetFloat("_RainClock",rainClock);
        if (!changed) return;
        mesh.vertices=vertices; mesh.uv=shapes; mesh.uv2=times; mesh.colors32=colors;
        changed=false;
    }

    void OnDestroy()
    {
        if (active==this) active=null;
        mud?.Dispose();
        if (mesh) Destroy(mesh);
        if (material) Destroy(material);
    }
}
