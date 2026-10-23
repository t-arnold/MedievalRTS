using UnityEngine;

public enum TelegraphType { Normal, Unblockable, Grab }

/// <summary>
/// One attack, fully described as data (top-down 2D version).
/// Create via Assets > Create > Combat > Attack Data.
/// Timing lives here, not in animation events: tune feel without touching sprites,
/// and keep the simulation independent of animation (good for multiplayer later).
/// Melee attacks sweep an arc; set a Projectile to make it a ranged attack instead.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Attack Data", fileName = "NewAttack")]
public class AttackData : ScriptableObject
{
    [Header("Animation")]
    [Tooltip("Animator state to cross-fade into. Make it a 2D blend tree on DirX/DirY holding one clip per sprite direction.")]
    public string animatorState = "Attack_Light_1";
    public float crossFade = 0f; // sprite animation: usually snap, no fade

    [Header("Phases (seconds)")]
    [Tooltip("For bows this is the draw time.")]
    public float windup = 0.3f;
    public float active = 0.12f;
    public float recovery = 0.4f;
    [Range(0f, 1f), Tooltip("Fraction of recovery after which a buffered attack or dodge may cancel it. 1 = never.")]
    public float cancelPoint = 0.6f;
    [Tooltip("Next attack if attack is pressed again during the cancel window.")]
    public AttackData nextInCombo;

    [Header("Damage")]
    public float damage = 20f;
    public float poiseDamage = 25f;
    public float staminaCost = 15f;
    public float blockStaminaDamage = 20f;
    public bool blockable = true;
    public bool parryable = true;
    [Range(0f, 1f), Tooltip("Damage dealt to your own team. 0 = no friendly fire. Arrows: 1 (they don't pick sides).")]
    public float allyDamageMultiplier = 0f;

    [Header("Versus mounted targets (spears punish horses)")]
    public float vsMountedDamageMultiplier = 1f;
    public float vsMountedPoiseMultiplier = 1f;

    [Header("Hit shape (melee arc in front of the attacker's feet)")]
    [Tooltip("World units. With 32 pixels-per-unit, 1.4 = ~45 px.")]
    public float reach = 1.4f;
    [Range(10f, 360f)] public float arcAngle = 120f;

    [Header("Ranged (leave empty for melee)")]
    [Tooltip("Set this to fire projectiles when the attack goes active instead of sweeping an arc.")]
    public ProjectileData projectile;
    [Min(1)] public int projectileCount = 1;
    [Tooltip("Total fan angle when firing several projectiles at once.")]
    public float spreadAngle = 0f;

    [Header("Aim & movement")]
    [Tooltip("Degrees/second the attacker may still re-aim during windup. Facing locks during active/recovery: that is what makes dodging fair.")]
    public float windupTurnSpeed = 360f;
    [Tooltip("World units moved along facing during the active phase.")]
    public float lungeDistance = 0.4f;
    [Tooltip("World units the victim is shoved.")]
    public float knockback = 0.6f;

    [Header("Telegraph (enemies)")]
    public TelegraphType telegraph = TelegraphType.Normal;
    public AudioClip telegraphSound;

    [Header("Feel")]
    [Range(0, 8), Tooltip("Hit-stop, in 60fps frames.")]
    public int hitStopFrames = 4;
    public float screenShake = 0.15f;
    [Tooltip("Optional swing smear sprite/prefab, spawned on active.")]
    public GameObject smearPrefab;

    public bool IsRanged => projectile != null;

    void OnValidate()
    {
        if (telegraph == TelegraphType.Unblockable) { blockable = false; parryable = false; }
    }
}
