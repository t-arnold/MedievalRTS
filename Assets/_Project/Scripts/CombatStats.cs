using System;
using UnityEngine;

/// <summary>
/// Health, stamina and poise. No Update(): CombatStateMachine calls Tick(dt), so later you can
/// drive everything from a fixed simulation tick instead of the frame rate.
/// </summary>
public class CombatStats : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    [Tooltip("A single hit can never take you from full health to dead.")]
    public bool preventOneShots = true;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaRegenPerSec = 35f;
    public float staminaRegenDelay = 0.8f;

    [Header("Poise")]
    public float maxPoise = 50f;
    public float poiseRegenPerSec = 25f;
    public float poiseRegenDelay = 2f;

    public float Health { get; private set; }
    public float Stamina { get; private set; }
    public float Poise { get; private set; }
    public bool IsDead => Health <= 0f;
    /// <summary>You may start an action while stamina is above zero; it can then drop to zero (exhausted).</summary>
    public bool CanAct => Stamina > 0f;

    public event Action<float> OnDamaged;
    public event Action OnDied;

    float staminaRegenTimer, poiseRegenTimer;

    void Awake() => ResetStats();

    public void ResetStats()
    {
        Health = maxHealth; Stamina = maxStamina; Poise = maxPoise;
    }

    public void Tick(float dt)
    {
        if (IsDead) return;
        staminaRegenTimer -= dt;
        if (staminaRegenTimer <= 0f) Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenPerSec * dt);
        poiseRegenTimer -= dt;
        if (poiseRegenTimer <= 0f) Poise = Mathf.Min(maxPoise, Poise + poiseRegenPerSec * dt);
    }

    public void SpendStamina(float amount)
    {
        Stamina = Mathf.Max(0f, Stamina - amount);
        staminaRegenTimer = staminaRegenDelay;
    }

    /// <summary>Returns true if this drained stamina to zero (guard break).</summary>
    public bool DrainStamina(float amount)
    {
        SpendStamina(amount);
        return Stamina <= 0f;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        if (preventOneShots && Health >= maxHealth - 0.01f && amount >= Health)
            amount = Health - 1f;
        Health = Mathf.Max(0f, Health - amount);
        OnDamaged?.Invoke(amount);
        if (IsDead) OnDied?.Invoke();
    }

    /// <summary>Returns true if poise broke (caller should stagger). Poise refills on break.</summary>
    public bool TakePoiseDamage(float amount)
    {
        Poise -= amount;
        poiseRegenTimer = poiseRegenDelay;
        if (Poise > 0f) return false;
        Poise = maxPoise;
        return true;
    }

    public void Heal(float amount)
    {
        if (!IsDead) Health = Mathf.Min(maxHealth, Health + amount);
    }
}
