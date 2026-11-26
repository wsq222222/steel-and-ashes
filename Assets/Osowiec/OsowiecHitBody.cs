using UnityEngine;

// A soldier's controller capsule handles locomotion. The smaller moving volumes register shots.
public sealed class OsowiecHitBody : MonoBehaviour
{
    internal bool HelmetLost { get; set; }

    public void SettleFall()
    {
        // Once locomotion is disabled the animated body may tilt with its own patch of ramp.
        // Query from standing height so vaulted galleries retain their interior floor.
        Vector3 pos = transform.position;
        if (!TryFloor(pos, out var centre)) return;
        Vector3 fore = transform.forward * .62f;
        Vector3 side = transform.right * .34f;
        if (!TryFloor(pos + fore, out var front) || !TryFloor(pos - fore, out var back) ||
            !TryFloor(pos + side, out var right) || !TryFloor(pos - side, out var left)) return;
        Vector3 up = Vector3.Cross(front-back, right-left).normalized;
        if (up.y < .65f) return;
        transform.SetPositionAndRotation(new Vector3(pos.x, centre.y + .015f, pos.z),
            Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, up), up));
    }

    internal static bool TryFloor(Vector3 expected, out Vector3 floor)
    {
        float closest = float.PositiveInfinity;
        floor = default;
        foreach(var hit in Physics.RaycastAll(expected+Vector3.up*.85f, Vector3.down, 1.7f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < .52f || hit.collider.GetComponentInParent<Unit>()) continue;
            float difference = Mathf.Abs(hit.point.y - expected.y);
            if (difference >= closest) continue;
            closest = difference;
            floor = hit.point;
        }
        return !float.IsPositiveInfinity(closest);
    }

    public static bool Trace(Unit shooter, Ray ray, float range, int layerMask,
        out RaycastHit nearest, out OsowiecHitPart part)
    {
        nearest = default;
        part = null;
        bool found = false;
        float best = range;
        foreach (var raw in Physics.RaycastAll(ray, range, layerMask, QueryTriggerInteraction.Collide))
        {
            if (raw.distance > best) continue;
            var hit = raw;
            var shape = hit.collider;
            var zone = shape.GetComponent<OsowiecHitPart>();
            var unit = shape.GetComponentInParent<Unit>();
            if (unit == shooter || (shape.isTrigger && !zone)) continue;
            if (!zone && unit && unit.TryGetComponent<OsowiecHitBody>(out _)) continue;
            if (shape.gameObject.layer == LayerMask.NameToLayer("Ignore Raycast") && !zone) continue;
            if (zone && zone.area == OsowiecHitArea.Helmet)
            {
                if (!zone.IntersectShell(ray, best, out float contact, out Vector3 normal)) continue;
                hit.distance = contact;
                hit.point = ray.GetPoint(contact);
                hit.normal = normal;
            }
            best = hit.distance;
            nearest = hit;
            part = zone;
            found = true;
        }
        return found;
    }
}
