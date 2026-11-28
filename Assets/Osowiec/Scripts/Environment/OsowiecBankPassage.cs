using UnityEngine;
using UnityEngine.AI;

// The physical boom reserves the dry track for a dispatched wagon and its escorts.
public sealed class OsowiecBankPassage : MonoBehaviour
{
    public Transform boom;
    public NavMeshObstacle pedestrianBlock;
    readonly Collider[] people = new Collider[32];
    float angle;
    void Awake() => pedestrianBlock.enabled=true;

    void Update()
    {
        var column=OsowiecOperation.Instance?OsowiecOperation.Instance.relief:null;
        if(!column || !boom)return;
        // The wagon preplans the entire road at dispatch, after the battery requests relief.
        pedestrianBlock.enabled=!column.Started;
        Vector3 d=column.CartPosition-transform.position;
        bool approach=column.UsesNorthRoad && Mathf.Abs(d.x)<6 && Mathf.Abs(d.z)<3.25f;
        if(!approach && angle>0 && Occupied())approach=true;
        SetAngle(Mathf.MoveTowards(angle,approach?88:0,90*Time.deltaTime));
    }

    bool Occupied()
    {
        var center=transform.position+new Vector3(0,.91f,1.76f);
        int count=Physics.OverlapBoxNonAlloc(center,new Vector3(.70f,.89f,1.94f),people,
            Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
            if(people[i].GetComponentInParent<Unit>())return true;
        return false;
    }

    void SetAngle(float degrees)
    {
        angle=degrees;
        boom.localRotation=Quaternion.Euler(-angle,0,0);
    }

#if UNITY_EDITOR
    public void SetInspectionPose(bool open) {SetAngle(open?88:0);Physics.SyncTransforms();}
#endif
}
