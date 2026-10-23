using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rider brain for light and heavy cavalry. The horse has momentum: it accelerates, turns in arcs
/// (tighter when slow) and can't stop on a coin. Riders charge through a target, ride out, wheel
/// around and charge again. Contact at speed is the charge: damage, poise break and knockback that
/// scale with speed, and every body hit slows the horse.
///
/// Counters: spearmen facing the horse brace — the horse is stopped dead and the rider takes the
/// spear's brace hit. Experienced riders avoid charging braced spears; levies ride straight in.
/// Infantry who see a charge coming dodge aside or raise shields (see SoldierAI).
/// </summary>
public class CavalryAI : UnitBrain
{
    public static readonly List<CavalryAI> Riders = new List<CavalryAI>();

    [Header("Riding (set from UnitType by Configure)")]
    public float topSpeed = 7f;
    public float accelerationRate = 8f;
    public float turnRate = 180f;
    public float bodyRadius = 0.45f;

    [Header("Charge")]
    public float chargeMinSpeed = 4f;
    public float chargeHitRadius = 0.7f;
    [Tooltip("Fraction of speed lost per infantryman ridden through.")]
    public float speedLossPerHit = 0.12f;
    [Tooltip("Seconds before the same unit can be charged again.")]
    public float rehitCooldown = 1.2f;
    public float targetArchersBias = 3f;

    [Header("Wheeling")]
    public Vector2 rideOutDistance = new Vector2(6f, 9f);
    public float wallLookAhead = 2.5f;

    public int Team => self ? self.team : -1;
    public Vector2 Heading => heading;
    public float Speed => speed;
    public Vector2 Velocity => heading * speed;
    public bool IsCharging => mode == Mode.Charge && speed >= chargeMinSpeed;
    public string ModeName => mode.ToString();

    enum Mode { Charge, RideOut, Brawl }
    Mode mode = Mode.Charge;
    float modeTime;

    Vector2 heading = Vector2.right;
    float speed;
    Vector2 desiredDir = Vector2.right;
    float desiredSpeed;
    Vector2 rideOutPoint;
    float chargeJitter;
    bool abortRolled;
    float nextSwingTime;

    AttackData chargeAttack;
    bool captured;
    float baseTopSpeed;

    readonly Dictionary<CombatStateMachine, float> lastHit = new Dictionary<CombatStateMachine, float>();
    readonly List<Collider2D> overlap = new List<Collider2D>(16);
    readonly List<RaycastHit2D> rayHits = new List<RaycastHit2D>(8);
    ContactFilter2D unitFilter, wallFilter;

    /// <summary>Copy riding and charge stats from a UnitType. Call right after AddComponent.</summary>
    public void Configure(UnitType u)
    {
        topSpeed = u.topSpeed;
        accelerationRate = u.acceleration;
        turnRate = u.turnRate;
        bodyRadius = u.bodyRadius;
        chargeMinSpeed = u.chargeMinSpeed;
        targetArchersBias = u.targetArchersBias;
        chargeAttack = u.BuildChargeAttack();
    }

    public void SetHeading(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        heading = dir.normalized;
        desiredDir = heading;
    }

    protected override void Awake()
    {
        base.Awake();
        unitFilter = new ContactFilter2D { useTriggers = true };
        wallFilter = new ContactFilter2D { useTriggers = false };
        aggroRange = 30f;
        crowdPenalty = 0.75f;
    }

    protected override void Start()
    {
        base.Start();
        if (!chargeAttack)
        {
            var u = loadout ? loadout.unit : null;
            if (u) Configure(u);
            else
            {
                chargeAttack = ScriptableObject.CreateInstance<AttackData>();
                chargeAttack.name = "Charge";
                chargeAttack.damage = 25f; chargeAttack.poiseDamage = 55f; chargeAttack.knockback = 1.6f;
                chargeAttack.parryable = false;
            }
        }
        self.SetAim(heading);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Riders.Add(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Riders.Remove(this);
    }

    public override void ApplySkill(CombatSkill s)
    {
        base.ApplySkill(s);
        if (!s) return;
        if (!captured) { baseTopSpeed = topSpeed; captured = true; }
        topSpeed = baseTopSpeed * s.moveSpeedMultiplier;
    }

    protected override void HandleState(CombatState from, CombatState to)
    {
        base.HandleState(from, to);
        if (to == CombatState.Staggered) speed *= 0.2f; // the horse rears
    }

    protected override bool CanSwitchTargetNow() => mode != Mode.Charge || modeTime < 0.3f;

    // ================= Loop =================

    void Update()
    {
        if (self.State == CombatState.Dead) { speed = 0f; return; }
        thinkTimer -= Time.deltaTime;
        if (thinkTimer <= 0f)
        {
            thinkTimer += thinkInterval;
            Think();
        }
        if (animator) animator.SetFloat(SpeedHash, speed);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (self.State == CombatState.Dead) { rb.linearVelocity = Vector2.zero; return; }
        if (self.IsFrozen) { rb.linearVelocity = Vector2.zero; return; } // hit-stop

        // Bodies and walls in the way bleed off speed (the physics solver already slowed us).
        float actual = rb.linearVelocity.magnitude;
        if (speed > 1f && actual < speed * 0.5f) speed = Mathf.Lerp(speed, actual, 0.5f);

        bool disabled = self.State == CombatState.Staggered;
        if (disabled)
        {
            speed = Mathf.MoveTowards(speed, 0f, accelerationRate * 3f * dt);
        }
        else
        {
            float goal = desiredSpeed;
            if (desiredDir.sqrMagnitude > 0.0001f)
            {
                float turn = turnRate * Mathf.Lerp(2f, 1f, Mathf.Clamp01(speed / Mathf.Max(0.1f, topSpeed)));
                float ang = Vector2.SignedAngle(heading, desiredDir);
                heading = Rotate(heading, Mathf.Clamp(ang, -turn * dt, turn * dt)).normalized;
                if (Mathf.Abs(ang) > 100f) goal = Mathf.Min(goal, topSpeed * 0.45f); // slow down to make a hard turn
            }
            float rate = goal > speed ? accelerationRate : accelerationRate * 1.8f;
            speed = Mathf.MoveTowards(speed, goal, rate * dt);
        }

        rb.linearVelocity = heading * speed + self.ForcedVelocity;
        if (!disabled && speed >= chargeMinSpeed) ChargeContacts();
    }

    // ================= Thinking =================

    void Think()
    {
        ChooseTarget();
        modeTime += thinkInterval;
        Vector2 pos = transform.position;

        if (!target)
        {
            desiredDir = heading;
            desiredSpeed = topSpeed * 0.3f;
            AvoidWalls();
            self.SetAim(heading);
            return;
        }

        Vector2 tpos = target.transform.position;
        Vector2 to = tpos - pos;
        float dist = to.magnitude;

        switch (mode)
        {
            case Mode.Charge:
            {
                // Intercept a moving target. Better riders lead more accurately.
                Vector2 tvel = target.Body ? target.Body.linearVelocity : Vector2.zero;
                float eta = dist / Mathf.Max(2f, speed);
                Vector2 aimPoint = tpos + tvel * eta * Mathf.Lerp(0.2f, 1f, Sk.tacticalChance);
                desiredDir = Rotate(aimPoint - pos, chargeJitter);
                desiredSpeed = topSpeed;

                // Experienced riders veer off from a braced spear wall instead of impaling themselves.
                if (!abortRolled && dist < 5f && IsBracedAgainstMe(target))
                {
                    abortRolled = true;
                    if (Random.value < Sk.tacticalChance)
                    {
                        BeginRideOut(Rotate(heading, Random.value < 0.5f ? 70f : -70f));
                        break;
                    }
                }

                TrySwing(dist, to);

                bool passed = dist < 1.1f || (Vector2.Dot(heading, to) < 0f && dist < 3f);
                if (passed) BeginRideOut(heading);
                else if (speed < 1.2f && modeTime > 1.5f) { mode = Mode.Brawl; modeTime = 0f; } // bogged down
                break;
            }

            case Mode.RideOut:
                desiredDir = rideOutPoint - pos;
                desiredSpeed = topSpeed;
                TrySwing(dist, to);
                if ((rideOutPoint - pos).sqrMagnitude < 2.25f || modeTime > 3f) BeginCharge();
                break;

            case Mode.Brawl:
                // Stuck in a crowd: fight from the saddle, then break away and reform.
                desiredDir = to;
                desiredSpeed = dist > 1.5f ? topSpeed * 0.35f : 0f;
                TrySwing(dist, to);
                if (modeTime > 3f + Random.value * 2f)
                {
                    Vector2 away = -to.normalized;
                    BeginRideOut(Rotate(away, Random.Range(-40f, 40f)));
                }
                break;
        }

        self.SetAim(mode == Mode.Brawl || dist < 3f ? to : heading);
        AvoidWalls();
    }

    void TrySwing(float dist, Vector2 to)
    {
        if (self.State != CombatState.Idle || Time.time < nextSwingTime) return;
        var list = MeleeAttacks;
        if (list == null || list.Length == 0) return;
        var atk = list[Random.Range(0, list.Length)];
        if (!atk) return;

        float judge = Random.Range(Sk.rangeJudgement.x, Sk.rangeJudgement.y);
        if (dist > atk.reach * judge + 0.3f) return;
        if (self.Stats.Stamina < atk.staminaCost + Sk.staminaReserve * 0.5f) return;
        if (Random.value < Sk.tacticalChance && AllyInArc(atk)) return;

        self.SetAim(Rotate(to, Random.Range(-Sk.aimError, Sk.aimError)));
        self.RequestAttack(atk);
        nextSwingTime = Time.time + atk.windup + atk.recovery + Random.Range(Sk.attackCadence.x, Sk.attackCadence.y);
    }

    void BeginRideOut(Vector2 dir)
    {
        mode = Mode.RideOut;
        modeTime = 0f;
        Vector2 pos = transform.position;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : heading;
        float d = Random.Range(rideOutDistance.x, rideOutDistance.y);
        float wall = WallDistance(pos, dir, d + 2f);
        if (wall < d + 2f) d = Mathf.Max(1.5f, wall - 2f);
        rideOutPoint = pos + dir * d;
    }

    void BeginCharge()
    {
        mode = Mode.Charge;
        modeTime = 0f;
        abortRolled = false;
        chargeJitter = Random.Range(-Sk.aimError, Sk.aimError) * 0.5f;
        retargetTimer = 0f; // pick a fresh target for the next pass
    }

    // ================= Charge contact =================

    void ChargeContacts()
    {
        if (!chargeAttack) return;
        Vector2 pos = rb.position;
        Vector2 centre = pos + heading * (bodyRadius * 0.6f);
        int n = Physics2D.OverlapCircle(centre, chargeHitRadius, unitFilter, overlap);
        float now = Time.time;
        if (lastHit.Count > 64) lastHit.Clear();

        for (int i = 0; i < n; i++)
        {
            var victim = overlap[i].GetComponentInParent<CombatStateMachine>();
            if (!victim || victim == self || victim.State == CombatState.Dead) continue;
            if (lockedTarget && victim != lockedTarget) continue;

            Vector2 toV = (Vector2)victim.transform.position - pos;
            if (toV.sqrMagnitude > 0.0001f && Vector2.Dot(heading, toV.normalized) < 0.25f) continue; // only what's in front

            bool ally = victim.team == self.team;
            if (ally && chargeAttack.allyDamageMultiplier <= 0f) continue;
            if (lastHit.TryGetValue(victim, out float t) && now - t < rehitCooldown) continue;
            lastHit[victim] = now;

            float power = Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(chargeMinSpeed, topSpeed, speed));

            if (!ally && IsBracedAgainstMe(victim))
            {
                Impaled(victim, power);
                return;
            }

            float mult = power * self.outgoingDamageMultiplier * (ally ? chargeAttack.allyDamageMultiplier : 1f);
            var result = victim.ReceiveHit(self, chargeAttack, mult, pos - heading);
            if (result.type == HitType.Ignored || result.type == HitType.Dodged) continue;

            self.ReportHitLanded(result);
            speed *= 1f - speedLossPerHit * (victim.isMounted ? 3f : 1f);
            if (speed < chargeMinSpeed) return;
        }
    }

    /// <summary>Rode onto a braced spear: the horse stops dead and the rider takes the spear.</summary>
    void Impaled(CombatStateMachine spearman, float power)
    {
        var w = spearman.Loadout ? spearman.Loadout.weapon : null;
        if (w && w.braceAttack)
        {
            var r = self.ReceiveHit(spearman, w.braceAttack, power * spearman.outgoingDamageMultiplier, (Vector2)spearman.transform.position);
            if (r.type != HitType.Ignored && r.type != HitType.Dodged) spearman.ReportHitLanded(r);
        }

        // The spearman still takes part of the impact.
        var r2 = spearman.ReceiveHit(self, chargeAttack, power * 0.3f * self.outgoingDamageMultiplier, (Vector2)transform.position - heading);
        if (r2.type != HitType.Ignored && r2.type != HitType.Dodged) self.ReportHitLanded(r2);

        speed = 0f;
        mode = Mode.Brawl;
        modeTime = 0f;
    }

    bool IsBracedAgainstMe(CombatStateMachine v)
    {
        if (!v || !v.Loadout || !v.Loadout.Braces) return false;
        var s = v.State;
        if (s == CombatState.Staggered || s == CombatState.Dodging || s == CombatState.Dead || s == CombatState.Recovery) return false;
        Vector2 toMe = (Vector2)transform.position - (Vector2)v.transform.position;
        return Vector2.Angle(v.Facing, toMe) <= 60f;
    }

    // ================= Targeting & walls =================

    protected override float ScoreTarget(CombatStateMachine c)
    {
        float s = Vector2.Distance(transform.position, c.transform.position) * 0.6f; // riders cover ground fast
        var tk = c.Tokens;
        int others = tk ? tk.EngagedCount - (c == target ? 1 : 0) : 0;
        s += crowdPenalty * Mathf.Max(0, others);
        var lo = c.Loadout;
        if (lo && lo.CanShoot) s -= targetArchersBias;
        if (lo && lo.Braces) s += 10f * Sk.tacticalChance; // experienced riders avoid spears
        if (c.isMounted) s += 4f;
        if (c.isPlayer) s -= playerBias;
        return s;
    }

    void AvoidWalls()
    {
        if (desiredDir.sqrMagnitude < 0.0001f) return;
        Vector2 pos = transform.position;
        Vector2 dir = desiredDir.normalized;
        float look = wallLookAhead * Mathf.Max(0.5f, speed / Mathf.Max(0.1f, topSpeed));
        if (WallDistance(pos, dir, look) >= look) return;

        Vector2 left = Rotate(dir, 70f), right = Rotate(dir, -70f);
        bool leftClear = WallDistance(pos, left, look) >= look;
        bool rightClear = WallDistance(pos, right, look) >= look;
        desiredDir = leftClear ? left : rightClear ? right : -dir;
        desiredSpeed = Mathf.Min(desiredSpeed, topSpeed * 0.6f);
    }

    /// <summary>Distance to the nearest wall (static collider) along a ray, or maxDist if none.</summary>
    float WallDistance(Vector2 pos, Vector2 dir, float maxDist)
    {
        int n = Physics2D.Raycast(pos, dir, wallFilter, rayHits, maxDist);
        float best = maxDist;
        for (int i = 0; i < n; i++)
        {
            var h = rayHits[i];
            if (h.rigidbody == null && h.distance < best) best = h.distance;
        }
        return best;
    }
}
