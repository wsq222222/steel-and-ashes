using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Refine the half-metre survey only along the eroded banks players can approach.
// The shared mesh drives both the visible ground and its existing mesh collider.
internal static class OsowiecBankErosion
{
    struct Sample
    {
        public Vector3 position, normal;
        public Color colour;
        public Vector2 uv;
        public float exposure;
    }

    static readonly Vector2[][] Routes = {
        new[] { new Vector2(-48,-16), new(-34,-16), new(-34,-9), new(-25,-9), new(-25,-3), new(-15,-3) },
        new[] { new Vector2(-15,-3), new(-15,6), new(-9,12), new(0,14), new(10,11), new(18,6), new(25,3), new(33,0) },
        new[] { new Vector2(-15,-3), new(-13,-12), new(-9,-12), new(10,-12), new(16,-12), new(20,-4), new(25,0), new(33,0) }
    };

    static float Ease(float a, float b, float value) =>
        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, value));

    internal static float Exposure(Vector3 p, Vector3 n)
    {
        if (p.y < .22f || p.x < -56 || p.x > 44 || Mathf.Abs(p.z) > 25) return 0;
        Vector2 xz = new(p.x, p.z);
        float distanceSqr = float.MaxValue;
        foreach (var route in Routes)
            for (int i = 1; i < route.Length; i++)
            {
                var edge = route[i] - route[i-1];
                var rel = xz - route[i-1];
                float t = Mathf.Clamp01(Vector2.Dot(rel, edge) / edge.sqrMagnitude);
                distanceSqr = Mathf.Min(distanceSqr, (rel - t * edge).sqrMagnitude);
            }
        float dist = Mathf.Sqrt(distanceSqr);
        float strip = Ease(1.65f, 2.75f, dist) * (1 - Ease(6f, 9f, dist));
        // Hold the seam against the preauthored collapsible pieces fixed.
        float plateA = Mathf.Max(Mathf.Abs(p.x + 4), Mathf.Abs(p.z - 13));
        float plateB = Mathf.Max(Mathf.Abs(p.x - 11), Mathf.Abs(p.z - 11));
        return strip * Ease(.22f, .78f, p.y) * Ease(2.25f, 2.9f, Mathf.Min(plateA, plateB))
             * Mathf.Lerp(.35f, 1, Ease(.05f, .45f, 1 - n.y));
    }

    static float HeightOffset(float x, float y, float z)
    {
        float bend = Mathf.PerlinNoise(x * .86f + 47.2f, z * .86f + 16.9f);
        float seam = Mathf.PerlinNoise(x * 2.5f + bend * 2 + 126, z * 1.65f + bend * 1.3f + 72);
        float crumbs = Mathf.PerlinNoise(x * 5.9f + 31, z * 5.9f + 21);
        float fissure = 1 - Ease(.015f, .16f, Mathf.Abs(seam - .49f));
        float shelf = Mathf.Sin(y * 12 + bend * 8 + .62f * x - .4f * z);
        float collapse = Ease(.21f, .7f, Mathf.PerlinNoise(.97f*x + 9,.97f*z + 30));
        return (seam - .5f) * .39f + (crumbs - .5f) * .105f
             - fissure * collapse * .14f + shelf * collapse * .07f;
    }

    public static void Refine(Mesh mesh, Matrix4x4 toSurvey)
    {
        var local = mesh.vertices;
        var normals = mesh.normals;
        var colours = mesh.colors;
        var uv = mesh.uv;
        var fromSurvey = toSurvey.inverse;
        var toSurveyNormal = fromSurvey.transpose;
        var samples = new List<Sample>(local.Length);
        bool exposed = false;
        for (int i = 0; i < local.Length; i++)
        {
            Vector3 p = toSurvey.MultiplyPoint3x4(local[i]);
            Vector3 n = toSurveyNormal.MultiplyVector(normals[i]).normalized;
            float amount = Exposure(p,n);
            samples.Add(new Sample { position=p, normal=n, uv=uv[i], colour=colours[i], exposure=amount });
            exposed |= amount > .01f;
        }
        if (!exposed) return;

        var slots = new List<int>[mesh.subMeshCount];
        for (int i = 0; i < slots.Length; i++) slots[i] = new List<int>(mesh.GetTriangles(i));
        for (int level = 0; level < 2; level++)
        {
            var splits = new Dictionary<(int,int),int>();
            int Split(int i, int j)
            {
                var key = i < j ? (i,j) : (j,i);
                if (splits.TryGetValue(key, out int mid)) return mid;
                var a = samples[i]; var b = samples[j];
                float weight = (a.exposure + b.exposure) * .5f;
                if (weight < (level == 0 ? .035f : .20f)) return -1;
                Vector3 p = (a.position+b.position)*.5f;
                Vector3 n = (a.normal+b.normal).normalized;
                // Cubic midpoint follows the surveyed endpoint tangents across a crest.
                Vector3 chord=b.position-a.position;
                float lead = -(a.normal.x*chord.x+a.normal.z*chord.z)/Mathf.Max(.22f,a.normal.y);
                float tail = -(b.normal.x*chord.x+b.normal.z*chord.z)/Mathf.Max(.22f,b.normal.y);
                p.y += Mathf.Clamp((lead-tail)*.125f,-.10f,.10f)*weight;
                mid = samples.Count; splits[key]=mid;
                samples.Add(new Sample { position=p, normal=n, uv=(a.uv+b.uv)*.5f,
                                         colour=(a.colour+b.colour)*.5f, exposure=weight });
                return mid;
            }
            for (int sub = 0; sub < slots.Length; sub++)
            {
                var old=slots[sub]; var tris=new List<int>(old.Count*2);
                void Tri(int a,int b,int c) { tris.Add(a); tris.Add(b); tris.Add(c); }
                void One(int a,int b,int c,int m) { Tri(a,m,c); Tri(m,b,c); }
                void Two(int a,int b,int c,int ab,int bc) { Tri(ab,b,bc); Tri(a,ab,bc); Tri(a,bc,c); }
                for (int t=0; t<old.Count; t+=3)
                {
                    int a=old[t], b=old[t+1], c=old[t+2];
                    int ab=Split(a,b), bc=Split(b,c), ca=Split(c,a);
                    int mask=(ab>=0?1:0)|(bc>=0?2:0)|(ca>=0?4:0);
                    switch(mask) {
                        case 0: Tri(a,b,c); break;
                        case 1: One(a,b,c,ab); break;
                        case 2: One(b,c,a,bc); break;
                        case 4: One(c,a,b,ca); break;
                        case 3: Two(a,b,c,ab,bc); break;
                        case 6: Two(b,c,a,bc,ca); break;
                        case 5: Two(c,a,b,ca,ab); break;
                        default: Tri(a,ab,ca); Tri(ab,b,bc); Tri(ca,bc,c); Tri(ab,bc,ca); break;
                    }
                }
                slots[sub]=tris;
            }
        }
        var v=new Vector3[samples.Count]; var ns=new Vector3[samples.Count];
        var tex=new Vector2[samples.Count]; var paint=new Color[samples.Count];
        for (int i=0; i<samples.Count; i++)
        {
            var s=samples[i]; var p=s.position; var n=s.normal;
            if(s.exposure>.001f)
            {
                const float step=.07f;
                float offset=HeightOffset(p.x,p.y,p.z);
                float gx=(HeightOffset(p.x+step,p.y,p.z)-HeightOffset(p.x-step,p.y,p.z))/(2*step);
                float gz=(HeightOffset(p.x,p.y,p.z+step)-HeightOffset(p.x,p.y,p.z-step))/(2*step);
                p.y+=offset*s.exposure;
                n=(n-s.exposure*n.y*new Vector3(gx,0,gz)).normalized;
            }
            v[i]=fromSurvey.MultiplyPoint3x4(p);
            ns[i]=toSurvey.transpose.MultiplyVector(n).normalized;
            tex[i]=s.uv; paint[i]=s.colour;
        }
        mesh.Clear();
        if(v.Length>65535) mesh.indexFormat=IndexFormat.UInt32;
        mesh.vertices=v; mesh.normals=ns; mesh.uv=tex; mesh.colors=paint;
        mesh.subMeshCount=slots.Length;
        for(int i=0;i<slots.Length;i++) mesh.SetTriangles(slots[i],i,false);
        mesh.RecalculateBounds();
    }
}
