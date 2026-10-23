using UnityEngine;

public enum WeaponLook { Sword, Axe, Spear, Bow, Mace, Lance }

/// <summary>
/// A weapon kit: the attacks it gives the AI and the player, whether it has a shield,
/// ranged ammo, and special rules (spears brace against cavalry).
/// Create via Assets > Create > Combat > Weapon, or Tools > Combat > Create Default Unit Assets.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Weapon", fileName = "NewWeapon")]
public class WeaponData : ScriptableObject
{
    public string weaponName = "Sword & Shield";
    public WeaponLook look = WeaponLook.Sword;

    [Header("AI melee attacks (picked by SoldierAI / CavalryAI)")]
    public AttackData[] aiAttacks;

    [Header("Player attacks (LMB light, E heavy)")]
    public AttackData playerLight;
    public AttackData playerHeavy;

    [Header("Ranged")]
    [Tooltip("AI ranged attack. Leave empty for melee weapons.")]
    public AttackData rangedAttack;
    [Tooltip("Arrows carried by AI units. The player has unlimited.")]
    public int ammo = 24;
    [Tooltip("Distance the AI tries to keep while shooting.")]
    public float preferredRange = 10f;

    [Header("Defence")]
    public bool hasShield = true;
    [Tooltip("Degrees in front that a block covers.")]
    public float blockAngle = 140f;
    [Range(0f, 1f), Tooltip("Fraction of damage that still gets through a block.")]
    public float blockChipDamage = 0.1f;
    [Tooltip("Multiplies stamina lost when blocking. Above 1 = weapon-only block, guard breaks sooner.")]
    public float blockStaminaMultiplier = 1f;

    [Header("Versus cavalry")]
    [Tooltip("A unit facing a charging horse with this weapon braces: the horse is stopped and takes the Brace Attack.")]
    public bool bracesVsCavalry;
    public AttackData braceAttack;

    public bool IsRanged => rangedAttack && rangedAttack.projectile;
}
