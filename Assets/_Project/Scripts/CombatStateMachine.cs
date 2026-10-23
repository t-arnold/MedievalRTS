using System;
using System.Collections.Generic;
using UnityEngine;

public enum CombatState { Idle, Windup, Active, Recovery, Blocking, Dodging, Staggered, Dead }
public enum HitType { Ignored, Dodged, Hit, CounterHit, Blocked, GuardBroken, Parried }

public struct HitResult
{
    public HitType type;
    public CombatStateMachine attacker;
    public CombatStateMachine defender;
    public AttackData attack;
    /// <summary>Direction the blow travelled (attacker -> defender).</summary>
    public Vector2 direction;
}

/// <summary>
/// Top-down 2D combat core, shared by the player and every NPC.
/// Brains (PlayerController, EnemyAI) only send intents: SetAim, RequestAttack, RequestDodge, SetBlock.
///
/// Setup: sprite pivot at the FEET; a small Collider2D at the feet on a "Hurtbox" layer.
/// Animator parameters: floats "DirX", "DirY" (set here), "Speed" (set by the controller).
/// Animator states on layer 0 (missing ones are skipped): "Locomotion", each AttackData.animatorState,
/// "Block", "BlockImpact", "Dodge", "Stagger", "Death". Make each a 2D blend tree on DirX/DirY
/// with one sprite clip per direction.
/// </summary>
[RequireComponent(typeof(CombatStats))]
public class CombatStateMachine : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public LayerMask hurtboxMask = ~0;
    [Tooltip("Same number = same side.")]
    public int team;
    [Tooltip("Scales damage and poise damage this combatant deals (set by experience tier).")]
    public float outgoingDamageMultiplier = 1f;

    [Header("Aim")]
    public float turnSpeed = 1080f;
    [Tooltip("How many directions your sprites are drawn in (4 or 8).")]
    public int spriteDirections = 8;

    [Header("Block / Parry")]
    public float blockAngle = 140f;
    public float parryWindow = 0.15f;
    [Range(0f, 1f)] public float blockChipDamage = 0.1f;
    public float guardBreakDuration = 1.2f;
    public float parriedStaggerDuration = 1.3f;

    [Header("Dodge")]
    public float dodgeDuration = 0.4f;
    public float dodgeDistance = 2.5f;
    public float dodgeStaminaCost = 20f;
    public Vector2 iFrames = new Vector2(0.03f, 0.25f);

    [Header("Stagger / knockback")]
    public float staggerDuration = 0.7f;
    public float counterHitPoiseMultiplier = 1.5f;
    [Range(0f, 1f), Tooltip("0 = full knockback, 1 = immovable (heavy armour, cavalry).")]
    public float knockbackResistance = 0f;
    [Tooltip("Higher = knockback stops sooner.")]
    public float knockbackDecay = 12f;

    [Header("Input")]
    public float inputBufferTime = 0.25f;

    // ---- public read-only state ----
    public CombatState State { get; private set; }
    public float StateTime { get; private set; }
    public AttackData CurrentAttack { get; private set; }
    public CombatStats Stats { get; private set; }
    public Vector2 Facing { get; private set; } = Vector2.down;
    public bool IsFrozen => freezeTimer > 0f;
    public bool IsInvulnerable => State == CombatState.Dodging && StateTime >= iFrames.x && StateTime <= iFrames.y;
    public bool CanMove => State == CombatState.Idle || State == CombatState.Blocking;

    /// <summary>Movement the combat system imposes (dodge, lunge, knockback). The controller adds it.</summary>
    public Vector2 ForcedVelocity
    {
        get
        {
            if (IsFrozen) return Vector2.zero;
            Vector2 v = knockbackVelocity;
            if (State == CombatState.Dodging) v += dodgeVelocity;
            if (State == CombatState.Active && CurrentAttack && CurrentAttack.active > 0f)
                v += Facing * (CurrentAttack.lungeDistance / CurrentAttack.active);
            return v;
        }
    }

    // ---- events: hook smears, hit-flash, audio, screen shake, UI, AI ----
    public event Action<CombatState, CombatState> OnStateChanged; // (from, to)
    public event Action<AttackData> OnTelegraph;                  // windup began
    public event Action<AttackData> OnSwing;                      // active began: spawn smear here
    public event Action<HitResult> OnHitLanded;
    public event Action<HitResult> OnHitReceived;

    // ---- internals ----
    enum Intent { None, Attack, Dodge }
    Intent bufferedIntent;
    AttackData bufferedAttack;
    Vector2 bufferedDodgeDir;
    float bufferTimer;
    bool blockHeld;

    Vector2 aimTarget = Vector2.down;
    float stateDuration;
    float freezeTimer;
    Vector2 dodgeVelocity;
    Vector2 knockbackVelocity;

    readonly HashSet<CombatStateMachine> hitThisSwing = new HashSet<CombatStateMachine>();
    readonly List<Collider2D> overlap = new List<Collider2D>(32);
    ContactFilter2D filter;

    static readonly int DirXHash = Animator.StringToHash("DirX");
    static readonly int DirYHash = Animator.StringToHash("DirY");

    void Awake()
    {
        Stats = GetComponent<CombatStats>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(hurtboxMask);
    }

    // ================= Intents =================

    /// <summary>World-space direction to face (e.g. toward the mouse). Turning is limited by state.</summary>
    public void SetAim(Vector2 dir)
    {
        if (dir.sqrMagnitude > 0.0001f) aimTarget = dir.normalized;
    }

    public void RequestAttack(AttackData attack)
    {
        if (!attack) return;
        bufferedIntent = Intent.Attack; bufferedAttack = attack; bufferTimer = inputBufferTime;
    }

    /// <summary>Dodge along a direction (usually movement input). Zero = backstep away from facing.</summary>
    public void RequestDodge(Vector2 dir)
    {
        bufferedIntent = Intent.Dodge;
        bufferedDodgeDir = dir.sqrMagnitude > 0.01f ? dir.normalized : -Facing;
        bufferTimer = inputBufferTime;
    }

    public void SetBlock(bool held) => blockHeld = held;

    // ================= Simulation =================

    /// <summary>Every active combatant. Used by AI target search and debug tools.
    /// Fine for hundreds of units; replace with a spatial grid before thousands.</summary>
    public static readonly List<CombatStateMachine> All = new List<CombatStateMachine>();
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update() => Tick(Time.deltaTime);

    public void Tick(float dt)
    {
        if (freezeTimer > 0f)
        {
            freezeTimer -= dt;
            if (freezeTimer <= 0f && animator) animator.speed = 1f;
            return; // hit-stop freezes combat timing too
        }

        Stats.Tick(dt);
        RotateFacing(dt);
        knockbackVelocity *= Mathf.Exp(-knockbackDecay * dt);
        StateTime += dt;
        if (bufferTimer > 0f) bufferTimer -= dt; else bufferedIntent = Intent.None;

        switch (State)
        {
            case CombatState.Idle:
                if (TryConsumeBuffer()) break;
                if (blockHeld) Enter(CombatState.Blocking);
                break;

            case CombatState.Blocking:
                if (TryConsumeBuffer()) break;
                if (!blockHeld) Enter(CombatState.Idle);
                break;

            case CombatState.Windup:
                if (StateTime >= CurrentAttack.windup)
                {
                    Enter(CombatState.Active);
                    OnSwing?.Invoke(CurrentAttack);
                }
                break;

            case CombatState.Active:
                SweepHitbox();
                if (State == CombatState.Active && StateTime >= CurrentAttack.active) Enter(CombatState.Recovery);
                break;

            case CombatState.Recovery:
                if (StateTime >= CurrentAttack.recovery * CurrentAttack.cancelPoint && TryConsumeBuffer()) break;
                if (StateTime >= CurrentAttack.recovery) Enter(CombatState.Idle);
                break;

            case CombatState.Dodging:
                if (StateTime >= dodgeDuration) Enter(CombatState.Idle);
                break;

            case CombatState.Staggered:
                if (StateTime >= stateDuration) Enter(CombatState.Idle);
                break;
        }

        UpdateAnimatorDirection();
    }

    float CurrentTurnSpeed => State switch
    {
        CombatState.Idle => turnSpeed,
        CombatState.Blocking => turnSpeed * 0.5f,
        CombatState.Windup => CurrentAttack ? CurrentAttack.windupTurnSpeed : 0f,
        _ => 0f, // committed: active, recovery, dodge, stagger, dead
    };

    void RotateFacing(float dt)
    {
        float maxStep = CurrentTurnSpeed * dt;
        if (maxStep <= 0f) return;
        float angle = Vector2.SignedAngle(Facing, aimTarget);
        Facing = Rotate(Facing, Mathf.Clamp(angle, -maxStep, maxStep)).normalized;
    }

    bool TryConsumeBuffer()
    {
        if (bufferedIntent == Intent.None || !Stats.CanAct) return false;

        if (bufferedIntent == Intent.Attack)
        {
            var attack = (State == CombatState.Recovery && CurrentAttack && CurrentAttack.nextInCombo)
                ? CurrentAttack.nextInCombo
                : bufferedAttack;
            bufferedIntent = Intent.None;
            StartAttack(attack);
            return true;
        }

        bufferedIntent = Intent.None;
        StartDodge(bufferedDodgeDir);
        return true;
    }

    void StartAttack(AttackData attack)
    {
        CurrentAttack = attack;
        Stats.SpendStamina(attack.staminaCost);
        hitThisSwing.Clear();
        Facing = aimTarget; // snap to aim on commit; windupTurnSpeed limits it from here
        Enter(CombatState.Windup);
        UpdateAnimatorDirection();
        Play(attack.animatorState, attack.crossFade);
        OnTelegraph?.Invoke(attack);
    }

    void StartDodge(Vector2 dir)
    {
        Stats.SpendStamina(dodgeStaminaCost);
        dodgeVelocity = dir * (dodgeDistance / dodgeDuration);
        Facing = dir;
        Enter(CombatState.Dodging);
        UpdateAnimatorDirection();
        Play("Dodge", 0f);
    }

    void SweepHitbox()
    {
        Vector2 origin = transform.position;
        int n = Physics2D.OverlapCircle(origin, CurrentAttack.reach, filter, overlap);

        for (int i = 0; i < n; i++)
        {
            Collider2D col = overlap[i];
            var target = col.GetComponentInParent<CombatStateMachine>();
            if (!target || target == this || hitThisSwing.Contains(target)) continue;

            // Arc test against the nearest point of the target's collider.
            Vector2 toTarget = col.ClosestPoint(origin) - origin;
            if (toTarget.sqrMagnitude > 0.0001f && Vector2.Angle(Facing, toTarget) > CurrentAttack.arcAngle * 0.5f)
                continue;

            float mult = target.team == team ? CurrentAttack.allyDamageMultiplier : 1f;
            if (mult <= 0f) continue;

            hitThisSwing.Add(target);
            HitResult result = target.ReceiveHit(this, CurrentAttack, mult * outgoingDamageMultiplier);
            if (result.type == HitType.Ignored || result.type == HitType.Dodged) continue;

            OnHitLanded?.Invoke(result);
            if (result.type != HitType.Parried) Freeze(CurrentAttack.hitStopFrames);
            if (State != CombatState.Active) return; // parried mid-sweep
        }
    }

    // ================= Receiving hits =================

    public HitResult ReceiveHit(CombatStateMachine attacker, AttackData attack, float damageMultiplier = 1f)
    {
        Vector2 toAttacker = (Vector2)(attacker.transform.position - transform.position);
        var result = new HitResult
        {
            attacker = attacker,
            defender = this,
            attack = attack,
            direction = toAttacker.sqrMagnitude > 0.0001f ? -toAttacker.normalized : -Facing,
        };

        if (State == CombatState.Dead) { result.type = HitType.Ignored; return result; }
        if (IsInvulnerable) { result.type = HitType.Dodged; return result; }

        bool facing = Vector2.Angle(Facing, toAttacker) <= blockAngle * 0.5f;

        if (State == CombatState.Blocking && facing && attack.blockable)
        {
            if (attack.parryable && StateTime <= parryWindow)
            {
                result.type = HitType.Parried;
                attacker.Stagger(parriedStaggerDuration, -result.direction);
            }
            else
            {
                Stats.TakeDamage(attack.damage * blockChipDamage * damageMultiplier);
                ApplyKnockback(result.direction, attack.knockback * 0.5f);
                if (Stats.DrainStamina(attack.blockStaminaDamage))
                {
                    result.type = HitType.GuardBroken;
                    Stagger(guardBreakDuration, result.direction);
                }
                else
                {
                    result.type = HitType.Blocked;
                    Play("BlockImpact", 0f);
                }
                Freeze(attack.hitStopFrames);
            }
            OnHitReceived?.Invoke(result);
            return result;
        }

        // Clean hit. Being caught in your own windup/recovery is the punishment.
        bool counter = State == CombatState.Windup || State == CombatState.Recovery;
        result.type = counter ? HitType.CounterHit : HitType.Hit;

        Stats.TakeDamage(attack.damage * damageMultiplier);
        ApplyKnockback(result.direction, attack.knockback);

        if (Stats.IsDead)
        {
            Enter(CombatState.Dead);
            Play("Death", 0f);
        }
        else if (Stats.TakePoiseDamage(attack.poiseDamage * damageMultiplier * (counter ? counterHitPoiseMultiplier : 1f)))
        {
            Stagger(staggerDuration, result.direction);
        }
        // else: poise held (hyper-armour) — flash white via OnHitReceived, keep going.

        Freeze(attack.hitStopFrames);
        OnHitReceived?.Invoke(result);
        return result;
    }

    public void Stagger(float duration, Vector2 blowDirection)
    {
        if (State == CombatState.Dead) return;
        CurrentAttack = null;
        bufferedIntent = Intent.None;
        stateDuration = duration;
        if (blowDirection.sqrMagnitude > 0.0001f) Facing = -blowDirection.normalized; // face the hit
        Enter(CombatState.Staggered);
        UpdateAnimatorDirection();
        Play("Stagger", 0f);
    }

    void ApplyKnockback(Vector2 dir, float distance)
    {
        // Exponential decay: total distance travelled = v0 / decay.
        knockbackVelocity += dir * (distance * (1f - knockbackResistance) * knockbackDecay);
    }

    // ================= Helpers =================

    void Freeze(int frames60)
    {
        if (frames60 <= 0) return;
        freezeTimer = Mathf.Max(freezeTimer, frames60 / 60f);
        if (animator) animator.speed = 0f; // local freeze; never use Time.timeScale
    }

    void Enter(CombatState next)
    {
        var prev = State;
        State = next;
        StateTime = 0f;
        if (prev == CombatState.Dodging) dodgeVelocity = Vector2.zero;

        if (next == CombatState.Idle) Play("Locomotion", 0f);
        else if (next == CombatState.Blocking) Play("Block", 0f);

        OnStateChanged?.Invoke(prev, next);
    }

    /// <summary>Snaps facing to the nearest drawn sprite direction for the animator.</summary>
    void UpdateAnimatorDirection()
    {
        if (!animator) return;
        float step = 360f / Mathf.Max(1, spriteDirections);
        float angle = Mathf.Round(Mathf.Atan2(Facing.y, Facing.x) * Mathf.Rad2Deg / step) * step;
        Vector2 snapped = Rotate(Vector2.right, angle);
        animator.SetFloat(DirXHash, snapped.x);
        animator.SetFloat(DirYHash, snapped.y);
    }

    void Play(string state, float fade)
    {
        if (!animator || string.IsNullOrEmpty(state)) return;
        int hash = Animator.StringToHash(state);
        if (!animator.HasState(0, hash)) return;
        if (fade > 0f) animator.CrossFadeInFixedTime(hash, fade, 0);
        else animator.Play(hash, 0, 0f);
    }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 o = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(o, o + (Vector3)Facing);

        if (State != CombatState.Active || !CurrentAttack) return;
        Gizmos.color = Color.red;
        float half = CurrentAttack.arcAngle * 0.5f;
        Vector3 prev = o + (Vector3)(Rotate(Facing, -half) * CurrentAttack.reach);
        Gizmos.DrawLine(o, prev);
        for (int i = 1; i <= 16; i++)
        {
            Vector3 p = o + (Vector3)(Rotate(Facing, -half + CurrentAttack.arcAngle * i / 16f) * CurrentAttack.reach);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        Gizmos.DrawLine(o, prev);
    }
}
