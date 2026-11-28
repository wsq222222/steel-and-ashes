using UnityEngine;
using UnityEngine.InputSystem;

/// Drives the imported field-uniform rig from the actual unit state in both camera modes.
public sealed class OsowiecCastMotion : MonoBehaviour
{
    public Animator body;
    Unit unit;
    Vector3 previous;
    int health, ammo;
    string state;
    float hold;
    float idleOffset;
    OsowiecWardGait stairGait;
    Renderer[] visibleParts;
    bool insideView;
    bool previousMask;

    public string PerkName(ChessUnitType role) => role switch
    {
        ChessUnitType.Молот => "Пневмо-толчок",
        ChessUnitType.Циклоп => "Перископ",
        _ => "Рывок"
    };

    public string PerkDescription(ChessUnitType role) => role switch
    {
        ChessUnitType.Молот => "Зажмите Q, чтобы выбрать длину прыжка. Отпустите для прыжка или расчистки проволоки. Выстрел остаётся.",
        ChessUnitType.Циклоп => "Q открывает перископ. ЛКМ отмечает врага на два хода (+20% урона от винтовок). Проведите свой ход без движения и атаки: следующий выстрел с 10 м получит ×2. Предел урона — 84, по каске — 24.",
        _ => "Q добавляет 6 м. После выстрела Гончая может пройти оставшиеся метры. За ход доступен один выстрел."
    };

    void Start()
    {
        unit = GetComponent<Unit>();
        previous = transform.position;
        idleOffset = Mathf.Repeat(previous.x * .18f + previous.z * .31f, 1f);
        health = unit.GetHealth();
        ammo = unit.GetEquippedWeapon() != null ? unit.GetEquippedWeapon().AmmoInMag : 0;
        visibleParts = GetComponentsInChildren<Renderer>(true);
        // Record level shoe rotations before locomotion clips first rotate the ankles.
        if(body) stairGait=new OsowiecWardGait(transform,body,true);
    }

    void Update() => stairGait?.Reset();

    void LateUpdate()
    {
        if (unit == null || body == null) return;
        var cameras = CameraManager.Instance;
        bool firstPerson = cameras != null && cameras.IsActionMode() &&
            cameras.GetCurrentControlledUnit() == unit && unit.owner == Player.Player1;
        // Adjacent deployment cells can put a friendly backpack directly on the lens.
        // Only that nearby ally drops out of the first-person view. It reappears
        // after separation or a turn away, with hysteresis to avoid flicker.
        var controlled = cameras != null && cameras.IsActionMode() ? cameras.GetCurrentControlledUnit() : null;
        if (controlled != null && controlled != unit && controlled.owner == unit.owner &&
            unit.owner == Player.Player1 && unit.cameraAttachPoint != null)
        {
            Transform eye = cameras.GetActionCamera().transform;
            Vector3 offset = unit.cameraAttachPoint.position - eye.position;
            float until = insideView ? 1.46f : 1.17f;
            insideView = offset.sqrMagnitude < until*until &&
                (offset.sqrMagnitude < .12f || Vector3.Dot(eye.forward, offset.normalized) > (insideView ? .48f : .73f));
        }
        else insideView = false;
        foreach (var part in visibleParts)
            if (part != null) part.forceRenderingOff = firstPerson || insideView;

        var position = transform.position;
        Vector3 travel=position-previous;
        float speed = Vector3.ProjectOnPlane(travel, Vector3.up).magnitude / Mathf.Max(.001f, Time.deltaTime);
        previous = position;
        if (unit.GetHealth() <= 0 || Time.timeScale == 0) return;
        var weapon = unit.GetEquippedWeapon();
        var mag = weapon != null ? weapon.AmmoInMag : 0;
        var op = OsowiecOperation.Instance;
        bool mask = op && op.MaskOn(unit);
        string next;
        if (health > unit.GetHealth()) { next = "Hit"; hold = Time.time + .3f; }
        else if (mag < ammo) { next = "Fire"; hold = Time.time + .2f; }
        else if (mask != previousMask)
        { next = "Mask"; hold = Time.time + .6f; }
        else if (weapon != null && weapon.IsReloading) next = "Reload";
        else if (hold > Time.time) next = state;
        else if (op && op.Aiming(unit)) next = "Aim";
        else if (speed > .11f) next = speed >= (state == "Run" ? 2.35f : 2.65f) ? "Run" : "Walk";
        else if (firstPerson && Mouse.current != null && Mouse.current.rightButton.isPressed) next = "Aim";
        else next = "Idle";
        ammo = mag;
        previousMask = mask;
        health = unit.GetHealth();
        if (next == "Reload" && weapon != null)
        {
            body.speed = 1;
            body.Play("Base Layer.Reload", 0, weapon.ReloadPhase);
            state = next;
            return;
        }
        body.speed = next switch
        {
            "Walk" => Mathf.Clamp(speed / 1.3f, .60f, 1.7f),
            "Run" => Mathf.Clamp(speed / 2.5f, .90f, 1.8f),
            _ => 1
        };
        if (!string.IsNullOrEmpty(next) && state != next)
        {
            bool matchFoot = (next == "Walk" && state == "Run") || (next == "Run" && state == "Walk");
            if (matchFoot)
            {
                var footPhase = Mathf.Repeat(body.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
                body.CrossFade("Base Layer." + next, .15f, 0, footPhase);
            }
            else body.CrossFadeInFixedTime("Base Layer." + next, .09f, 0, next == "Idle" ? idleOffset : 0);
            state = next;
        }
        if ((next=="Walk" || next=="Run") && OsowiecGalleryStairs.OnFlight(position))
        {
            stairGait.Advance(travel,Time.deltaTime,false,body);
            stairGait.Apply();
        }
        else stairGait?.Stop();
    }
}
