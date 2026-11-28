using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public sealed class OsowiecReceivingZone : MonoBehaviour
{
    [SerializeField] OsowiecSupplyParcel parcel;

    void OnTriggerEnter(Collider other)
    {
        if (other.attachedRigidbody == parcel.Body) parcel.RecordZone(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.attachedRigidbody == parcel.Body) parcel.RecordZone(false);
    }
}
