using UnityEngine;

/// <summary>
/// An experience tier (Levy, Veteran, ...). Mostly changes HOW a soldier fights (reactions, reads,
/// timing, judgement), with only modest stat bumps, so skill is something you can SEE and learn to beat.
/// Create via Assets > Create > Combat > Skill Tier, or Tools > Combat > Create Default Skill Tiers.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Skill Tier", fileName = "NewSkillTier")]
public class CombatSkill : ScriptableObject
{
    [Header("Identity")]
    public string tierName = "Soldier";
    [Range(0, 5)] public int rankPips = 2;
    [Tooltip("Experience needed to reach this tier (0 for the first tier).")]
    public int xpToReach = 40;

    [Header("Body (modest multipliers)")]
    public float healthMultiplier = 1f;
    public float staminaMultiplier = 1f;
    public float poiseMultiplier = 1f;
    public float damageMultiplier = 1f;
    public float moveSpeedMultiplier = 1f;

    [Header("Perception & reaction")]
    [Range(0f, 1f), Tooltip("Chance to notice an incoming telegraphed attack at all.")]
    public float readChance = 0.7f;
    [Tooltip("Seconds between noticing an attack and acting on it (min, max).")]
    public Vector2 reactionTime = new Vector2(0.22f, 0.4f);
    [Tooltip("± seconds of error on block/parry/dodge timing.")]
    public float timingError = 0.08f;

    [Header("Defence choices (after reading an attack)")]
    [Range(0f, 1f)] public float blockChance = 0.6f;
    [Range(0f, 1f), Tooltip("Of blocks, how often they try to time a parry.")]
    public float parryChance = 0.12f;
    [Range(0f, 1f), Tooltip("Dodge chance vs blockable attacks they chose not to block.")]
    public float dodgeChance = 0.2f;
    [Range(0f, 1f)] public float dodgeUnblockableChance = 0.6f;

    [Header("Offence & judgement")]
    [Range(0f, 1f), Tooltip("Chance to attack into an opponent's recovery or stagger.")]
    public float punishChance = 0.4f;
    [Range(0f, 1f), Tooltip("Chance to pick the right attack for the situation instead of a random one.")]
    public float tacticalChance = 0.4f;
    [Tooltip("Swing when distance <= reach × this (min, max). Above 1 = swings from out of range and whiffs.")]
    public Vector2 rangeJudgement = new Vector2(0.7f, 1.05f);
    [Tooltip("Degrees of aim error per swing.")]
    public float aimError = 12f;
    [Tooltip("Pause between own attacks (min, max seconds).")]
    public Vector2 attackCadence = new Vector2(0.45f, 1f);
    [Tooltip("Keeps this much stamina in reserve before committing to attacks.")]
    public float staminaReserve = 20f;
}

/// <summary>Built-in ladder of six tiers, used when TestArena has no skill assets assigned.</summary>
public static class DefaultSkills
{
    static CombatSkill[] cached;

    public static CombatSkill[] Ladder => cached ??= Create();
    public static CombatSkill Soldier => Ladder[2];

    public static CombatSkill[] Create()
    {
        //                         name        pips  xp   hp    stam  poise dmg   move  read  react min/max   timing block parry dodge dUnbl punish tact  range min/max   aim  cadence min/max  reserve
        return new[]
        {
            Make("Levy",       0,   0, 0.85f, 0.80f, 0.80f, 0.85f, 0.90f, 0.45f, 0.35f, 0.60f, 0.12f, 0.55f, 0.02f, 0.10f, 0.30f, 0.10f, 0.0f, 0.70f, 1.35f, 25f, 0.80f, 1.60f,  5f),
            Make("Militia",    1,  15, 0.95f, 0.90f, 0.90f, 0.95f, 0.95f, 0.60f, 0.28f, 0.50f, 0.10f, 0.60f, 0.06f, 0.15f, 0.45f, 0.25f, 0.2f, 0.70f, 1.20f, 18f, 0.60f, 1.30f, 12f),
            Make("Soldier",    2,  40, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 0.72f, 0.22f, 0.40f, 0.08f, 0.62f, 0.12f, 0.20f, 0.60f, 0.40f, 0.4f, 0.70f, 1.05f, 12f, 0.45f, 1.00f, 20f),
            Make("Veteran",    3,  90, 1.10f, 1.10f, 1.15f, 1.10f, 1.05f, 0.82f, 0.17f, 0.32f, 0.06f, 0.60f, 0.25f, 0.30f, 0.75f, 0.60f, 0.6f, 0.70f, 0.92f,  8f, 0.30f, 0.80f, 28f),
            Make("Elite",      4, 180, 1.20f, 1.20f, 1.30f, 1.15f, 1.10f, 0.90f, 0.13f, 0.25f, 0.04f, 0.55f, 0.40f, 0.35f, 0.85f, 0.75f, 0.8f, 0.72f, 0.88f,  5f, 0.20f, 0.60f, 35f),
            Make("Champion",   5, 350, 1.35f, 1.30f, 1.50f, 1.25f, 1.12f, 0.96f, 0.10f, 0.20f, 0.03f, 0.50f, 0.55f, 0.40f, 0.92f, 0.90f, 0.9f, 0.75f, 0.85f,  3f, 0.15f, 0.45f, 40f),
        };
    }

    static CombatSkill Make(string name, int pips, int xp, float hp, float stam, float poise, float dmg, float move,
        float read, float reactMin, float reactMax, float timing, float block, float parry, float dodge, float dodgeUnbl,
        float punish, float tactical, float rangeMin, float rangeMax, float aim, float cadMin, float cadMax, float reserve)
    {
        var s = ScriptableObject.CreateInstance<CombatSkill>();
        s.name = "Skill_" + name;
        s.tierName = name; s.rankPips = pips; s.xpToReach = xp;
        s.healthMultiplier = hp; s.staminaMultiplier = stam; s.poiseMultiplier = poise;
        s.damageMultiplier = dmg; s.moveSpeedMultiplier = move;
        s.readChance = read; s.reactionTime = new Vector2(reactMin, reactMax); s.timingError = timing;
        s.blockChance = block; s.parryChance = parry; s.dodgeChance = dodge; s.dodgeUnblockableChance = dodgeUnbl;
        s.punishChance = punish; s.tacticalChance = tactical; s.rangeJudgement = new Vector2(rangeMin, rangeMax);
        s.aimError = aim; s.attackCadence = new Vector2(cadMin, cadMax); s.staminaReserve = reserve;
        return s;
    }
}
