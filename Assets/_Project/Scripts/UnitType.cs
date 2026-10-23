using UnityEngine;

public enum UnitRole { Infantry, Archer, Cavalry }

/// <summary>
/// A troop type: body stats, weapon, and (for cavalry) how the horse rides and charges.
/// Experience tiers (CombatSkill) stack on top: a Veteran Spearman is this asset plus the Veteran tier.
/// Create via Assets > Create > Combat > Unit Type, or Tools > Combat > Create Default Unit Assets.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Unit Type", fileName = "NewUnitType")]
public class UnitType : ScriptableObject
{
    public string displayName = "Swordsman";
    [Tooltip("Shown above heads in the debug overlay.")]
    public string shortName = "Sword";
    public UnitRole role = UnitRole.Infantry;
    public WeaponData weapon;

    [Header("Body")]
    public float health = 60f;
    public float poise = 40f;
    public float stamina = 100f;
    [Tooltip("Infantry walking speed (world units/sec).")]
    public float moveSpeed = 2.8f;
    [Range(0f, 1f)] public float knockbackResistance = 0f;
    [Tooltip("Collider radius at the feet. Horses are bigger.")]
    public float bodyRadius = 0.3f;
    [Tooltip("Physics mass: heavy horses shove infantry aside.")]
    public float mass = 1f;

    [Header("Mounted")]
    public bool mounted;
    public float topSpeed = 7f;
    [Tooltip("Units/sec². Heavy horses build speed slowly.")]
    public float acceleration = 8f;
    [Tooltip("Degrees/sec at full gallop. Turns are tighter when slow.")]
    public float turnRate = 180f;

    [Header("Charge (riding into enemies at speed)")]
    [Tooltip("Below this speed, contact does no charge damage.")]
    public float chargeMinSpeed = 4f;
    [Tooltip("Damage at full gallop. Scales down to 40% at minimum charge speed.")]
    public float chargeDamage = 25f;
    public float chargePoise = 55f;
    public float chargeKnockback = 1.6f;
    public float chargeBlockStaminaDamage = 40f;
    [Range(0f, 1f), Tooltip("Damage to your own infantry when riding through them. 0 = no trampling friends.")]
    public float chargeAllyDamage = 0f;
    [Tooltip("How strongly the AI prefers to ride down archers. Light cavalry hunt them.")]
    public float targetArchersBias = 3f;

    /// <summary>The 'attack' a charge delivers on contact, built from the fields above.</summary>
    public AttackData BuildChargeAttack()
    {
        var a = CreateInstance<AttackData>();
        a.name = displayName + " Charge";
        a.damage = chargeDamage;
        a.poiseDamage = chargePoise;
        a.knockback = chargeKnockback;
        a.blockStaminaDamage = chargeBlockStaminaDamage;
        a.blockable = true;
        a.parryable = false;
        a.allyDamageMultiplier = chargeAllyDamage;
        a.hitStopFrames = 3;
        a.screenShake = 0.35f;
        a.windup = 0f; a.active = 0f; a.recovery = 0f;
        a.reach = 0.8f; a.arcAngle = 120f; a.lungeDistance = 0f;
        return a;
    }
}
