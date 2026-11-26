using System;
using System.Collections.Generic;
using UnityEngine;

// The fixed jamb and vault belong to the fort. Only the wood carried by this hinge breaks.
public sealed class OsowiecGateBreak : MonoBehaviour
{
    sealed class Piece
    {
        public Transform node;
        public Vector3 start, rest;
        public Quaternion finish;
        public float lift, duration;
    }
    readonly List<Piece> pieces = new();
    Mesh splinters;
    Light flash;
    float elapsed;

    internal static void Release(Transform hinge)
    {
        var leaf = hinge.Find("Doorleaf");
        if (!leaf) foreach (var r in hinge.GetComponentsInChildren<MeshRenderer>())
            if (r.name.StartsWith("Doorleaf", StringComparison.Ordinal)) { leaf = r.transform; break; }
        var collider = hinge.GetComponent<BoxCollider>();
        if (collider) collider.enabled = false;
        if (!leaf) return;
        var renderer = leaf.GetComponent<Renderer>();
        var wood = Array.Find(renderer.sharedMaterials, m => m && m.name.Contains("Timber"));
        if (!wood) wood = renderer.sharedMaterial;
        leaf.gameObject.SetActive(false);
        var effect = new GameObject("Обломки ворот").AddComponent<OsowiecGateBreak>();
        effect.transform.SetParent(hinge, false);
        effect.Create(wood);
    }

    void Create(Material material)
    {
        splinters = FractureMesh();
        var rng = new System.Random(742);
        float Range(float lo, float hi) => Mathf.Lerp(lo, hi, (float)rng.NextDouble());
        for (int i = 0; i < 26; ++i)
        {
            float z = .115f + (i % 13) * .212f;
            var node = new GameObject("Расколотая доска").transform;
            node.SetParent(transform, false);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = splinters;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            float length = Range(.65f, 1.23f);
            node.localScale = new Vector3(Range(.07f,.11f), length, Range(.115f,.20f));
            node.localPosition = new Vector3(0, i < 13 ? .58f : 1.72f, z);
            float side = z < 1.4f ? 0f : 2.8f;
            pieces.Add(new Piece {
                node = node, start = node.localPosition, lift = Range(.12f,.64f), duration = Range(.48f,.92f),
                rest = new Vector3(Range(-1.6f,2.2f), Range(.068f,.14f), Mathf.Lerp(side,z,.19f)),
                finish = Quaternion.Euler(Range(-8f,8f),Range(-33f,33f),Range(84f,94f))
            });
        }
        flash = new GameObject("Свет в проходе").AddComponent<Light>();
        flash.transform.SetParent(transform,false);
        flash.transform.localPosition = new Vector3(-.32f,1.12f,1.43f);
        flash.color = new Color(1,.63f,.26f);
        flash.shadows = LightShadows.None;
        flash.range = 5;
        flash.intensity = 9;
    }

    void Update()
    {
        if (GameManager.Instance && GameManager.Instance.IsPaused()) return;
        elapsed += Time.deltaTime;
        if (flash) {
            if (elapsed >= .12f) Destroy(flash.gameObject);
            else flash.intensity = 9 * (1 - elapsed / .12f);
        }
        foreach (var p in pieces) {
            float t = Mathf.Clamp01(elapsed / p.duration);
            p.node.localPosition = Vector3.Lerp(p.start,p.rest,t) + Vector3.up * (4 * p.lift * t * (1-t));
            p.node.localRotation = Quaternion.Slerp(Quaternion.identity,p.finish,t);
        }
        if (elapsed >= 1) { pieces.Clear(); enabled = false; }
    }

    // Jagged ends are part of the silhouette and remain visible once the dust settles.
    static Mesh FractureMesh()
    {
        Vector2[] edge = { new(-.5f,-.43f), new(-.14f,-.33f), new(.18f,-.49f), new(.5f,-.39f),
            new(.5f,.37f),new(.20f,.32f),new(-.03f,.50f),new(-.23f,.30f),new(-.5f,.46f) };
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        Vector3 Point(int j,float x) => new(x,edge[j].y,edge[j].x);
        void Triangle(Vector3 a,Vector3 b,Vector3 c) {
            foreach (var p in new[]{a,b,c}) {
                indices.Add(v.Count); v.Add(p); uv.Add(new Vector2(p.z*.23f + p.x*.14f + .5f,p.y + .51f));
            }
        }
        for(int j=0;j<edge.Length;j++) {
            int k=(j+1)%edge.Length;
            var a=Point(j,-.5f);var b=Point(k,-.5f);var c=Point(k,.5f);var d=Point(j,.5f);
            Triangle(Vector3.left*.5f,a,b); Triangle(Vector3.right*.5f,c,d);
            Triangle(a,d,c);Triangle(a,c,b);
        }
        var mesh=new Mesh{name="Щепа ворот"}; mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);
        mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
    }
    void OnDestroy() { if(splinters) Destroy(splinters); }
}
