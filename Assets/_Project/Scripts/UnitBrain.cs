using UnityEngine;

/// <summary>
/// Shared base for NPC brains (SoldierAI for infantry and archers, CavalryAI for riders):
/// experience tier, target choice, attack tokens, separation and friendly-fire checks.
/// </summary>
[RequireComponent(typeof(CombatStateMachine), typeof(Rigidbody2D))]
public abstract class UnitBrain : MonoBehaviour
{
    [Tooltip("Fallback melee attacks if the unit has no weapon loadout.")]
    public AttackData[] attacks;
    public CombatSkill skill;
    [Tooltip("If set, fights only this target and ignores everyone else (used by the duel tester).")]
    public CombatStateMachine lockedTarget;

    [Header("Movement")]
    public float moveSpeed = 2.8f;
    public float separationRadius = 0.8f;

    [Header("Brain")]
    public float thinkInterval = 0.15f;
    public float aggroRange = 14f;

    [Header("Target choice")]
    [Tooltip("How often (seconds) to reconsider the target.")]
    public float retargetInterval = 0.8f;
    [Tooltip("Extra 'distance' per AI already engaging a target. Higher = attackers spread out more.")]
    public float crowdPenalty = 1.5f;
    [Tooltip("Only switch if the new target scores this much better (prevents flip-flopping).")]
    public float switchMargin = 1f;
    [Tooltip("Extra pull toward the player (negative = avoid). 0 = player is just another soldier.")]
    public float playerBias = 0f;

    public CombatStateMachine Target => target;
    public CombatStateMachine Self => self;

    protected CombatStateMachine self, target;
    protected Rigidbody2D rb;
    protected Animator animator;
    protected UnitLoadout loadout;
    protected AttackTokens targetTokens;
    protected bool holdingToken;
    protected float tokenTime;
    protected float thinkTimer, retargetTimer;

    bool captured;
    float baseHealth, baseStamina, basePoise, baseMove;

    protected static readonly int SpeedHash = Animator.StringToHash("Speed");

    /// <summary>The tier, or a sensible default before Start runs.</summary>
    protected CombatSkill Sk => skill ? skill : DefaultSkills.Soldier;

    protected virtual void Awake()
    {
        self = GetComponent<CombatStateMachine>();
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        animator = GetComponentInChildren<Animator>();
        loadout = GetComponent<UnitLoadout>();
        thinkTimer = Random.Range(0f, thinkInterval);      // stagger thinking across units
        retargetTimer = Random.Range(0f, retargetInterval);
    }

    protected virtual void Start()
    {
        if (!loadout) loadout = GetComponent<UnitLoadout>();
        ApplySkill(skill ? skill : DefaultSkills.Soldier);
    }

    protected virtual void OnEnable() => self.OnStateChanged += HandleState;

    protected virtual void OnDisable()
    {
        self.OnStateChanged -= HandleState;
        SetTarget(null);
    }

    /// <summary>Melee attacks from the weapon, or the fallback list.</summary>
    public AttackData[] MeleeAttacks
    {
        get
        {
            var w = loadout ? loadout.weapon : null;
            if (w && w.aiAttacks != null && w.aiAttacks.Length > 0) return w.aiAttacks;
            return attacks;
        }
    }

    /// <summary>Apply a tier. Safe to call again on promotion; current HP/stamina/poise keep their fractions.</summary>
    public virtual void ApplySkill(CombatSkill s)
    {
        if (!s) return;
        if (!captured)
        {
            baseHealth = self.Stats.maxHealth;
            baseStamina = self.Stats.maxStamina;
            basePoise = self.Stats.maxPoise;
            baseMove = moveSpeed;
            captured = true;
        }
        skill = s;
        self.Stats.Rescale(baseHealth * s.healthMultiplier, baseStamina * s.staminaMultiplier, basePoise * s.poiseMultiplier);
        self.outgoingDamageMultiplier = s.damageMultiplier;
        moveSpeed = baseMove * s.moveSpeedMultiplier;
    }

    protected virtual void HandleState(CombatState from, CombatState to)
    {
        bool swingFinished = to == CombatState.Idle && from == CombatState.Recovery;
        if (swingFinished || to == CombatState.Staggered || to == CombatState.Dead) ReleaseToken();
        if (to == CombatState.Dead) SetTarget(null);
    }

    // ================= Targeting =================

    protected void ChooseTarget()
    {
        if (lockedTarget)
        {
            SetTarget(lockedTarget.State != CombatState.Dead ? lockedTarget : null);
            return;
        }

        retargetTimer -= thinkInterval;
        bool invalid = !IsValid(target);
        if (!invalid && retargetTimer > 0f) return;
        retargetTimer = retargetInterval;
        if (!invalid && !CanSwitchTargetNow()) return;

        CombatStateMachine best = null;
        float bestScore = float.MaxValue;
        foreach (var other in CombatStateMachine.All)
        {
            if (!IsValid(other)) continue;
            float score = ScoreTarget(other);
            if (score < bestScore) { bestScore = score; best = other; }
        }

        if (invalid || (best && best != target && bestScore < ScoreTarget(target) - switchMargin))
            SetTarget(best);
    }

    /// <summary>Don't switch mid-swing by default.</summary>
    protected virtual bool CanSwitchTargetNow() =>
        self.State == CombatState.Idle || self.State == CombatState.Blocking;

    /// <summary>Lower is better: distance, plus a penalty for each other AI already on that target.</summary>
    protected virtual float ScoreTarget(CombatStateMachine c)
    {
        float d = Vector2.Distance(transform.position, c.transform.position);
        var t = c.Tokens;
        int others = t ? t.EngagedCount - (c == target ? 1 : 0) : 0;
        float score = d + crowdPenalty * Mathf.Max(0, others);
        if (c.isPlayer) score -= playerBias;
        return score;
    }

    protected bool IsValid(CombatStateMachine c) =>
        c && c != self && c.team != self.team && c.State != CombatState.Dead &&
        ((Vector2)(c.transform.position - transform.position)).sqrMagnitude < aggroRange * aggroRange;

    protected void SetTarget(CombatStateMachine t)
    {
        if (t == target) return;
        ReleaseToken();
        if (targetTokens) targetTokens.Disengage(this);
        target = t;
        targetTokens = t ? t.Tokens : null;
        if (targetTokens) targetTokens.Engage(this);
        OnTargetChanged();
    }

    protected virtual void OnTargetChanged() { }

    protected void ReleaseToken()
    {
        if (holdingToken && targetTokens) targetTokens.Release(this);
        holdingToken = false;
    }

    // ================= Helpers =================

    protected bool AllyInArc(AttackData atk)
    {
        if (!atk || atk.allyDamageMultiplier <= 0f) return false;
        Vector2 origin = transform.position;
        foreach (var other in CombatStateMachine.All)
        {
            if (other == self || other.team != self.team || other.State == CombatState.Dead) continue;
            Vector2 to = (Vector2)other.transform.position - origin;
            // The player gets a wider safety margin: soldiers are wary of swinging near their lord.
            float margin = other.isPlayer ? 0.8f : 0.3f;
            float extraAngle = other.isPlayer ? 25f : 0f;
            if (to.magnitude > atk.reach + margin) continue;
            if (Vector2.Angle(self.Facing, to) <= atk.arcAngle * 0.5f + extraAngle) return true;
        }
        return false;
    }

    /// <summary>Allies of this unit within radius of a point (not counting itself).</summary>
    protected int AlliesNear(Vector2 point, float radius)
    {
        int n = 0;
        float r2 = radius * radius;
        foreach (var other in CombatStateMachine.All)
        {
            if (other == self || other.team != self.team || other.State == CombatState.Dead) continue;
            if (((Vector2)other.transform.position - point).sqrMagnitude <= r2) n++;
        }
        return n;
    }

    protected Vector2 Separation()
    {
        Vector2 push = Vector2.zero;
        Vector2 pos = transform.position;
        foreach (var other in CombatStateMachine.All)
        {
            if (other == self || other.State == CombatState.Dead) continue;
            Vector2 away = pos - (Vector2)other.transform.position;
            float d = away.magnitude;
            float r = other.isMounted ? separationRadius + 0.4f : separationRadius;
            if (d < r && d > 0.001f) push += away / d * (1f - d / r);
        }
        return push;
    }

    protected static Vector2 Rotate(Vector2 v, float degrees) => CombatStateMachine.Rotate(v, degrees);
}
