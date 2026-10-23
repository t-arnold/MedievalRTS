using UnityEngine;

/// <summary>
/// Starting attack values for the test arena. Used two ways:
///  - At runtime, if TestArena's attack slots are empty (instant testing, but tweaks are lost on Stop).
///  - By Tools > Combat > Create Default Attack Assets, which saves them as real assets you can tune.
/// Units: seconds, world units (1 unit ~ one character footprint), 60fps frames for hit-stop.
/// </summary>
public static class DefaultAttacks
{
    public class Set
    {
        public AttackData playerLight1, playerLight2, playerHeavy;
        public AttackData soldierSlash, soldierThrust, soldierOverhead;
        public AttackData[] Soldier => new[] { soldierSlash, soldierThrust, soldierOverhead };
        public AttackData[] All => new[] { playerLight1, playerLight2, playerHeavy, soldierSlash, soldierThrust, soldierOverhead };
    }

    public static Set Create()
    {
        var s = new Set
        {
            playerLight1 = Make("Player_Light_1", a =>
            {
                a.animatorState = "Attack_Light_1";
                a.windup = 0.22f; a.active = 0.10f; a.recovery = 0.35f; a.cancelPoint = 0.5f;
                a.damage = 18; a.poiseDamage = 20; a.staminaCost = 12; a.blockStaminaDamage = 15;
                a.reach = 1.3f; a.arcAngle = 120; a.lungeDistance = 0.35f; a.knockback = 0.4f;
                a.hitStopFrames = 3; a.screenShake = 0.08f; a.allyDamageMultiplier = 0.5f;
                a.windupTurnSpeed = 540;
            }),
            playerLight2 = Make("Player_Light_2", a =>
            {
                a.animatorState = "Attack_Light_2";
                a.windup = 0.20f; a.active = 0.10f; a.recovery = 0.50f; a.cancelPoint = 0.7f;
                a.damage = 22; a.poiseDamage = 28; a.staminaCost = 14; a.blockStaminaDamage = 20;
                a.reach = 1.35f; a.arcAngle = 140; a.lungeDistance = 0.45f; a.knockback = 0.7f;
                a.hitStopFrames = 4; a.screenShake = 0.12f; a.allyDamageMultiplier = 0.5f;
                a.windupTurnSpeed = 540;
            }),
            playerHeavy = Make("Player_Heavy", a =>
            {
                a.animatorState = "Attack_Heavy";
                a.windup = 0.55f; a.active = 0.14f; a.recovery = 0.70f; a.cancelPoint = 0.8f;
                a.damage = 40; a.poiseDamage = 60; a.staminaCost = 30; a.blockStaminaDamage = 45;
                a.reach = 1.6f; a.arcAngle = 160; a.lungeDistance = 0.6f; a.knockback = 1.4f;
                a.hitStopFrames = 6; a.screenShake = 0.3f; a.allyDamageMultiplier = 0.5f;
                a.windupTurnSpeed = 240;
            }),
            soldierSlash = Make("Soldier_Slash", a =>
            {
                a.animatorState = "Attack_Slash";
                a.windup = 0.50f; a.active = 0.12f; a.recovery = 0.60f; a.cancelPoint = 1f;
                a.damage = 15; a.poiseDamage = 25; a.staminaCost = 15; a.blockStaminaDamage = 20;
                a.reach = 1.3f; a.arcAngle = 110; a.lungeDistance = 0.4f; a.knockback = 0.5f;
                a.hitStopFrames = 4; a.screenShake = 0.1f; a.allyDamageMultiplier = 0.5f;
                a.windupTurnSpeed = 180;
            }),
            soldierThrust = Make("Soldier_Thrust", a =>
            {
                a.animatorState = "Attack_Thrust";
                a.windup = 0.45f; a.active = 0.10f; a.recovery = 0.55f; a.cancelPoint = 1f;
                a.damage = 14; a.poiseDamage = 20; a.staminaCost = 12; a.blockStaminaDamage = 15;
                a.reach = 1.8f; a.arcAngle = 40; a.lungeDistance = 0.7f; a.knockback = 0.3f;
                a.hitStopFrames = 3; a.screenShake = 0.08f; a.allyDamageMultiplier = 0.5f;
                a.windupTurnSpeed = 220;
            }),
            soldierOverhead = Make("Soldier_Overhead_Unblockable", a =>
            {
                a.animatorState = "Attack_Overhead";
                a.windup = 0.90f; a.active = 0.15f; a.recovery = 0.90f; a.cancelPoint = 1f;
                a.damage = 28; a.poiseDamage = 50; a.staminaCost = 25; a.blockStaminaDamage = 0;
                a.reach = 1.5f; a.arcAngle = 90; a.lungeDistance = 0.5f; a.knockback = 1.2f;
                a.hitStopFrames = 6; a.screenShake = 0.25f; a.allyDamageMultiplier = 0.5f;
                a.windupTurnSpeed = 120;
                a.telegraph = TelegraphType.Unblockable; a.blockable = false; a.parryable = false;
            }),
        };
        s.playerLight1.nextInCombo = s.playerLight2;
        return s;
    }

    static AttackData Make(string name, System.Action<AttackData> init)
    {
        var a = ScriptableObject.CreateInstance<AttackData>();
        a.name = name;
        init(a);
        return a;
    }
}
