using UnityEngine;

public enum OsowiecGroundSurface { Mud, Timber, Brick }

[DisallowMultipleComponent]
public sealed class OsowiecAcousticSurface : MonoBehaviour
{
    public OsowiecGroundSurface surface;
}
