using UnityEngine;

/// <summary>
/// What a combatant is and carries: its UnitType, current weapon, and ammo.
/// Applies the weapon's defensive stats to the CombatStateMachine and counts arrows as they fly.
/// </summary>
[RequireComponent(typeof(CombatStateMachine))]
public class UnitLoadout : MonoBehaviour
{
    public UnitType unit;
    public WeaponData weapon;
    [Tooltip("-1 = unlimited.")]
    public int ammo = -1;

    CombatStateMachine csm;

    public bool CanShoot => weapon && weapon.IsRanged && ammo != 0;
    public bool Braces => weapon && weapon.bracesVsCavalry;
    public string Label => unit ? unit.shortName : weapon ? weapon.weaponName : "";

    void Awake()
    {
        csm = GetComponent<CombatStateMachine>();
        csm.Loadout = this;
    }

    void OnEnable() => csm.OnSwing += Swung;
    void OnDisable() => csm.OnSwing -= Swung;

    void Swung(AttackData a)
    {
        if (a && a.projectile && ammo > 0) ammo--;
    }

    public void Equip(WeaponData w, bool unlimitedAmmo = false)
    {
        weapon = w;
        ApplyWeapon(csm, w);
        ammo = unlimitedAmmo || !w || !w.IsRanged ? -1 : w.ammo;
    }

    public static void ApplyWeapon(CombatStateMachine c, WeaponData w)
    {
        if (!c || !w) return;
        c.blockAngle = w.blockAngle;
        c.blockChipDamage = w.blockChipDamage;
        c.blockStaminaMultiplier = w.blockStaminaMultiplier;
    }
}
