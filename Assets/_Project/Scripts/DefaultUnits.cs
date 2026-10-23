using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Built-in roster used when TestArena has no unit assets assigned, and the source for
/// Tools > Combat > Create Default Unit Assets (which saves everything as tunable assets).
///
/// Design intent of each weapon:
///   Sword & shield — balanced all-rounder, good shield.
///   Axe & shield   — slower, hits harder, wrecks shields (high block-stamina damage), short reach.
///   Spear & shield — longest reach, weak per hit, huge bonus vs horses, braces against charges.
///   Bow            — arcing arrows that don't pick sides; weak sidearm, no shield.
///   Light cavalry  — fast, nimble, modest charge; hunts archers.
///   Heavy cavalry  — slow to build speed, devastating charge, very tough.
/// </summary>
public static class DefaultUnits
{
    public class Set
    {
        public ProjectileData arrow;
        public WeaponData swordShield, axeShield, spearShield, bow, riderSword, lanceMace;
        public UnitType swordsman, axeman, spearman, archer, lightCavalry, heavyCavalry;
        public readonly List<AttackData> attacks = new List<AttackData>();

        /// <summary>Order matters: TestArena's armyMix weights and the 0 key follow it.</summary>
        public UnitType[] Units => new[] { swordsman, axeman, spearman, archer, lightCavalry, heavyCavalry };
        public WeaponData[] PlayerWeapons => new[] { swordShield, axeShield, spearShield, bow };
        public WeaponData[] Weapons => new[] { swordShield, axeShield, spearShield, bow, riderSword, lanceMace };
    }

    /// <summary>
    /// Pass the sword attacks you already tuned (TestArena's slots) to keep them; nulls fall back to defaults.
    /// </summary>
    public static Set Create(AttackData swordLight = null, AttackData swordHeavy = null, AttackData[] swordAI = null)
    {
        var s = new Set();
        var sword = DefaultAttacks.Create();
        if (!swordLight) swordLight = sword.playerLight1;
        if (!swordHeavy) swordHeavy = sword.playerHeavy;
        if (swordAI == null || swordAI.Length == 0) swordAI = sword.Soldier;
        s.attacks.AddRange(sword.All);

        // ---------------- Projectiles ----------------
        s.arrow = ScriptableObject.CreateInstance<ProjectileData>();
        s.arrow.name = "Arrow";
        s.arrow.mode = ProjectileMode.Arc;
        s.arrow.speed = 15f; s.arrow.minRange = 3f; s.arrow.maxRange = 17f;
        s.arrow.hitRadius = 0.38f; s.arrow.scatter = 0.045f; s.arrow.maxTargets = 1;

        // ---------------- Axe ----------------
        var axeChop = A(s, "Axe_Chop", a =>
        {
            a.animatorState = "Attack_Chop";
            a.windup = 0.55f; a.active = 0.12f; a.recovery = 0.65f; a.cancelPoint = 1f;
            a.damage = 18; a.poiseDamage = 35; a.staminaCost = 16; a.blockStaminaDamage = 34;
            a.reach = 1.2f; a.arcAngle = 100; a.lungeDistance = 0.35f; a.knockback = 0.6f;
            a.hitStopFrames = 5; a.screenShake = 0.14f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 180;
        });
        var axeSweep = A(s, "Axe_Sweep", a =>
        {
            a.animatorState = "Attack_Sweep";
            a.windup = 0.5f; a.active = 0.14f; a.recovery = 0.6f; a.cancelPoint = 1f;
            a.damage = 14; a.poiseDamage = 25; a.staminaCost = 14; a.blockStaminaDamage = 22;
            a.reach = 1.25f; a.arcAngle = 150; a.lungeDistance = 0.25f; a.knockback = 0.5f;
            a.hitStopFrames = 4; a.screenShake = 0.1f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 200;
        });
        var axeOverhead = A(s, "Axe_Overhead_Unblockable", a =>
        {
            a.animatorState = "Attack_Overhead";
            a.windup = 1.0f; a.active = 0.14f; a.recovery = 1.0f; a.cancelPoint = 1f;
            a.damage = 32; a.poiseDamage = 65; a.staminaCost = 26; a.blockStaminaDamage = 0;
            a.reach = 1.3f; a.arcAngle = 70; a.lungeDistance = 0.5f; a.knockback = 1.4f;
            a.hitStopFrames = 7; a.screenShake = 0.3f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 100;
            a.telegraph = TelegraphType.Unblockable; a.blockable = false; a.parryable = false;
        });
        var pAxe1 = A(s, "PlayerAxe_Light_1", a =>
        {
            a.animatorState = "Attack_Light_1";
            a.windup = 0.28f; a.active = 0.1f; a.recovery = 0.42f; a.cancelPoint = 0.55f;
            a.damage = 22; a.poiseDamage = 30; a.staminaCost = 15; a.blockStaminaDamage = 30;
            a.reach = 1.2f; a.arcAngle = 110; a.lungeDistance = 0.35f; a.knockback = 0.55f;
            a.hitStopFrames = 4; a.screenShake = 0.12f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 480;
        });
        var pAxe2 = A(s, "PlayerAxe_Light_2", a =>
        {
            a.animatorState = "Attack_Light_2";
            a.windup = 0.26f; a.active = 0.1f; a.recovery = 0.6f; a.cancelPoint = 0.75f;
            a.damage = 26; a.poiseDamage = 38; a.staminaCost = 17; a.blockStaminaDamage = 36;
            a.reach = 1.25f; a.arcAngle = 160; a.lungeDistance = 0.45f; a.knockback = 0.9f;
            a.hitStopFrames = 5; a.screenShake = 0.16f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 480;
        });
        var pAxeHeavy = A(s, "PlayerAxe_Heavy", a =>
        {
            a.animatorState = "Attack_Heavy";
            a.windup = 0.65f; a.active = 0.14f; a.recovery = 0.85f; a.cancelPoint = 0.85f;
            a.damage = 50; a.poiseDamage = 80; a.staminaCost = 34; a.blockStaminaDamage = 70;
            a.reach = 1.35f; a.arcAngle = 120; a.lungeDistance = 0.6f; a.knockback = 1.6f;
            a.hitStopFrames = 7; a.screenShake = 0.35f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 200;
        });
        pAxe1.nextInCombo = pAxe2;

        // ---------------- Spear (bonus vs horses) ----------------
        var spearThrust = A(s, "Spear_Thrust", a =>
        {
            a.animatorState = "Attack_Thrust";
            a.windup = 0.42f; a.active = 0.1f; a.recovery = 0.5f; a.cancelPoint = 1f;
            a.damage = 15; a.poiseDamage = 18; a.staminaCost = 12; a.blockStaminaDamage = 14;
            a.reach = 2.3f; a.arcAngle = 30; a.lungeDistance = 0.45f; a.knockback = 0.45f;
            a.hitStopFrames = 3; a.screenShake = 0.08f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 240;
            a.vsMountedDamageMultiplier = 2.5f; a.vsMountedPoiseMultiplier = 3f;
        });
        var spearJab = A(s, "Spear_Jab", a =>
        {
            a.animatorState = "Attack_Jab";
            a.windup = 0.3f; a.active = 0.08f; a.recovery = 0.4f; a.cancelPoint = 1f;
            a.damage = 9; a.poiseDamage = 12; a.staminaCost = 8; a.blockStaminaDamage = 8;
            a.reach = 2.1f; a.arcAngle = 26; a.lungeDistance = 0.2f; a.knockback = 0.7f;
            a.hitStopFrames = 2; a.screenShake = 0.05f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 300;
            a.vsMountedDamageMultiplier = 2.5f; a.vsMountedPoiseMultiplier = 3f;
        });
        var spearLunge = A(s, "Spear_Lunge", a =>
        {
            a.animatorState = "Attack_Lunge";
            a.windup = 0.6f; a.active = 0.12f; a.recovery = 0.7f; a.cancelPoint = 1f;
            a.damage = 22; a.poiseDamage = 30; a.staminaCost = 18; a.blockStaminaDamage = 20;
            a.reach = 2.7f; a.arcAngle = 24; a.lungeDistance = 1.0f; a.knockback = 0.8f;
            a.hitStopFrames = 5; a.screenShake = 0.14f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 160;
            a.vsMountedDamageMultiplier = 2.5f; a.vsMountedPoiseMultiplier = 3f;
        });
        var spearBrace = A(s, "Spear_Brace", a =>
        {
            // Not swung: dealt to a rider who charges onto a braced spear. Scales with charge speed.
            a.animatorState = "";
            a.windup = 0f; a.active = 0f; a.recovery = 0f;
            a.damage = 45; a.poiseDamage = 130; a.staminaCost = 0; a.blockStaminaDamage = 0;
            a.blockable = false; a.parryable = false;
            a.reach = 2.3f; a.arcAngle = 60; a.lungeDistance = 0f; a.knockback = 0.3f;
            a.hitStopFrames = 8; a.screenShake = 0.45f;
        });
        var pSpear1 = A(s, "PlayerSpear_Light_1", a =>
        {
            a.animatorState = "Attack_Light_1";
            a.windup = 0.2f; a.active = 0.08f; a.recovery = 0.35f; a.cancelPoint = 0.5f;
            a.damage = 13; a.poiseDamage = 15; a.staminaCost = 10; a.blockStaminaDamage = 12;
            a.reach = 2.2f; a.arcAngle = 30; a.lungeDistance = 0.3f; a.knockback = 0.5f;
            a.hitStopFrames = 3; a.screenShake = 0.07f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 600;
            a.vsMountedDamageMultiplier = 2.5f; a.vsMountedPoiseMultiplier = 3f;
        });
        var pSpear2 = A(s, "PlayerSpear_Light_2", a =>
        {
            a.animatorState = "Attack_Light_2";
            a.windup = 0.2f; a.active = 0.08f; a.recovery = 0.45f; a.cancelPoint = 0.6f;
            a.damage = 15; a.poiseDamage = 18; a.staminaCost = 11; a.blockStaminaDamage = 14;
            a.reach = 2.3f; a.arcAngle = 28; a.lungeDistance = 0.4f; a.knockback = 0.6f;
            a.hitStopFrames = 3; a.screenShake = 0.08f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 600;
            a.vsMountedDamageMultiplier = 2.5f; a.vsMountedPoiseMultiplier = 3f;
        });
        var pSpearHeavy = A(s, "PlayerSpear_Heavy", a =>
        {
            a.animatorState = "Attack_Heavy";
            a.windup = 0.45f; a.active = 0.12f; a.recovery = 0.7f; a.cancelPoint = 0.8f;
            a.damage = 28; a.poiseDamage = 40; a.staminaCost = 24; a.blockStaminaDamage = 25;
            a.reach = 2.8f; a.arcAngle = 26; a.lungeDistance = 1.1f; a.knockback = 1.1f;
            a.hitStopFrames = 5; a.screenShake = 0.2f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 260;
            a.vsMountedDamageMultiplier = 2.5f; a.vsMountedPoiseMultiplier = 3f;
        });
        pSpear1.nextInCombo = pSpear2;

        // ---------------- Bow (arrows don't pick sides) ----------------
        var bowShot = A(s, "Bow_Shot", a =>
        {
            a.animatorState = "Attack_Shoot";
            a.windup = 0.75f; a.active = 0.05f; a.recovery = 0.35f; a.cancelPoint = 1f;
            a.damage = 16; a.poiseDamage = 18; a.staminaCost = 5; a.blockStaminaDamage = 6;
            a.blockable = true; a.parryable = false; a.allyDamageMultiplier = 1f;
            a.reach = 17f; a.arcAngle = 10; a.lungeDistance = 0f; a.knockback = 0.25f;
            a.hitStopFrames = 1; a.screenShake = 0.04f; a.windupTurnSpeed = 300;
            a.projectile = s.arrow;
        });
        var archerStab = A(s, "Archer_Stab", a =>
        {
            a.animatorState = "Attack_Stab";
            a.windup = 0.4f; a.active = 0.1f; a.recovery = 0.5f; a.cancelPoint = 1f;
            a.damage = 9; a.poiseDamage = 14; a.staminaCost = 10; a.blockStaminaDamage = 10;
            a.reach = 1.1f; a.arcAngle = 90; a.lungeDistance = 0.3f; a.knockback = 0.3f;
            a.hitStopFrames = 2; a.screenShake = 0.05f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 240;
        });
        var pBowShot = A(s, "PlayerBow_Shot", a =>
        {
            a.animatorState = "Attack_Shoot";
            a.windup = 0.5f; a.active = 0.05f; a.recovery = 0.25f; a.cancelPoint = 0.5f;
            a.damage = 18; a.poiseDamage = 20; a.staminaCost = 6; a.blockStaminaDamage = 7;
            a.blockable = true; a.parryable = false; a.allyDamageMultiplier = 1f;
            a.reach = 17f; a.arcAngle = 10; a.lungeDistance = 0f; a.knockback = 0.3f;
            a.hitStopFrames = 1; a.screenShake = 0.04f; a.windupTurnSpeed = 720;
            a.projectile = s.arrow;
        });
        var pBowVolley = A(s, "PlayerBow_Volley", a =>
        {
            a.animatorState = "Attack_Shoot";
            a.windup = 0.9f; a.active = 0.05f; a.recovery = 0.4f; a.cancelPoint = 0.8f;
            a.damage = 14; a.poiseDamage = 16; a.staminaCost = 16; a.blockStaminaDamage = 6;
            a.blockable = true; a.parryable = false; a.allyDamageMultiplier = 1f;
            a.reach = 17f; a.arcAngle = 10; a.lungeDistance = 0f; a.knockback = 0.3f;
            a.hitStopFrames = 1; a.screenShake = 0.06f; a.windupTurnSpeed = 540;
            a.projectile = s.arrow; a.projectileCount = 3; a.spreadAngle = 14f;
        });

        // ---------------- Cavalry melee (swung while riding) ----------------
        var riderSlash = A(s, "Rider_Slash", a =>
        {
            a.animatorState = "Attack_Slash";
            a.windup = 0.3f; a.active = 0.14f; a.recovery = 0.4f; a.cancelPoint = 1f;
            a.damage = 20; a.poiseDamage = 30; a.staminaCost = 12; a.blockStaminaDamage = 18;
            a.reach = 1.7f; a.arcAngle = 200; a.lungeDistance = 0f; a.knockback = 0.7f;
            a.hitStopFrames = 4; a.screenShake = 0.12f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 360;
        });
        var riderMace = A(s, "Rider_Mace", a =>
        {
            a.animatorState = "Attack_Mace";
            a.windup = 0.4f; a.active = 0.14f; a.recovery = 0.5f; a.cancelPoint = 1f;
            a.damage = 26; a.poiseDamage = 45; a.staminaCost = 16; a.blockStaminaDamage = 32;
            a.reach = 1.6f; a.arcAngle = 180; a.lungeDistance = 0f; a.knockback = 1.0f;
            a.hitStopFrames = 5; a.screenShake = 0.18f; a.allyDamageMultiplier = 0.5f; a.windupTurnSpeed = 300;
        });

        // ---------------- Weapons ----------------
        s.swordShield = W("Sword & Shield", WeaponLook.Sword, w =>
        {
            w.aiAttacks = swordAI; w.playerLight = swordLight; w.playerHeavy = swordHeavy;
            w.hasShield = true; w.blockAngle = 140f; w.blockChipDamage = 0.1f; w.blockStaminaMultiplier = 1f;
        });
        s.axeShield = W("Axe & Shield", WeaponLook.Axe, w =>
        {
            w.aiAttacks = new[] { axeChop, axeSweep, axeOverhead }; w.playerLight = pAxe1; w.playerHeavy = pAxeHeavy;
            w.hasShield = true; w.blockAngle = 130f; w.blockChipDamage = 0.12f; w.blockStaminaMultiplier = 1f;
        });
        s.spearShield = W("Spear & Shield", WeaponLook.Spear, w =>
        {
            w.aiAttacks = new[] { spearThrust, spearJab, spearLunge }; w.playerLight = pSpear1; w.playerHeavy = pSpearHeavy;
            w.hasShield = true; w.blockAngle = 120f; w.blockChipDamage = 0.12f; w.blockStaminaMultiplier = 1.1f;
            w.bracesVsCavalry = true; w.braceAttack = spearBrace;
        });
        s.bow = W("Bow", WeaponLook.Bow, w =>
        {
            w.aiAttacks = new[] { archerStab }; w.playerLight = pBowShot; w.playerHeavy = pBowVolley;
            w.rangedAttack = bowShot; w.ammo = 24; w.preferredRange = 11f;
            w.hasShield = false; w.blockAngle = 90f; w.blockChipDamage = 0.35f; w.blockStaminaMultiplier = 1.7f;
        });
        s.riderSword = W("Cavalry Sword", WeaponLook.Sword, w =>
        {
            w.aiAttacks = new[] { riderSlash }; w.playerLight = riderSlash; w.playerHeavy = riderSlash;
            w.hasShield = true; w.blockAngle = 120f; w.blockChipDamage = 0.12f;
        });
        s.lanceMace = W("Lance & Mace", WeaponLook.Lance, w =>
        {
            w.aiAttacks = new[] { riderMace }; w.playerLight = riderMace; w.playerHeavy = riderMace;
            w.hasShield = true; w.blockAngle = 140f; w.blockChipDamage = 0.1f;
        });

        // ---------------- Unit types ----------------
        s.swordsman = U("Swordsman", "Sword", UnitRole.Infantry, s.swordShield, u =>
        {
            u.health = 60; u.poise = 40; u.moveSpeed = 2.8f;
        });
        s.axeman = U("Axeman", "Axe", UnitRole.Infantry, s.axeShield, u =>
        {
            u.health = 65; u.poise = 45; u.moveSpeed = 2.7f;
        });
        s.spearman = U("Spearman", "Spear", UnitRole.Infantry, s.spearShield, u =>
        {
            u.health = 55; u.poise = 35; u.moveSpeed = 2.6f;
        });
        s.archer = U("Archer", "Bow", UnitRole.Archer, s.bow, u =>
        {
            u.health = 45; u.poise = 25; u.moveSpeed = 2.9f;
        });
        s.lightCavalry = U("Light Cavalry", "LtCav", UnitRole.Cavalry, s.riderSword, u =>
        {
            u.health = 85; u.poise = 70; u.stamina = 110; u.moveSpeed = 3f;
            u.knockbackResistance = 0.6f; u.bodyRadius = 0.45f; u.mass = 6f;
            u.mounted = true; u.topSpeed = 7.5f; u.acceleration = 9f; u.turnRate = 210f;
            u.chargeMinSpeed = 4.2f; u.chargeDamage = 22f; u.chargePoise = 55f; u.chargeKnockback = 1.6f;
            u.chargeBlockStaminaDamage = 40f; u.targetArchersBias = 8f;
        });
        s.heavyCavalry = U("Heavy Cavalry", "HvCav", UnitRole.Cavalry, s.lanceMace, u =>
        {
            u.health = 140; u.poise = 120; u.stamina = 120; u.moveSpeed = 2.5f;
            u.knockbackResistance = 0.85f; u.bodyRadius = 0.5f; u.mass = 10f;
            u.mounted = true; u.topSpeed = 6.2f; u.acceleration = 5.5f; u.turnRate = 140f;
            u.chargeMinSpeed = 3.6f; u.chargeDamage = 48f; u.chargePoise = 110f; u.chargeKnockback = 2.8f;
            u.chargeBlockStaminaDamage = 85f; u.targetArchersBias = 3f;
        });

        return s;
    }

    static AttackData A(Set s, string name, Action<AttackData> init)
    {
        var a = ScriptableObject.CreateInstance<AttackData>();
        a.name = name;
        init(a);
        s.attacks.Add(a);
        return a;
    }

    static WeaponData W(string name, WeaponLook look, Action<WeaponData> init)
    {
        var w = ScriptableObject.CreateInstance<WeaponData>();
        w.name = name.Replace(" & ", "And").Replace(" ", "");
        w.weaponName = name;
        w.look = look;
        init(w);
        return w;
    }

    static UnitType U(string name, string shortName, UnitRole role, WeaponData weapon, Action<UnitType> init)
    {
        var u = ScriptableObject.CreateInstance<UnitType>();
        u.name = name.Replace(" ", "");
        u.displayName = name;
        u.shortName = shortName;
        u.role = role;
        u.weapon = weapon;
        init(u);
        return u;
    }
}
