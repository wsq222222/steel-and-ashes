using System.Collections;
using UnityEngine;

// The preview and the released prop share the sampled path, including obstacles above the reticle.
internal sealed class OsowiecKitThrow
{
    const int Segments=60;
    readonly Vector3[] points=new Vector3[Segments+1];
    readonly RaycastHit[] hits=new RaycastHit[48];
    int count;
    public Vector3 Landing => count > 0 ? points[count-1] : default;
    public GameObject InFlight { get; private set; }
    public Vector3 LastImpact { get; private set; }
    public int Impacts { get; private set; }
    float duration;

    bool Cast(Vector3 start, Vector3 travel, float radius, Unit owner, out Vector3 contact)
    {
        contact=start+travel;
        float distance=travel.magnitude;
        if(distance<.001f) return false;
        var direction=travel/distance;
        int total=radius>0 ? Physics.SphereCastNonAlloc(start,radius,direction,hits,distance,~0,QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(start,direction,hits,distance,~0,QueryTriggerInteraction.Ignore);
        bool found=false;
        for(int i=0;i<total;i++)
        {
            var h=hits[i];
            if(h.distance>distance || !h.collider || owner && h.transform.IsChildOf(owner.transform)) continue;
            distance=h.distance;
            contact=start+direction*distance;
            found=true;
        }
        return found;
    }

    const float SampleSeconds = .08f;

    public void Aim(Camera eye, Unit owner, float charge)
    {
        Vector3 start = OsowiecWeaponView.ThrowOrigin(owner) ??
            eye.transform.TransformPoint(new Vector3(.12f, -.12f, .48f));
        // These are game-space tuning values. The preview, release and moving prop follow the same samples.
        Vector3 forward = eye.transform.forward;
        Vector3 velocity = forward * Mathf.Lerp(5f, 16f, charge) + Vector3.up * Mathf.Lerp(2.4f, 5f, charge);
        points[0] = start;
        count = 1;
        duration = 0;
        for (int i = 1; i <= Segments; i++)
        {
            float time = i * SampleSeconds;
            var sample = start + velocity * time + Vector3.down * (6.75f * time * time);
            Vector3 previous = points[count - 1];
            bool blocked = Cast(previous, sample - previous, .055f, owner, out var end);
            points[count++] = end;
            duration += SampleSeconds;
            if (blocked) break;
        }
    }

    public IEnumerator Fly(OsowiecKitItem item,Unit owner,Material particle, System.Action<OsowiecKitItem,Unit,Vector3> onImpact)
    {
        // A new actor or a fresh aim preview may reuse the buffer while this prop is airborne.
        var route=new Vector3[count];
        System.Array.Copy(points,route,count);
        float flightSeconds=duration;
        var prop=OsowiecKitProps.Create(item,null,owner);
        prop.name="Летящий предмет · "+OsowiecKitWheel.Name(item);
        InFlight=prop;
        prop.transform.position=route[0];
        prop.transform.localScale*=1.25f;
        if(particle)
        {
            var trail=prop.AddComponent<TrailRenderer>();
            trail.sharedMaterial=particle;
            trail.time=.24f;
            trail.minVertexDistance=.03f;
            trail.startWidth=.085f;
            trail.endWidth=.015f;
            trail.startColor=item is OsowiecKitItem.Smoke or OsowiecKitItem.Gas ? new Color(.81f,.85f,.77f,.92f) : new Color(1,.75f,.39f,.95f);
            trail.endColor=new Color(.65f,.60f,.48f,0);
        }
        Vector3 last=route[0];
        Quaternion initial=Quaternion.Euler(10,25,-17);
        int nextKnot=1;
        for(float t=0;t<1;)
        {
            t=Mathf.Min(1,t+Time.deltaTime/flightSeconds);
            float sample=t*(route.Length-1);
            int index=Mathf.Min(Mathf.FloorToInt(sample),route.Length-2);
            var next=Vector3.Lerp(route[index],route[index+1],sample-index);
            bool blocked=false;
            while(nextKnot<route.Length && nextKnot<=sample && !blocked)
            {
                var knot=route[nextKnot++];
                blocked=Cast(last,knot-last,.055f,owner,out last);
            }
            if(!blocked) blocked=Cast(last,next-last,.055f,owner,out last);
            prop.transform.SetPositionAndRotation(last,initial*Quaternion.Euler(430*t,55*t,210*t));
            if(blocked) break;
            yield return null;
        }
        if(InFlight==prop) InFlight=null;
        Object.Destroy(prop);
        Impacts++;
        LastImpact=last;
        onImpact(item,owner,last);
    }

    public void DrawAim(Camera eye) => DrawPath(eye, points, count);

    internal static void DrawPath(Camera eye, Vector3[] samples, int count)
    {
        if(count<2) return;
        float s=OsowiecHudPaint.Scale;
        Color ink=new(.065f,.075f,.06f,.8f);
        Color cream=new(.97f,.85f,.48f,.94f);
        for(int i=2;i<count-1;i+=2)
        {
            var p=eye.WorldToScreenPoint(samples[i]);
            if(p.z<eye.nearClipPlane) continue;
            var dot=new Rect(p.x-2*s,Screen.height-p.y-2*s,4*s,4*s);
            OsowiecHudPaint.Fill(new Rect(dot.x-s,dot.y-s,6*s,6*s),ink);
            OsowiecHudPaint.Fill(dot,cream);
        }
        var hit=eye.WorldToScreenPoint(samples[count-1]);
        if(hit.z<=eye.nearClipPlane) return;
        Vector2 center=new(hit.x,Screen.height-hit.y);
        var before=GUI.matrix;
        GUIUtility.RotateAroundPivot(45,center);
        for(int i=0;i<4;i++)
        {
            var line=new Rect(center.x-7*s,center.y-8*s,14*s,2*s);
            OsowiecHudPaint.Fill(new Rect(line.x-s,line.y-s,16*s,4*s),ink);
            OsowiecHudPaint.Fill(line,cream);
            GUIUtility.RotateAroundPivot(90,center);
        }
        GUI.matrix=before;
    }
}

