using UnityEngine;

public sealed class OsowiecDeskRain : MonoBehaviour
{
    const int Segments = 28;
    const float Period = 3.8f;
    readonly Vector3[] vertices = new Vector3[Segments*4+6];
    Mesh mesh;
    MeshRenderer surface;
    float elapsed;

    void Awake()
    {
        surface=GetComponent<MeshRenderer>();
        mesh=new Mesh { name="Map margin ripple and falling bead" };
        var triangles=new int[Segments*12+24];
        for (int ring=0;ring<2;ring++)
        for (int j=0;j<Segments;j++)
        {
            int o=ring*Segments*2;
            int a=o+j*2, b=o+((j+1)%Segments)*2, k=(ring*Segments+j)*6;
            triangles[k]=a; triangles[k+1]=b+1; triangles[k+2]=a+1;
            triangles[k+3]=a; triangles[k+4]=b; triangles[k+5]=b+1;
        }
        int drop=Segments*4;
        for(int i=0;i<4;i++)
        {
            int k=Segments*12+i*6, j=drop+1+i, next=drop+1+(i+1)%4;
            triangles[k]=drop; triangles[k+1]=next; triangles[k+2]=j;
            triangles[k+3]=drop+5; triangles[k+4]=j; triangles[k+5]=next;
        }
        mesh.MarkDynamic(); mesh.vertices=vertices; mesh.triangles=triangles;
        mesh.bounds=new Bounds(new Vector3(0,.14f,0),new Vector3(.102f,.34f,.094f));
        GetComponent<MeshFilter>().sharedMesh=mesh;
    }

    void Update()
    {
        surface.enabled=!OsowiecFieldSettings.ReducedEffects;
        if (!surface.enabled) return;
        elapsed=Mathf.Repeat(elapsed+Time.deltaTime,Period);
        for(int ring=0;ring<2;ring++)
        {
            float phase=Mathf.Clamp01((elapsed-.40f-ring*.12f)/.88f);
            float radius=.002f+.023f*phase;
            float width=.0012f*Mathf.Sin(phase*Mathf.PI);
            for(int j=0;j<Segments;j++)
            {
                float a=j*2*Mathf.PI/Segments;
                var radial=new Vector3(Mathf.Cos(a),0,.69f*Mathf.Sin(a));
                int i=2*(ring*Segments+j);
                vertices[i]=radial*radius;
                vertices[i+1]=radial*(radius+width)+Vector3.up*width*.65f;
            }
        }
        float t=Mathf.Clamp01(elapsed/.40f), r=elapsed<.40f ? .0017f : 0;
        Vector3 centre=Vector3.up*(.265f*(1-t*t));
        int start=Segments*4;
        vertices[start]=centre+Vector3.up*r*2.5f;
        for(int j=0;j<4;j++)vertices[start+1+j]=centre+r*new Vector3(Mathf.Cos(j*Mathf.PI*.5f),0,Mathf.Sin(j*Mathf.PI*.5f));
        vertices[start+5]=centre-Vector3.up*r*1.6f;
        mesh.vertices=vertices; mesh.RecalculateNormals();
    }

    void OnDestroy() { if(mesh) Destroy(mesh); }
}
