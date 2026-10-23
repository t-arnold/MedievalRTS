using UnityEngine;

public enum TelegraphType { Normal, Unblockable, Grab }

/// <summary>
/// One attack, fully described as data (top-down 2D version).
/// Create via Assets > Create > Combat > Attack Data.
/// Timing lives here, not in animation events: tune feel without touching sprites,
/// and keep the simulation independent of animation (good for multiplayer later).
/// </summary>
[CreateAssetMenu(menuName = "Combat/Attack Data", fileName = "NewAttack")]
public class AttackData : ScriptableObject
{
    [Header("Animation")]
    [Tooltip("Animator state to cross-fade into. Make it a 2D blend tree on DirX/DirY holding one clip per sprite direction.")]
    public string animatorState = "Attack_Light_1";
    public float crossFade = 0f; // sprite animation: usually snap, no fade

    [Header("Phases (seconds)")]
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
    [Range(0f, 1f), Tooltip("Damage dealt to your own team. 0 = no friendly fire. Magicka-style spells: 1.")]
    public float allyDamageMultiplier = 0f;

    [Header("Hit shape (arc in front of the attacker's feet)")]
    [Tooltip("World units. With 32 pixels-per-unit, 1.4 = ~45 px.")]
    public float reach = 1.4f;
    [Range(10f, 360f)] public float arcAngle = 120f;

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

    void OnValidate()
    {
        if (telegraph == TelegraphType.Unblockable) { blockable = false; parryable = false; }
    }
}
