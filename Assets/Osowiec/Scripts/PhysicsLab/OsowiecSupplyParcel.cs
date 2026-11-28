using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class OsowiecSupplyParcel : MonoBehaviour
{
    [SerializeField] Collider receivingTray;
    Rigidbody body;
    Vector3 hookPosition;
    Quaternion hookRotation;
    bool lifting;

    public Rigidbody Body => body;
    public int TrayContacts { get; private set; }
    public int ZoneEnters { get; private set; }
    public int ZoneExits { get; private set; }
    public bool InZone { get; private set; }
    public bool Ready => body && body.isKinematic && !lifting;
    public bool CanLift => body && !body.isKinematic && !lifting;
    public float LastImpactSpeed { get; private set; }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        hookPosition = body.position;
        hookRotation = body.rotation;
    }

    public void DropParcel()
    {
        if (!Ready) return;
        body.isKinematic = false;
        body.WakeUp();
    }

    public void ResetParcel()
    {
        if (!CanLift) return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.WakeUp();
        lifting = true;
    }

    void FixedUpdate()
    {
        if (!lifting) return;
        Vector3 toHook = hookPosition - body.position;
        if (toHook.sqrMagnitude > .01f * .01f)
        {
            body.linearVelocity = Vector3.ClampMagnitude(toHook / Time.fixedDeltaTime, 2.5f);
            return;
        }
        // Change the body type only after it has physically left the sensor.
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.position = hookPosition;
        body.rotation = hookRotation;
        body.useGravity = true;
        lifting = false;
    }

    void OnCollisionEnter(Collision contact)
    {
        if (contact.collider != receivingTray) return;
        TrayContacts++;
        LastImpactSpeed = contact.relativeVelocity.magnitude;
    }

    internal void RecordZone(bool inside)
    {
        if (inside == InZone) return;
        InZone = inside;
        if (inside) ZoneEnters++;
        else ZoneExits++;
    }
}
