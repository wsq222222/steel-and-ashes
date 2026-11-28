using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Map ink follows the walking surface. The scene depth buffer masks it behind visible architecture.
public sealed class OsowiecRouteInk : MonoBehaviour
{
    struct Sample { public Vector3 floor; public float distance; }
    readonly List<Sample> samples = new();
    readonly List<Vector3> vertices = new();
    readonly List<Color> colors = new();
    readonly List<int> indices = new();
    Mesh mesh; MeshRenderer ink; Material material;
    float stop, lastWidth;
    bool lastBankClosed;
    static readonly Color Safe = new(.94f,.87f,.64f), Future = new(.72f,.74f,.78f), Danger = new(1f,.57f,.25f);
    static readonly Color Border = new(.17f,.15f,.11f,.94f);

    void Awake()
    {
        var drawing=new GameObject("Surveyed route and endpoints");
        drawing.transform.SetParent(transform,false);
        mesh=new Mesh {name="Tactical movement order"};
        mesh.MarkDynamic();
        drawing.AddComponent<MeshFilter>().sharedMesh=mesh;
        ink=drawing.AddComponent<MeshRenderer>();
        material=new Material(Shader.Find("Sprites/Default"));
        ink.sharedMaterial=material;
        ink.shadowCastingMode=ShadowCastingMode.Off;
        ink.receiveShadows=false;
    }

    void OnDestroy() { if(mesh) Destroy(mesh); if(material) Destroy(material); }

    public void Clear() { samples.Clear(); mesh.Clear(); }
    public void Display(Camera cam,bool visible)
    {
        ink.enabled=visible && samples.Count>1;
        if(!ink.enabled || !cam) return;
        float width=cam.orthographic ? cam.orthographicSize*2 : 
            Mathf.Abs(cam.WorldToViewportPoint(samples[0].floor).z)*2*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f);
        width=Mathf.Clamp(width*3.5f/Mathf.Max(1,cam.pixelHeight),.055f,.36f);
        if(lastWidth<=0 || Mathf.Abs(width-lastWidth)>lastWidth*.10f || lastBankClosed!=OsowiecOperation.BankClosed)
        {
            lastBankClosed=OsowiecOperation.BankClosed;
            lastWidth=width;
            Rebuild(width);
        }
    }

    public void Set(Vector3[] path,float reachable)
    {
        Clear();
        stop=reachable;
        float travelled=0;
        Append(path[0],0);
        for(int i=1;i<path.Length;i++)
        {
            Vector3 a=path[i-1], b=path[i];
            float len=Vector3.Distance(a,b);
            if(len<.001f) continue;
            int count=Mathf.CeilToInt(len/.22f);
            for(int j=1;j<=count;j++)
            {
                float t=(float)j/count;
                float distance=travelled+len*t;
                if(samples[^1].distance < stop-.001f && distance>stop+.001f)
                    Append(Vector3.Lerp(a,b,(stop-travelled)/len),stop);
                Append(Vector3.Lerp(a,b,t),distance);
            }
            travelled+=len;
        }
        lastWidth=0;
    }

    void Append(Vector3 point,float distance)
    {
        float nearest=float.PositiveInfinity;
        float y=point.y;
        // Search from just above navigation height, so a roof cannot be mistaken for its floor.
        foreach(var hit in Physics.RaycastAll(point+Vector3.up*.5f,Vector3.down,1f,~0,QueryTriggerInteraction.Ignore))
        {
            float delta=Mathf.Abs(hit.point.y-point.y);
            if(hit.normal.y<.55f || delta>=nearest || hit.collider.GetComponentInParent<Unit>()) continue;
            nearest=delta;
            y=hit.point.y;
        }
        point.y=y;
        samples.Add(new Sample {floor=point+Vector3.up*.065f,distance=distance});
    }

    void Rebuild(float width)
    {
        vertices.Clear(); indices.Clear(); colors.Clear();
        Vector3 endpoint=samples[^1].floor, halt=endpoint;
        for(int i=1;i<samples.Count;i++)
        {
            var a=samples[i-1]; var b=samples[i];
            if(a.distance<=stop+.001f) halt=a.floor;
            bool future=(a.distance+b.distance)*.5f>stop+.001f;
            if(future && Mathf.FloorToInt((a.distance+b.distance)*.5f/.57f)%2!=0) continue;
            Color tint=OsowiecOperation.GasAt((a.floor+b.floor)*.5f) ? Danger : future ? Future : Safe;
            Stroke(a.floor,b.floor,width,Border);
            Stroke(a.floor+Vector3.up*.004f,b.floor+Vector3.up*.004f,width*.54f,tint);
        }
        if(samples[^1].distance>stop+.015f) Ring(halt,width,20,Safe);
        Ring(endpoint,width,4,Future);
        mesh.Clear();
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices,0);
        mesh.RecalculateBounds();
    }

    void Ring(Vector3 centre,float width,int sides,Color color)
    {
        centre+=Vector3.up*.01f;
        float radius=width*4;
        for(int i=0;i<sides;i++)
        {
            float a=i*Mathf.PI*2/sides, b=(i+1)*Mathf.PI*2/sides;
            Stroke(centre+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius,
                   centre+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius,width*.85f,color);
        }
    }

    void Stroke(Vector3 a,Vector3 b,float halfWidth,Color color)
    {
        Vector3 side=Vector3.Cross(Vector3.up,b-a).normalized*halfWidth;
        int i=vertices.Count;
        vertices.Add(a-side); vertices.Add(a+side); vertices.Add(b+side); vertices.Add(b-side);
        for(int c=0;c<4;c++) colors.Add(color);
        indices.Add(i);indices.Add(i+1);indices.Add(i+2);
        indices.Add(i);indices.Add(i+2);indices.Add(i+3);
    }
}

