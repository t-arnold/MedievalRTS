using System;
using UnityEngine;

/// <summary>
/// Experience for NPC soldiers: they earn XP by landing hits (swings, arrows, charges), breaking guards,
/// parrying, bracing against cavalry and killing, and get promoted up the skill ladder
/// (Levy -> ... -> Champion). Survivors of big battles become veterans.
/// The player has no tiers: the player's skill is you.
/// </summary>
[RequireComponent(typeof(CombatStateMachine))]
public class CombatExperience : MonoBehaviour
{
    /// <summary>Turned off by the duel tester so tiers stay fixed during measurements.</summary>
    public static bool GainEnabled = true;

    public CombatSkill[] ladder;
    public int xp;

    [Header("XP rewards")]
    public int perHit = 2;
    public int perCounterHit = 3;
    public int perGuardBreak = 3;
    public int perBlock = 1;
    public int perParry = 4;
    public int perKill = 10;

    public int TierIndex { get; private set; }
    public CombatSkill Tier => ladder != null && ladder.Length > 0 ? ladder[TierIndex] : null;

    public event Action<CombatSkill> OnPromoted;

    CombatStateMachine csm;
    UnitBrain ai;

    void Awake()
    {
        csm = GetComponent<CombatStateMachine>();
        ai = GetComponent<UnitBrain>();
    }

    void Start()
    {
        if (ladder == null || ladder.Length == 0) ladder = DefaultSkills.Ladder;
        if (!ai) ai = GetComponent<UnitBrain>();
        int i = ai && ai.skill ? Array.IndexOf(ladder, ai.skill) : 0;
        TierIndex = Mathf.Max(0, i);
        xp = Mathf.Max(xp, ladder[TierIndex].xpToReach);
    }

    void OnEnable()
    {
        csm.OnHitLanded += Landed;
        csm.OnHitReceived += Received;
    }

    void OnDisable()
    {
        csm.OnHitLanded -= Landed;
        csm.OnHitReceived -= Received;
    }

    void Landed(HitResult r)
    {
        if (r.defender && r.defender.team == csm.team) return; // no XP for hitting friends
        int gain = r.type switch
        {
            HitType.Hit => perHit,
            HitType.CounterHit => perCounterHit,
            HitType.GuardBroken => perGuardBreak,
            _ => 0,
        };
        if (r.defender && r.defender.Stats.IsDead) gain += perKill;
        Add(gain);
    }

    void Received(HitResult r)
    {
        if (r.type == HitType.Parried) Add(perParry);
        else if (r.type == HitType.Blocked) Add(perBlock);
    }

    public void Add(int amount)
    {
        if (!GainEnabled || amount <= 0 || ladder == null || ladder.Length == 0) return;
        if (csm.State == CombatState.Dead) return;
        xp += amount;
        while (TierIndex + 1 < ladder.Length && xp >= ladder[TierIndex + 1].xpToReach)
        {
            TierIndex++;
            if (ai) ai.ApplySkill(ladder[TierIndex]);
            OnPromoted?.Invoke(ladder[TierIndex]);
        }
    }
}
