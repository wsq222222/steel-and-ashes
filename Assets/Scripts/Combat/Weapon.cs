using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Оружие как prefab-компонент: хранит состояние (патроны/перезарядка/КД) и читает числа из RangedWeaponConfig.
/// </summary>
public class Weapon : MonoBehaviour
{
    [SerializeField] private RangedWeaponConfig config;
    [Tooltip("Дуло: вспышка и старт трассера. Луч урона задаётся в TryFire (у игрока — из камеры).")]
    [SerializeField] private Transform muzzle;

    [Header("VFX")]
    [SerializeField] private GameObject muzzleFlashVfxPrefab;
    [SerializeField] private GameObject tracerVfxPrefab;
    [SerializeField] private GameObject impactVfxPrefab;
    [Tooltip("Толщина LineRenderer трассера (если префаб трассера с LineRenderer).")]
    [SerializeField] private float tracerLineWidth = 0.06f;
    [Tooltip("Какие слои считаем 'поверхностями' для попадания/трассера. Если не трогать — будет Everything.")]
    [SerializeField] private LayerMask vfxRayMask = ~0;

    [Header("Звуки стрелкового оружия")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [Range(0f, 1f)] [SerializeField] private float fireSoundVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float reloadSoundVolume = 1f;
    [Tooltip("Пауза после выстрела, затем звук перезарядки (до автоперезарядки при пустом магазине).")]
    [SerializeField] private float postFireReloadSoundDelay = 0.4f;
    [Header("Camera shake (optional)")]
    [SerializeField] private float fireShakeDuration = 0.08f;
    [SerializeField] private float fireShakeMagnitude = 0.03f;

    [Header("Runtime State (read-only)")]
    [SerializeField] private int ammoInMag;
    [SerializeField] private int ammoReserve;
    [SerializeField] private bool isReloading;

    private float nextFireTime;
    private float reloadEndTime;
    private Unit ownerUnit;
    /// <summary>Если в префабе не проставили muzzle — ищем по имени под иерархией оружия.</summary>
    private Transform runtimeMuzzleCache;

    public float FireCooldownRemaining => Mathf.Max(0f, nextFireTime - Time.time);
    public bool IsOnFireCooldown => Time.time < nextFireTime;

    public RangedWeaponConfig Config => config;
    public Transform Muzzle => muzzle != null ? muzzle : ResolveRuntimeMuzzle();
    public int AmmoInMag => ammoInMag;
    public int AmmoReserve => ammoReserve;
    public bool IsReloading => isReloading;
    public float ReloadPhase => isReloading && config != null
        ? Mathf.Clamp01(1 - (reloadEndTime - Time.time) / Mathf.Max(.001f, config.reloadTimeSeconds)) : 0;
    public int MagazineSize => config != null ? config.magazineSize : 0;

    public void SetOwnerUnit(Unit unit)
    {
        ownerUnit = unit;
        if (gameObject.scene.name == "Osowiec") InitializeFromConfigIfNeeded();
    }

    private bool osowiecAmmoInitialized;

    public void InitializeFromConfigIfNeeded()
    {
        if (config == null) return;
        if (gameObject.scene.name == "Osowiec")
        {
            if (osowiecAmmoInitialized) return;
            osowiecAmmoInitialized = true;
        }
        if (ammoInMag == 0 && ammoReserve == 0)
        {
            ammoInMag = Mathf.Clamp(config.startingAmmoInMag, 0, config.magazineSize);
            ammoReserve = Mathf.Max(0, config.startingReserveAmmo);
        }
    }

    public bool TryStartReload(bool playSoundAtStart = true)
    {
        if (config == null) return false;
        if (isReloading) return false;
        if (ammoInMag >= config.magazineSize) return false;
        if (ammoReserve <= 0) return false;
        isReloading = true;
        reloadEndTime = Time.time + Mathf.Max(0f, config.reloadTimeSeconds);
        if (playSoundAtStart)
            PlayReloadSound();
        return true;
    }

    /// <summary>Автоперезарядка при пустом магазине (выстрел, бот, и т.д.).</summary>
    public void TryAutoReloadIfEmpty()
    {
        if (config == null || isReloading) return;
        if (ammoInMag > 0) return;
        TryStartReload();
    }

    public void TickReload()
    {
        if (!isReloading) return;
        if (Time.time < reloadEndTime) return;
        FinishReload();
    }

    private void FinishReload()
    {
        isReloading = false;
        if (config == null) return;
        int need = Mathf.Max(0, config.magazineSize - ammoInMag);
        int take = Mathf.Min(need, Mathf.Max(0, ammoReserve));
        ammoInMag += take;
        ammoReserve -= take;
    }

    public bool CanFire()
    {
        if (config == null) return false;
        if (isReloading) return false;
        if (ammoInMag <= 0) return false;
        return Time.time >= nextFireTime;
    }

    /// <summary>
    /// Hitscan-выстрел. origin/direction — луч попадания/урона (у игрока с action-камеры).
    /// Трассер рисуется от <see cref="muzzle"/> (если задан) до точки попадания этого луча.
    /// </summary>
    public bool TryFire(Unit owner, Vector3 origin, Vector3 direction)
    {
        if (!OsowiecFireTurn.Allows(owner)) return false;
        if (gameObject.scene.name == "Osowiec" && owner.FieldTurn.Attacked)
            return false;
        return ResolveShot(owner, origin, direction);
    }

    // A reaction is a single authorised shot at the point recorded when the warning began.
    internal bool TryFirePrepared(Unit shooter, Unit target, Vector3 aim, bool scattered)
    {
        var op = OsowiecOperation.Instance;
        if (!op || !op.CanReleaseAim(shooter, target)) return false;
        Vector3 origin = Muzzle ? Muzzle.position : shooter.transform.position + Vector3.up * 1.2f;
        return ResolveShot(shooter, origin, (aim - origin).normalized, scattered);
    }

    private bool ResolveShot(Unit owner, Vector3 origin, Vector3 direction, bool scattered = false)
    {
        if (OsowiecFieldKit.Busy(owner) || (owner.IsControlled() && OsowiecFieldKit.ScopeOpen)) return false;
        ownerUnit = owner;
        InitializeFromConfigIfNeeded();
        TickReload();

        if (!CanFire())
        {
            TryAutoReloadIfEmpty();
            return false;
        }

        float interval = config.FireIntervalSeconds;
        if (owner.IsControlled()) OsowiecFieldKit.Fired(owner);
        nextFireTime = Time.time + Mathf.Max(0f, interval);

        float maxRange = Mathf.Max(0.1f, config.maxHitRangeMeters);
        bool friendly = config.friendlyFire;
        bool fieldMission = gameObject.scene.name == "Osowiec";
        bool alliedFieldShot = fieldMission && owner.owner == Player.Player1;
        bool observed = fieldMission && owner.FieldTurn.Fire();
        int baseDamage = Mathf.Max(0, config.damagePerHit);

        Transform muzzleXf = OsowiecWeaponView.VisibleMuzzle(owner);
        if (muzzleXf == null) muzzleXf = Muzzle;
        if (gameObject.scene.name != "Osowiec" || !muzzleXf || !OsowiecShotEffects.AtMuzzle(muzzleXf.position,direction))
            SpawnMuzzleFlash();
        PlayFireSound();
        if (owner.owner == Player.Player1 && OsowiecOperation.Instance)
            OsowiecOperation.Instance.HearShot(owner.transform.position);
        DoCameraShake();


        bool hasHit = false;
        Vector3 hitPoint = origin + direction * maxRange;
        Vector3 hitNormal = -direction;

        // Один raycast "по миру": даёт и точку попадания (стены/пол/юниты), и не позволяет стрелять сквозь коллайдеры.
        LayerMask rayMask = vfxRayMask.value != 0 ? vfxRayMask : ~0;
        OsowiecHitPart part = null;
        Unit targetHit = null;
        float damageDealt = 0;
        RaycastHit hit;
        if (fieldMission) Physics.SyncTransforms();
        if (fieldMission
            ? OsowiecHitBody.Trace(owner, new Ray(origin, direction.normalized), maxRange, rayMask, out hit, out part)
            : Physics.Raycast(origin, direction, out hit, maxRange, rayMask, QueryTriggerInteraction.Ignore))
        {
            hasHit = true;
            hitPoint = hit.point;
            hitNormal = hit.normal;
            if (alliedFieldShot) OsowiecOperation.Instance?.HearImpact(hitPoint, owner.transform.position);
            Unit target = hit.collider != null ? hit.collider.GetComponentInParent<Unit>() : null;
            if (target != null && target != owner)
            {
                bool isEnemy = target.owner != owner.owner;
                if (!scattered && (isEnemy || friendly))
                {
                    targetHit = target;
                    int before = Mathf.Max(0, target.GetHealth());
                    var zone = part ? part.area : OsowiecHitArea.Torso;
                    int finalDamage = fieldMission
                        ? OsowiecFieldTurn.ShotDamage(baseDamage, zone, observed, hit.distance,
                            isEnemy && OsowiecFieldKit.IsIdentified(target), owner.GetDamageMultiplier())
                        : Mathf.RoundToInt(baseDamage * owner.GetDamageMultiplier());
                    if (part && zone == OsowiecHitArea.Helmet)
                        part.DropHelmet(target, direction);
                    target.TakeDamage(finalDamage, owner);
                    damageDealt = Mathf.Max(0, before - Mathf.Max(0, target.GetHealth()));
                }
            }
            else
            {
                if (target == null && gameObject.scene.name == "Osowiec")
                    OsowiecAtmosphere.PlayImpact(hit.collider, hit.point);
                IDamageableTarget damageable = FindDamageableInParents(hit.collider);
                if (damageable != null && damageable.IsAlive && damageable.Owner != owner.owner)
                {
                    int finalDamage = Mathf.RoundToInt(baseDamage * owner.GetDamageMultiplier());
                    damageable.ApplyDamage(finalDamage, owner);
                }
            }
        }

        // Визуал: луч от дула; урон — по origin/direction (у игрока это камера).
        Vector3 tracerStart = muzzleXf != null ? muzzleXf.position : origin;
        if (alliedFieldShot) OsowiecFieldKit.ReportPlayerShot(owner,targetHit,Mathf.RoundToInt(damageDealt));
        if (fieldMission && owner.owner == Player.Player2)
            OsowiecEnemyFireReadout.Report(owner, targetHit, tracerStart, hitPoint, damageDealt);
        if (OsowiecOperation.Instance != null) OsowiecStressEffects.PassBy(owner, tracerStart, hitPoint);
        SpawnTracer(tracerStart, hitPoint);
        if (hasHit && (gameObject.scene.name != "Osowiec" || !OsowiecShotEffects.AtSurface(hit)))
            SpawnImpact(hitPoint, hitNormal);

        ammoInMag = Mathf.Max(0, ammoInMag - 1);

        // Звук перезарядки после выстрела — только у Гончей и танка. Циклоп перезаряжается по R или при пустом магазине.
        if (owner != null && owner.chessType == ChessUnitType.Hound && ownerUnit != null)
            ownerUnit.SchedulePostFireWeaponAudio(this, postFireReloadSoundDelay);
        else
            TryAutoReloadIfEmpty();

        return true;
    }

    public float GetPostFireReloadSoundDelay() => Mathf.Max(0.05f, postFireReloadSoundDelay);

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashVfxPrefab == null) return;
        Transform t = Muzzle != null ? Muzzle : transform;
        GameObject go = Instantiate(muzzleFlashVfxPrefab, t.position, t.rotation);
        AutoDestroyVfx(go);
    }

    private Transform ResolveRuntimeMuzzle()
    {
        if (runtimeMuzzleCache != null) return runtimeMuzzleCache;
        runtimeMuzzleCache = FindChildTransformByName(transform, "muzzle")
            ?? FindChildTransformByName(transform, "Muzzle");
        return runtimeMuzzleCache;
    }

    private static Transform FindChildTransformByName(Transform root, string exactName)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName) return t;
        }
        return null;
    }

    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        if (tracerVfxPrefab == null) return;
        Vector3 dir = to - from;
        Quaternion rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;

        // Без позиции в Instantiate: у ParticleSystem с playOnAwake первый кадр иначе может симулироваться в (0,0,0).
        GameObject go = Instantiate(tracerVfxPrefab);
        Transform tr = go.transform;
        tr.SetPositionAndRotation(from, rotation);

        if (go.TryGetComponent(out LineRenderer lr))
        {
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            float w = Mathf.Max(0.001f, tracerLineWidth);
            lr.startWidth = w;
            lr.endWidth = w;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            AutoDestroyVfx(go, lr);
            return;
        }

        AutoDestroyVfx(go);
    }

    private void SpawnImpact(Vector3 point, Vector3 normal)
    {
        if (impactVfxPrefab == null) return;
        Quaternion rot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        GameObject go = Instantiate(impactVfxPrefab, point, rot);
        AutoDestroyVfx(go);
    }

    private void DoCameraShake()
    {
        if (fireShakeDuration <= 0f || fireShakeMagnitude <= 0f) return;
        if (CameraShake.Instance == null) return;
        if (gameObject.scene.name == "Osowiec" && OsowiecFieldSettings.ReducedShake) return;
        CameraShake.Instance.Shake(fireShakeDuration, fireShakeMagnitude);
    }

    private void PlayFireSound()
    {
        PlayCombatClip(ResolveFireSound(), fireSoundVolume);
    }

    private void PlayReloadSound()
    {
        PlayCombatClip(ResolveReloadSound(), reloadSoundVolume);
    }

    private AudioClip ResolveFireSound()
    {
        if (gameObject.scene.name == "Osowiec")
            return OsowiecSoundBank.Variant(OsowiecAtmosphere.IsInAudioVault(transform.position)
                ? "VaultShot" : "FieldShot", 3);
        if (fireSound != null) return fireSound;

        CombatSfxLibrary lib = CombatSfxLibrary.Instance;
        if (ownerUnit != null && lib != null)
        {
            if (ownerUnit.chessType == ChessUnitType.Hound && lib.GetScoutFire() != null)
                return lib.GetScoutFire();
        }

        return ownerUnit != null ? ownerUnit.GetObserverFireSoundFallback() : null;
    }

    private AudioClip ResolveReloadSound()
    {
        if (gameObject.scene.name == "Osowiec") return OsowiecSoundBank.Clip("Reload");
        if (reloadSound != null) return reloadSound;

        CombatSfxLibrary lib = CombatSfxLibrary.Instance;
        if (ownerUnit != null && lib != null)
        {
            if (ownerUnit.chessType == ChessUnitType.Hound && lib.GetScoutReload() != null)
                return lib.GetScoutReload();
        }

        return ownerUnit != null ? ownerUnit.GetObserverReloadSoundFallback() : null;
    }

    /// <summary>Звук перезарядки после выстрела (вызывается из Unit).</summary>
    public void PlayPostFireReloadSound()
    {
        if (gameObject.scene.name == "Osowiec" && ammoInMag > 0)
            PlayCombatClip(OsowiecSoundBank.Clip("Cycle"), reloadSoundVolume);
        else PlayReloadSound();
    }

    /// <summary>Тихая перезарядка после того, как SFX уже проигран.</summary>
    public void TryStartReloadAfterPostFireAudio()
    {
        if (ammoInMag > 0) return;
        TryStartReload(playSoundAtStart: false);
    }

    private void PlayCombatClip(AudioClip clip, float volumeScale)
    {
        if (clip == null || volumeScale <= 0f) return;
        Unit host = ownerUnit;
        if (host == null) return;
        AudioSource src = host.GetCombatAudioSource();
        if (src == null) return;
        if (gameObject.scene.name == "Osowiec")
        {
            src.outputAudioMixerGroup = OsowiecSoundBank.Bus("World");
            bool firstPerson = CameraManager.Instance != null && CameraManager.Instance.IsActionMode() &&
                CameraManager.Instance.GetCurrentControlledUnit() == host;
            src.spatialBlend = firstPerson ? 0f : 1f;
            src.minDistance = 3f;
            src.maxDistance = 65f;
            src.dopplerLevel = 0f;
        }
        float vol = (AudioManager.Instance != null ? AudioManager.Instance.SFXVolume : 1f) * volumeScale;
        src.PlayOneShot(clip, vol);
    }

    private static void AutoDestroyVfx(GameObject go, LineRenderer line = null)
    {
        if (go == null) return;
        float ttl = 0.12f;
        if (line != null)
            ttl = 0.12f;
        else
        {
            ParticleSystem ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                ttl = Mathf.Max(0.1f, main.duration + main.startLifetime.constantMax);
            }
            else
                ttl = 2.5f;
        }
        Destroy(go, ttl);
    }

    private static IDamageableTarget FindDamageableInParents(Collider collider)
    {
        if (collider == null) return null;
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IDamageableTarget damageable)
                return damageable;
        }

        return null;
    }

    /// <summary>Утилита: подобрать origin на NavMesh/с поверхности, если нужно.</summary>
    public static Vector3 SafeOrigin(Vector3 origin)
    {
        if (NavMesh.SamplePosition(origin, out NavMeshHit hit, 1.0f, NavMesh.AllAreas))
            return hit.position + Vector3.up * 0.05f;
        return origin;
    }
}
