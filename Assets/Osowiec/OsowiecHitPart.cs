using UnityEngine;

public enum OsowiecHitArea { Torso, Limb, Head, Helmet }

public sealed class OsowiecHitPart : MonoBehaviour
{
    public OsowiecHitArea area;
    public Mesh helmetMesh;

    Vector3[] vertices;
    int[] triangles;

    // The trigger is only a broad phase. Gaps under the brim use the original rendered triangles.
    internal bool IntersectShell(Ray worldRay, float maximum, out float distance, out Vector3 normal)
    {
        vertices ??= helmetMesh.vertices;
        triangles ??= helmetMesh.triangles;
        Vector3 start = transform.InverseTransformPoint(worldRay.origin);
        Vector3 direction = transform.InverseTransformVector(worldRay.direction);
        normal = Vector3.zero;
        distance = maximum;
        bool found = false;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 ab = vertices[triangles[i + 1]] - a;
            Vector3 ac = vertices[triangles[i + 2]] - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < .00000001f) continue;
            Vector3 offset = start - a;
            float u = Vector3.Dot(offset, p) / determinant;
            if (u < 0 || u > 1) continue;
            Vector3 q = Vector3.Cross(offset, ab);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1) continue;
            float along = Vector3.Dot(ac, q) / determinant;
            if (along <= 0 || along > distance) continue;
            found = true;
            distance = along;
            normal = transform.TransformDirection(Vector3.Cross(ab, ac).normalized);
            if (Vector3.Dot(normal, worldRay.direction) > 0) normal = -normal;
        }
        return found;
    }

    public void DropHelmet(Unit wearer, Vector3 direction)
    {
        if (area != OsowiecHitArea.Helmet || !gameObject.activeSelf) return;
        if (wearer.TryGetComponent<OsowiecHitBody>(out var hitBody)) hitBody.HelmetLost = true;
        var visible = GetComponent<MeshRenderer>();
        var loose = new GameObject("Сбитая каска");
        loose.layer = LayerMask.NameToLayer("Ignore Raycast");
        loose.transform.SetPositionAndRotation(transform.position, transform.rotation);
        loose.transform.localScale = transform.lossyScale;
        loose.AddComponent<MeshFilter>().sharedMesh = helmetMesh;
        loose.AddComponent<MeshRenderer>().sharedMaterials = visible.sharedMaterials;
        var shape = loose.AddComponent<BoxCollider>();
        shape.center = helmetMesh.bounds.center;
        shape.size = helmetMesh.bounds.size * .88f;
        foreach (var own in wearer.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(own, shape);
        var body = loose.AddComponent<Rigidbody>();
        body.mass = 1.1f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = direction.normalized * 1.65f + Vector3.up * 1.45f;
        body.angularVelocity = Vector3.Cross(direction.normalized, Vector3.up) * 6f;
        gameObject.SetActive(false);
        Destroy(loose, 14f);
    }
}
