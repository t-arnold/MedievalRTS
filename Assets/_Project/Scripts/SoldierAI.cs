using UnityEngine;

/// <summary>
/// Individual-soldier brain (enemies AND allies; team decides who is hostile).
/// Loop: pick a target (spreading out across targets) -> circle -> get an attack token -> close in ->
/// hold the swing if an ally is in the arc -> attack -> release token.
/// HOW WELL it does each step comes from its CombatSkill tier: reading telegraphs, reaction time,
/// block/parry/dodge timing, judging range, aiming, punishing openings, stamina discipline.
/// </summary>
[RequireComponent(typeof(CombatStateMachine), typeof(Rigidbody2D))]
public class SoldierAI : MonoBehaviour
{
    public AttackData[] attacks;
    public CombatSkill skill;
    [Tooltip("If set, fights only this target and ignores everyone else (used by the duel tester).")]
    public CombatStateMachine lockedTarget;

    [Header("Movement")]
    public float moveSpeed = 2.8f;
    public float acceleration = 20f;
    public float separationRadius = 0.8f;

    [Header("Brain")]
    public float thinkInterval = 0.15f;
    public float aggroRange = 14f;
    [Tooltip("Distance to circle at while waiting for an attack token.")]
    public float waitDistance = 2.6f;

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

    CombatStateMachine self, target;
    Rigidbody2D rb;
    Animator animator;
    AttackTokens targetTokens;

    // attack planning
    AttackData nextAttack;
    float attackTriggerScale = 0.85f;
    float aimJitter;
    float nextAttackAllowedTime;
    bool wantPunish, punishRolled;

    // movement
    float thinkTimer, retargetTimer;
    Vector2 desiredVelocity, velocity;
    int circleSign;

    // tokens
    bool holdingToken;
    float tokenTime;

    // scheduled reactions
    bool threatSeen, pendingDodge;
    float blockStartAt, blockUntil, dodgeAt;
    Vector2 dodgeDir;

    // base values captured before tier multipliers
    bool captured;
    float baseHealth, baseStamina, basePoise, baseMove;

    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        self = GetComponent<CombatStateMachine>();
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        animator = GetComponentInChildren<Animator>();
        circleSign = Random.value < 0.5f ? -1 : 1;
        thinkTimer = Random.Range(0f, thinkInterval);      // stagger thinking across units
        retargetTimer = Random.Range(0f, retargetInterval);
    }

    void Start() => ApplySkill(skill ? skill : DefaultSkills.Soldier);

    void OnEnable() => self.OnStateChanged += HandleState;

    void OnDisable()
    {
        self.OnStateChanged -= HandleState;
        SetTarget(null);
    }

    /// <summary>Apply a tier. Safe to call again on promotion; current HP/stamina/poise keep their fractions.</summary>
    public void ApplySkill(CombatSkill s)
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

    void HandleState(CombatState from, CombatState to)
    {
        bool swingFinished = to == CombatState.Idle && from == CombatState.Recovery;
        if (swingFinished && skill)
            nextAttackAllowedTime = Time.time + Random.Range(skill.attackCadence.x, skill.attackCadence.y);

        if (swingFinished || to == CombatState.Staggered || to == CombatState.Dead) ReleaseToken();

        if (to == CombatState.Staggered || to == CombatState.Dead)
        {
            pendingDodge = false;
            blockUntil = 0f;
        }
        if (to == CombatState.Dead) SetTarget(null);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (self.State == CombatState.Dead) { velocity = Vector2.zero; return; }

        // Never hog a token if the attack didn't start.
        if (holdingToken && self.State == CombatState.Idle && Time.time - tokenTime > 1.5f) ReleaseToken();

        thinkTimer -= dt;
        if (thinkTimer <= 0f)
        {
            thinkTimer += thinkInterval;
            Think(dt);
        }

        // Scheduled reactions (this is where reaction time and timing error play out).
        if (pendingDodge && Time.time >= dodgeAt)
        {
            pendingDodge = false;
            self.RequestDodge(dodgeDir);
        }
        self.SetBlock(Time.time >= blockStartAt && Time.time < blockUntil);

        velocity = Vector2.MoveTowards(velocity, self.CanMove ? desiredVelocity : Vector2.zero, acceleration * dt);
        if (animator) animator.SetFloat(SpeedHash, velocity.magnitude, 0.05f, dt);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = velocity + self.ForcedVelocity;
    }

    // ================= Thinking =================

    void Think(float dt)
    {
        ChooseTarget();
        if (!target) { desiredVelocity = Separation() * moveSpeed; return; }

        Vector2 toTarget = target.transform.position - transform.position;
        float dist = toTarget.magnitude;
        Vector2 dir = dist > 0.001f ? toTarget / dist : Vector2.down;

        if (!nextAttack) PlanAttack(dist);
        self.SetAim(Rotate(toTarget, aimJitter));

        ReadThreat(toTarget, dist);

        bool tired = self.Stats.Stamina < skill.staminaReserve;
        bool ready = Time.time >= nextAttackAllowedTime;

        // Spot an opening: opponent recovering from a swing or staggered.
        bool open = target.State == CombatState.Recovery || target.State == CombatState.Staggered;
        if (open && !punishRolled) { punishRolled = true; wantPunish = Random.value < skill.punishChance; }
        if (!open) { punishRolled = false; wantPunish = false; }
        bool punishing = wantPunish && open && !tired;

        if (!holdingToken && !tired && (ready || punishing) && dist < waitDistance + 1.5f)
        {
            holdingToken = targetTokens == null || targetTokens.TryAcquire(this);
            if (holdingToken) tokenTime = Time.time;
        }

        Vector2 move;
        if (holdingToken)
        {
            float triggerDist = nextAttack ? nextAttack.reach * attackTriggerScale : 1.2f;
            if (dist > triggerDist)
            {
                move = dir; // close in
            }
            else
            {
                move = Vector2.zero;
                if (self.State == CombatState.Idle && nextAttack && (ready || punishing) && !AllyInArc(nextAttack))
                {
                    self.RequestAttack(nextAttack);
                    nextAttack = null;
                    wantPunish = false;
                }
            }
        }
        else
        {
            float ring = tired ? waitDistance + 1.5f : waitDistance;
            float radialError = Mathf.Clamp(dist - ring, -1f, 1f);
            Vector2 tangent = new Vector2(-dir.y, dir.x) * circleSign;
            move = (dir * radialError + tangent * 0.5f) * 0.7f;
            if (Random.value < 0.02f) circleSign = -circleSign;
        }

        move += Separation() * 1.2f;
        desiredVelocity = Vector2.ClampMagnitude(move, 1f) * moveSpeed;
    }

    /// <summary>Choose which attack to throw next, and how well it will be judged and aimed.</summary>
    void PlanAttack(float dist)
    {
        if (attacks == null || attacks.Length == 0) return;

        nextAttack = null;
        if (Random.value < skill.tacticalChance)
        {
            AttackData unblockable = null, longest = null, fastest = null;
            foreach (var a in attacks)
            {
                if (!a) continue;
                if (a.telegraph == TelegraphType.Unblockable) unblockable = a;
                if (!longest || a.reach > longest.reach) longest = a;
                if (a.blockable && (!fastest || a.windup < fastest.windup)) fastest = a;
            }
            if (target.State == CombatState.Blocking && unblockable) nextAttack = unblockable;   // beat the turtle
            else if (dist > 1.5f && longest) nextAttack = longest;                               // use reach
            else nextAttack = fastest;                                                           // quick and safe
        }
        if (!nextAttack) nextAttack = attacks[Random.Range(0, attacks.Length)];

        attackTriggerScale = Random.Range(skill.rangeJudgement.x, skill.rangeJudgement.y);
        aimJitter = Random.Range(-skill.aimError, skill.aimError);
    }

    /// <summary>Notice an incoming attack and schedule a block, parry or dodge (or fail to).</summary>
    void ReadThreat(Vector2 toTarget, float dist)
    {
        var atk = target.CurrentAttack;
        bool threatened = target.State == CombatState.Windup && atk
                       && dist < atk.reach + 0.6f
                       && Vector2.Angle(target.Facing, -toTarget) < atk.arcAngle * 0.5f + 15f;

        if (!threatened) { threatSeen = false; return; }
        if (threatSeen) return;
        threatSeen = true;

        if (Random.value > skill.readChance) return; // didn't see it coming

        float now = Time.time;
        float predictedHit = now + Mathf.Max(0f, atk.windup - target.StateTime);
        float reactAt = now + Random.Range(skill.reactionTime.x, skill.reactionTime.y);
        float noise = Random.Range(-skill.timingError, skill.timingError);

        if (atk.blockable && Random.value < skill.blockChance)
        {
            bool tryParry = atk.parryable && Random.value < skill.parryChance;
            float start = tryParry ? predictedHit - self.parryWindow * Random.Range(0.2f, 0.8f) + noise : reactAt + noise;
            blockStartAt = Mathf.Max(reactAt, start);   // can't act before reacting: slow tiers can't parry fast swings
            blockUntil = predictedHit + atk.active + 0.15f;
        }
        else if (Random.value < (atk.blockable ? skill.dodgeChance : skill.dodgeUnblockableChance))
        {
            dodgeAt = Mathf.Max(reactAt, predictedHit - 0.15f + noise); // aim i-frames at the moment of impact
            dodgeDir = new Vector2(-toTarget.y, toTarget.x).normalized * (Random.value < 0.5f ? -1f : 1f);
            pendingDodge = true;
        }
    }

    // ================= Targeting =================

    void ChooseTarget()
    {
        if (lockedTarget)
        {
            if (lockedTarget.State != CombatState.Dead) SetTarget(lockedTarget);
            else SetTarget(null);
            return;
        }

        retargetTimer -= thinkInterval;
        bool invalid = !IsValid(target);
        if (!invalid && retargetTimer > 0f) return;
        retargetTimer = retargetInterval;

        // Don't switch mid-swing.
        if (!invalid && self.State != CombatState.Idle && self.State != CombatState.Blocking) return;

        CombatStateMachine best = null;
        float bestScore = float.MaxValue;
        foreach (var other in CombatStateMachine.All)
        {
            if (!IsValid(other)) continue;
            float score = Score(other);
            if (score < bestScore) { bestScore = score; best = other; }
        }

        if (invalid || (best && best != target && bestScore < Score(target) - switchMargin))
            SetTarget(best);
    }

    /// <summary>Lower is better: distance, plus a penalty for each other AI already on that target.</summary>
    float Score(CombatStateMachine c)
    {
        float d = Vector2.Distance(transform.position, c.transform.position);
        var tokens = c.GetComponent<AttackTokens>();
        int others = tokens ? tokens.EngagedCount - (c == target ? 1 : 0) : 0;
        float score = d + crowdPenalty * Mathf.Max(0, others);
        if (c.GetComponent<PlayerController>()) score -= playerBias;
        return score;
    }

    bool IsValid(CombatStateMachine c) =>
        c && c != self && c.team != self.team && c.State != CombatState.Dead &&
        ((Vector2)(c.transform.position - transform.position)).sqrMagnitude < aggroRange * aggroRange;

    void SetTarget(CombatStateMachine t)
    {
        if (t == target) return;
        ReleaseToken();
        if (targetTokens) targetTokens.Disengage(this);
        target = t;
        targetTokens = t ? t.GetComponent<AttackTokens>() : null;
        if (targetTokens) targetTokens.Engage(this);
        nextAttack = null;
        threatSeen = false;
    }

    void ReleaseToken()
    {
        if (holdingToken && targetTokens) targetTokens.Release(this);
        holdingToken = false;
    }

    // ================= Helpers =================

    bool AllyInArc(AttackData atk)
    {
        if (atk.allyDamageMultiplier <= 0f) return false;
        Vector2 origin = transform.position;
        foreach (var other in CombatStateMachine.All)
        {
            if (other == self || other.team != self.team || other.State == CombatState.Dead) continue;
            Vector2 to = (Vector2)other.transform.position - origin;
            if (to.magnitude > atk.reach + 0.3f) continue;
            if (Vector2.Angle(self.Facing, to) <= atk.arcAngle * 0.5f) return true;
        }
        return false;
    }

    Vector2 Separation()
    {
        Vector2 push = Vector2.zero;
        Vector2 pos = transform.position;
        foreach (var other in CombatStateMachine.All)
        {
            if (other == self || other.State == CombatState.Dead) continue;
            Vector2 away = pos - (Vector2)other.transform.position;
            float d = away.magnitude;
            if (d < separationRadius && d > 0.001f) push += away / d * (1f - d / separationRadius);
        }
        return push;
    }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}
