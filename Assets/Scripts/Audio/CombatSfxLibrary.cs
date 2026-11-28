using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Дефолтные боевые клипы (Resources/CombatSfxLibrary). Подставляются, если на Weapon/Tank поля пустые.
/// </summary>
[CreateAssetMenu(fileName = "CombatSfxLibrary", menuName = "Steel and Ashes/Combat SFX Library")]
public class CombatSfxLibrary : ScriptableObject
{
    [Header("Hound")]
    [FormerlySerializedAs("guardianFire")]
    public AudioClip scoutFire;
    [FormerlySerializedAs("guardianReload")]
    public AudioClip scoutReload;

    [Header("Пехота")]
    public AudioClip unitMove;

    [Header("Tank")]
    public AudioClip tankFire;
    public AudioClip tankReload;
    public AudioClip tankMove;

    private static CombatSfxLibrary instance;

    public static CombatSfxLibrary Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<CombatSfxLibrary>("CombatSfxLibrary");
            return instance;
        }
    }

    public AudioClip GetScoutFire() => scoutFire;
    public AudioClip GetScoutReload() => scoutReload;
    public AudioClip GetUnitMove() => unitMove;
    public AudioClip GetTankFire() => tankFire;
    public AudioClip GetTankReload() => tankReload;
    public AudioClip GetTankMove() => tankMove;
}
