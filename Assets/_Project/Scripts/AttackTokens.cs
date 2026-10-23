using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on anything that can be ganged up on (TestArena adds it to everyone).
/// - Attack tokens: an attacker must hold one to swing, which caps simultaneous attackers and
///   spaces out their timing.
/// - Engagement count: how many AIs currently have this as their target, so AIs can spread out
///   across targets instead of all piling onto one (e.g. the player).
/// </summary>
public class AttackTokens : MonoBehaviour
{
    [Range(1, 5)] public int maxSimultaneousAttackers = 2;
    [Tooltip("Random gap between two attackers being granted tokens, so swings don't land in unison.")]
    public Vector2 gapBetweenGrants = new Vector2(0.35f, 0.9f);

    readonly HashSet<Object> holders = new HashSet<Object>();
    readonly HashSet<Object> engaged = new HashSet<Object>();
    float nextGrantTime;

    public int ActiveAttackers => holders.Count;
    public int EngagedCount => engaged.Count;

    public bool TryAcquire(Object who)
    {
        if (holders.Contains(who)) return true;
        if (holders.Count >= maxSimultaneousAttackers || Time.time < nextGrantTime) return false;
        holders.Add(who);
        nextGrantTime = Time.time + Random.Range(gapBetweenGrants.x, gapBetweenGrants.y);
        return true;
    }

    public void Release(Object who) => holders.Remove(who);

    public void Engage(Object who) => engaged.Add(who);
    public void Disengage(Object who) => engaged.Remove(who);

    void LateUpdate()
    {
        holders.RemoveWhere(h => h == null); // destroyed attackers
        engaged.RemoveWhere(h => h == null);
    }
}
