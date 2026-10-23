using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on the PLAYER (or anything that can be ganged up on). Enemies must hold a token
/// to start an attack on it, which caps simultaneous attackers and spaces out their timing.
///
/// Enemy usage:
///   if (tokens.TryAcquire(this)) combat.RequestAttack(attack);
///   // release when the swing finishes, or when staggered/killed:
///   combat.OnStateChanged += (from, to) => { if (to == CombatState.Idle || to == CombatState.Staggered || to == CombatState.Dead) tokens.Release(this); };
/// </summary>
public class AttackTokens : MonoBehaviour
{
    [Range(1, 5)] public int maxSimultaneousAttackers = 2;
    [Tooltip("Random gap between two attackers being granted tokens, so swings don't land in unison.")]
    public Vector2 gapBetweenGrants = new Vector2(0.35f, 0.9f);

    readonly HashSet<Object> holders = new HashSet<Object>();
    float nextGrantTime;

    public int ActiveAttackers => holders.Count;

    public bool TryAcquire(Object who)
    {
        if (holders.Contains(who)) return true;
        if (holders.Count >= maxSimultaneousAttackers || Time.time < nextGrantTime) return false;
        holders.Add(who);
        nextGrantTime = Time.time + Random.Range(gapBetweenGrants.x, gapBetweenGrants.y);
        return true;
    }

    public void Release(Object who) => holders.Remove(who);

    void LateUpdate() => holders.RemoveWhere(h => h == null); // destroyed enemies
}
