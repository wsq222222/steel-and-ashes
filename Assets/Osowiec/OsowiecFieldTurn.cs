using System;

// Exertion is audible during an activation. Only an observation carries into the next turn.
internal sealed class OsowiecFieldTurn
{
    internal const float ObservationRange = 10f;
    internal const float SprintBonus = 6f;
    internal const int ShotCap = 84;
    internal bool Activated { get; set; }
    internal bool Observed { get; private set; }
    internal bool Attacked { get; private set; }
    internal bool RifleSpent { get; private set; }
    float distanceWalked;
    bool marched, enteredCombat;
    internal float ContactNoticeUntil { get; private set; }

    internal bool EnterContact(bool free, float now)
    {
        if (free) marched = true;
        if (free || !marched || enteredCombat) return false;
        enteredCombat = true;
        ContactNoticeUntil = now + 3f;
        return true;
    }

    internal float Breath(float full) => Math.Min(1f, distanceWalked / Math.Max(1f, full));
    internal void Walk(float metres) => distanceWalked += metres;

    internal void Attack() => Attacked = true;

    internal bool Fire()
    {
        Attack();
        RifleSpent = true;
        bool observed = Observed;
        Observed = false;
        return observed;
    }

    internal static int ShotDamage(int damage, OsowiecHitArea zone, bool observed, float distance,
        bool marked, float attackerMultiplier)
    {
        float location = zone switch
        {
            OsowiecHitArea.Helmet => .30f,
            OsowiecHitArea.Head => 1.35f,
            OsowiecHitArea.Limb => .72f,
            _ => 1f
        };
        float observation = observed && distance >= ObservationRange ? 2f : 1f;
        float mark = marked ? 1.2f : 1f;
        int amount = UnityEngine.Mathf.RoundToInt(damage * location * observation * mark * attackerMultiplier);
        return UnityEngine.Mathf.Clamp(amount, 0, zone == OsowiecHitArea.Helmet ? 24 : ShotCap);
    }

    internal void Finish(bool observer)
    {
        if (Activated) Observed = observer && !Attacked && distanceWalked <= .02f;
        marched = enteredCombat = false;
        ContactNoticeUntil = 0;
        Activated = false;
        Attacked = RifleSpent = false;
        distanceWalked = 0;
    }
}
