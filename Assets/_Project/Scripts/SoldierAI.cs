using UnityEngine;

/// <summary>
/// Individual-soldier brain for milestone 1 (works for enemies AND allies; team decides who is hostile).
/// Loop: find nearest hostile -> circle at a distance -> get an attack token -> close in ->
/// hold the swing if an ally is inside the arc -> attack -> release token.
/// Reacts to telegraphs: blocks normal attacks, sidesteps unblockables.
/// Thinks on a staggered timer (not every frame), as the large-scale plan requires.
/// </summary>
[RequireComponent(typeof(CombatStateMachine), typeof(Rigidbody2D))]
public class SoldierAI : MonoBehaviour
{
    public AttackData[] attacks;

    [Header("Movement")]
    public float moveSpeed = 2.8f;
    public float acceleration = 20f;
    public float separationRadius = 0.8f;

    [Header("Brain")]
    public float thinkInterval = 0.15f;
    public float aggroRange = 14f;
    [Tooltip("Distance to circle at while waiting for an attack token.")]
    public float waitDistance = 2.6f;
    [Tooltip("Back off and wait when stamina is below this.")]
    public float lowStamina = 25f;
    [Range(0f, 1f)] public float blockChance = 0.4f;
    [Range(0f, 1f)] public float dodgeChance = 0.2f;
    [Range(0f, 1f), Tooltip("Chance to sidestep an unblockable attack.")]
    public float dodgeUnblockableChance = 0.6f;

    CombatStateMachine self, target;
    Rigidbody2D rb;
    Animator animator;
    AttackTokens targetTokens;
    AttackData nextAttack;

    float thinkTimer;
    Vector2 desiredVelocity, velocity;
    int circleSign;
    bool holdingToken, reactedToSwing;
    float tokenTime, blockUntil;

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
        thinkTimer = Random.Range(0f, thinkInterval); // stagger so everyone doesn't think on the same frame
    }

    void OnEnable() => self.OnStateChanged += HandleState;
    void OnDisable()
    {
        self.OnStateChanged -= HandleState;
        ReleaseToken();
    }

    void HandleState(CombatState from, CombatState to)
    {
        bool swingFinished = to == CombatState.Idle && from == CombatState.Recovery;
        if (swingFinished || to == CombatState.Staggered || to == CombatState.Dead) ReleaseToken();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (self.State == CombatState.Dead)
        {
            velocity = Vector2.zero;
            return;
        }

        // Never hog a token if the attack didn't start (e.g. out of stamina).
        if (holdingToken && self.State == CombatState.Idle && Time.time - tokenTime > 1.5f) ReleaseToken();

        thinkTimer -= dt;
        if (thinkTimer <= 0f)
        {
            thinkTimer += thinkInterval;
            Think();
        }

        self.SetBlock(Time.time < blockUntil);
        velocity = Vector2.MoveTowards(velocity, self.CanMove ? desiredVelocity : Vector2.zero, acceleration * dt);
        if (animator) animator.SetFloat(SpeedHash, velocity.magnitude, 0.05f, dt);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = velocity + self.ForcedVelocity;
    }

    void Think()
    {
        if (!IsValidTarget(target)) SetTarget(FindNearestHostile());
        if (!target) { desiredVelocity = Separation() * moveSpeed; return; }

        Vector2 toTarget = target.transform.position - transform.position;
        float dist = toTarget.magnitude;
        Vector2 dir = dist > 0.001f ? toTarget / dist : Vector2.down;
        self.SetAim(toTarget);

        ReactToTelegraph(toTarget, dist);

        if (!nextAttack && attacks != null && attacks.Length > 0) nextAttack = attacks[Random.Range(0, attacks.Length)];
        float reach = nextAttack ? nextAttack.reach : 1.2f;

        bool tired = self.Stats.Stamina < lowStamina;
        if (!holdingToken && !tired && dist < waitDistance + 1.5f)
        {
            holdingToken = targetTokens == null || targetTokens.TryAcquire(this);
            if (holdingToken) tokenTime = Time.time;
        }

        Vector2 move;
        if (holdingToken)
        {
            if (dist > reach * 0.85f)
            {
                move = dir; // close in
            }
            else
            {
                move = Vector2.zero;
                if (self.State == CombatState.Idle && nextAttack && !AllyInArc(nextAttack))
                {
                    self.RequestAttack(nextAttack);
                    nextAttack = null;
                }
            }
        }
        else
        {
            // Circle the target at waitDistance (farther if tired), occasionally changing direction.
            float ring = tired ? waitDistance + 1.5f : waitDistance;
            float radialError = Mathf.Clamp(dist - ring, -1f, 1f);
            Vector2 tangent = new Vector2(-dir.y, dir.x) * circleSign;
            move = dir * radialError + tangent * 0.5f;
            if (Random.value < 0.02f) circleSign = -circleSign;
            move *= 0.7f;
        }

        move += Separation() * 1.2f;
        desiredVelocity = Vector2.ClampMagnitude(move, 1f) * moveSpeed;
    }

    void ReactToTelegraph(Vector2 toTarget, float dist)
    {
        var atk = target.CurrentAttack;
        bool threatened = target.State == CombatState.Windup && atk
                       && dist < atk.reach + 0.6f
                       && Vector2.Angle(target.Facing, -toTarget) < atk.arcAngle * 0.5f + 15f;

        if (!threatened) { reactedToSwing = false; return; }
        if (reactedToSwing) return;
        reactedToSwing = true;

        float timeToHit = Mathf.Max(0f, atk.windup - target.StateTime);
        if (atk.blockable && Random.value < blockChance && self.State == CombatState.Idle)
        {
            blockUntil = Time.time + timeToHit + atk.active + 0.15f;
        }
        else if (Random.value < (atk.blockable ? dodgeChance : dodgeUnblockableChance))
        {
            Vector2 side = new Vector2(-toTarget.y, toTarget.x).normalized * (Random.value < 0.5f ? -1f : 1f);
            self.RequestDodge(side);
        }
    }

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

    CombatStateMachine FindNearestHostile()
    {
        CombatStateMachine best = null;
        float bestSq = aggroRange * aggroRange;
        Vector2 pos = transform.position;
        foreach (var other in CombatStateMachine.All)
        {
            if (other.team == self.team || other.State == CombatState.Dead) continue;
            float sq = ((Vector2)other.transform.position - pos).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = other; }
        }
        return best;
    }

    bool IsValidTarget(CombatStateMachine t) =>
        t && t.State != CombatState.Dead &&
        ((Vector2)(t.transform.position - transform.position)).sqrMagnitude < aggroRange * aggroRange;

    void SetTarget(CombatStateMachine t)
    {
        if (t == target) return;
        ReleaseToken();
        target = t;
        targetTokens = t ? t.GetComponent<AttackTokens>() : null;
    }

    void ReleaseToken()
    {
        if (holdingToken && targetTokens) targetTokens.Release(this);
        holdingToken = false;
    }
}
