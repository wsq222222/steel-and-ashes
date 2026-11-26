using System;

// Exertion is audible during an activation. Only an observation carries into the next turn.
internal sealed class OsowiecFieldTurn
{
    internal const float ObservationRange = 10f;
    internal bool Activated { get; set; }
    internal bool Observed { get; private set; }
    internal bool Attacked { get; private set; }
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
        bool observed = Observed;
        Observed = false;
        return observed;
    }

    internal static int ShotDamage(int damage, bool observed, float distance) =>
        observed && distance >= ObservationRange ? damage * 2 : damage;

    internal void Finish(bool observer)
    {
        if (Activated && observer && !Attacked) Observed = true;
        marched = enteredCombat = false;
        ContactNoticeUntil = 0;
        Activated = false;
        Attacked = false;
        distanceWalked = 0;
    }
}
