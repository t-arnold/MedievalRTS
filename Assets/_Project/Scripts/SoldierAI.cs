using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Foot-soldier brain: swordsmen, axemen, spearmen and archers (team decides who is hostile).
/// Melee loop: pick a target -> circle -> get an attack token -> close in -> hold the swing if an
/// ally is in the arc -> attack -> release token.
/// Archers: keep their distance, lead moving targets and shoot; switch to a sidearm when enemies close in
/// or arrows run out. Careful (experienced) archers won't shoot into a melee their own side is in.
/// Everyone: raises shields when arrows are about to land on them; spearmen brace against charging
/// horses, others try to dodge or block. HOW WELL they do each step comes from the CombatSkill tier.
/// </summary>
public class SoldierAI : UnitBrain
{
    /// <summary>Archers currently shooting (so infantry can watch for incoming volleys cheaply).</summary>
    public static readonly List<SoldierAI> Shooters = new List<SoldierAI>();

    [Header("Infantry")]
    public float acceleration = 20f;
    [Tooltip("Distance to circle at while waiting for an attack token.")]
    public float waitDistance = 2.6f;

    [Header("Archery")]
    [Tooltip("Closer than this, archers stop shooting and use their sidearm.")]
    public float meleeSwitchDistance = 2.8f;

    public bool InRangedMode { get; private set; }

    // melee planning
    AttackData nextAttack;
    float attackTriggerScale = 0.85f;
    float aimJitter;
    float nextAttackAllowedTime;
    bool wantPunish, punishRolled;

    // ranged planning
    float nextShotTime;
    float shotAimJitter, rangeErrorFactor = 1f;

    // movement
    Vector2 desiredVelocity, velocity;
    int circleSign;

    // scheduled reactions
    bool threatSeen, pendingDodge;
    float blockStartAt, blockUntil, dodgeAt;
    Vector2 dodgeDir;
    float shieldUntil; Vector2 shieldFrom;      // raise the shield toward this point (arrows, charges)
    float braceUntil; CavalryAI braceRider;     // spearmen: plant feet and face the horse
    CavalryAI chargeSeen; float chargeSeenUntil;
    float arrowAlertUntil;

    protected override void Awake()
    {
        base.Awake();
        circleSign = Random.value < 0.5f ? -1 : 1;
    }

    protected override void Start()
    {
        base.Start();
        if (loadout && loadout.CanShoot)
            aggroRange = Mathf.Max(aggroRange, loadout.weapon.rangedAttack.projectile.maxRange + 2f);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Shooters.Remove(this);
    }

    protected override void HandleState(CombatState from, CombatState to)
    {
        base.HandleState(from, to);
        bool swingFinished = to == CombatState.Idle && from == CombatState.Recovery;
        if (swingFinished && skill)
        {
            float pause = Random.Range(skill.attackCadence.x, skill.attackCadence.y);
            if (InRangedMode) nextShotTime = Time.time + pause + 0.3f;
            else nextAttackAllowedTime = Time.time + pause;
        }

        if (to == CombatState.Staggered || to == CombatState.Dead)
        {
            pendingDodge = false;
            blockUntil = 0f;
            braceUntil = 0f;
        }
        if (to == CombatState.Dead) SetRanged(false);
    }

    protected override void OnTargetChanged()
    {
        nextAttack = null;
        threatSeen = false;
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
            Think();
        }

        // Scheduled reactions (this is where reaction time and timing error play out).
        if (pendingDodge && Time.time >= dodgeAt)
        {
            pendingDodge = false;
            self.RequestDodge(dodgeDir);
        }
        self.SetBlock(Time.time >= blockStartAt && Time.time < blockUntil);

        bool braced = Time.time < braceUntil;
        Vector2 want = self.CanMove && !braced ? desiredVelocity : Vector2.zero;
        velocity = Vector2.MoveTowards(velocity, want, acceleration * dt);
        if (animator) animator.SetFloat(SpeedHash, velocity.magnitude, 0.05f, dt);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = velocity + self.ForcedVelocity;
    }

    // ================= Thinking =================

    void Think()
    {
        ChooseTarget();
        ReactToCharges();
        ReactToArrows();

        if (!target)
        {
            SetRanged(false);
            desiredVelocity = Separation() * moveSpeed;
        }
        else
        {
            Vector2 toTarget = target.transform.position - transform.position;
            float dist = toTarget.magnitude;
            Vector2 dir = dist > 0.001f ? toTarget / dist : Vector2.down;

            bool shoot = loadout && loadout.CanShoot && dist > meleeSwitchDistance;
            SetRanged(shoot);
            if (shoot) ThinkRanged(dist, dir);
            else ThinkMelee(toTarget, dist, dir);
        }

        // Defensive facing overrides whatever the attack plan wanted.
        Vector2 pos = transform.position;
        if (Time.time < braceUntil && braceRider) self.SetAim((Vector2)braceRider.transform.position - pos);
        else if (Time.time < shieldUntil) self.SetAim(shieldFrom - pos);
    }

    void SetRanged(bool on)
    {
        if (on == InRangedMode) return;
        InRangedMode = on;
        if (on)
        {
            Shooters.Add(this);
            ReleaseToken();
            nextAttack = null;
        }
        else Shooters.Remove(this);
    }

    // ---------------- Melee ----------------

    void ThinkMelee(Vector2 toTarget, float dist, Vector2 dir)
    {
        if (!nextAttack) PlanAttack(dist);
        self.SetAim(Rotate(toTarget, aimJitter));

        ReadThreat(toTarget, dist);

        bool tired = self.Stats.Stamina < Sk.staminaReserve;
        bool ready = Time.time >= nextAttackAllowedTime;
        bool braced = Time.time < braceUntil;

        // Spot an opening: opponent recovering from a swing or staggered.
        bool open = target.State == CombatState.Recovery || target.State == CombatState.Staggered;
        if (open && !punishRolled) { punishRolled = true; wantPunish = Random.value < Sk.punishChance; }
        if (!open) { punishRolled = false; wantPunish = false; }
        bool punishing = wantPunish && open && !tired;

        if (!holdingToken && !tired && !braced && (ready || punishing) && dist < waitDistance + 1.5f)
        {
            holdingToken = targetTokens == null || targetTokens.TryAcquire(this);
            if (holdingToken) tokenTime = Time.time;
        }

        Vector2 move;
        if (holdingToken)
        {
            float triggerDist = nextAttack ? nextAttack.reach * attackTriggerScale : 1.2f;
            if (target.isMounted) triggerDist += 0.2f; // horses are big
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
        var list = MeleeAttacks;
        if (list == null || list.Length == 0) return;

        nextAttack = null;
        if (Random.value < Sk.tacticalChance)
        {
            AttackData unblockable = null, longest = null, fastest = null, antiHorse = null;
            foreach (var a in list)
            {
                if (!a) continue;
                if (a.telegraph == TelegraphType.Unblockable) unblockable = a;
                if (!longest || a.reach > longest.reach) longest = a;
                if (a.blockable && (!fastest || a.windup < fastest.windup)) fastest = a;
                if (a.vsMountedDamageMultiplier > 1f && (!antiHorse || a.vsMountedDamageMultiplier > antiHorse.vsMountedDamageMultiplier)) antiHorse = a;
            }
            if (target.isMounted && antiHorse) nextAttack = antiHorse;                           // spear the horse
            else if (target.State == CombatState.Blocking && unblockable) nextAttack = unblockable; // beat the turtle
            else if (dist > 1.5f && longest) nextAttack = longest;                                 // use reach
            else nextAttack = fastest;                                                             // quick and safe
        }
        if (!nextAttack) nextAttack = list[Random.Range(0, list.Length)];

        attackTriggerScale = Random.Range(Sk.rangeJudgement.x, Sk.rangeJudgement.y);
        aimJitter = Random.Range(-Sk.aimError, Sk.aimError);
    }

    /// <summary>Notice an incoming melee attack and schedule a block, parry or dodge (or fail to).</summary>
    void ReadThreat(Vector2 toTarget, float dist)
    {
        var atk = target.CurrentAttack;
        bool threatened = target.State == CombatState.Windup && atk && !atk.projectile
                       && dist < atk.reach + 0.6f
                       && Vector2.Angle(target.Facing, -toTarget) < atk.arcAngle * 0.5f + 15f;

        if (!threatened) { threatSeen = false; return; }
        if (threatSeen) return;
        threatSeen = true;

        if (Random.value > Sk.readChance) return; // didn't see it coming

        float now = Time.time;
        float predictedHit = now + Mathf.Max(0f, atk.windup - target.StateTime);
        float reactAt = now + Random.Range(Sk.reactionTime.x, Sk.reactionTime.y);
        float noise = Random.Range(-Sk.timingError, Sk.timingError);

        if (atk.blockable && Random.value < Sk.blockChance)
        {
            bool tryParry = atk.parryable && Random.value < Sk.parryChance;
            float start = tryParry ? predictedHit - self.parryWindow * Random.Range(0.2f, 0.8f) + noise : reactAt + noise;
            blockStartAt = Mathf.Max(reactAt, start);   // can't act before reacting: slow tiers can't parry fast swings
            blockUntil = predictedHit + atk.active + 0.15f;
        }
        else if (Random.value < (atk.blockable ? Sk.dodgeChance : Sk.dodgeUnblockableChance))
        {
            dodgeAt = Mathf.Max(reactAt, predictedHit - 0.15f + noise); // aim i-frames at the moment of impact
            dodgeDir = new Vector2(-toTarget.y, toTarget.x).normalized * (Random.value < 0.5f ? -1f : 1f);
            pendingDodge = true;
        }
    }

    // ---------------- Ranged ----------------

    void ThinkRanged(float dist, Vector2 dir)
    {
        var w = loadout.weapon;
        var shot = w.rangedAttack;
        var proj = shot.projectile;
        float preferred = Mathf.Min(w.preferredRange, proj.maxRange - 1f);

        // Hold a firing position: close to range, back off if they get too near, otherwise stand.
        Vector2 move = Vector2.zero;
        if (dist > proj.maxRange - 0.5f) move = dir;
        else if (dist < preferred - 2.5f) move = -dir * 0.8f;
        move += Separation() * 1.2f;
        desiredVelocity = Vector2.ClampMagnitude(move, 1f) * moveSpeed;

        // Aim: lead a moving target. Better tiers lead more accurately.
        Vector2 targetPos = target.transform.position;
        Vector2 targetVel = target.Body ? target.Body.linearVelocity : Vector2.zero;
        float flight = dist / Mathf.Max(1f, proj.speed);
        Vector2 predicted = targetPos + targetVel * flight * Mathf.Lerp(0.2f, 1f, Sk.tacticalChance);
        Vector2 toAim = predicted - (Vector2)transform.position;
        self.SetAim(Rotate(toAim, shotAimJitter) * rangeErrorFactor);

        if (self.State != CombatState.Idle || Time.time < nextShotTime || dist < proj.minRange) return;
        if (self.Stats.Stamina < shot.staminaCost + 1f) return;

        // Careful archers won't loose into a melee their own side is in. Levies don't care.
        if (Random.value < Sk.tacticalChance && AlliesNear(predicted, 1.4f) > 0)
        {
            nextShotTime = Time.time + 0.5f;
            retargetTimer = 0f; // look for a cleaner target
            return;
        }

        // Each shot gets its own error: worse tiers miss wide and long/short.
        shotAimJitter = Random.Range(-Sk.aimError, Sk.aimError) * 0.4f;
        rangeErrorFactor = 1f + Random.Range(-1f, 1f) * Sk.aimError / 100f;
        self.RequestAttack(shot);
        nextShotTime = Time.time + shot.windup + 1.5f; // fallback; set properly when the shot finishes
    }

    // ---------------- Reactions ----------------

    /// <summary>Arrows about to land on me: raise the shield toward the archer.</summary>
    void ReactToArrows()
    {
        if (InRangedMode || Time.time < arrowAlertUntil || holdingToken) return;
        if (!loadout || !loadout.weapon || !loadout.weapon.hasShield) return;

        Vector2 pos = transform.position;
        foreach (var s in Shooters)
        {
            if (!s) continue;
            var c = s.Self;
            if (!c || c.team == self.team || c.State != CombatState.Windup) continue;
            var atk = c.CurrentAttack;
            if (!atk || !atk.projectile) continue;
            if ((c.AimPoint - pos).sqrMagnitude > 2.25f) continue;

            arrowAlertUntil = Time.time + 1.5f; // one roll per volley
            if (Random.value > Sk.readChance * 0.8f) return;

            float d = Vector2.Distance(pos, c.transform.position);
            float hitAt = Time.time + Mathf.Max(0f, atk.windup - c.StateTime) + d / Mathf.Max(1f, atk.projectile.speed);
            blockStartAt = Time.time + Random.Range(Sk.reactionTime.x, Sk.reactionTime.y);
            blockUntil = Mathf.Max(blockUntil, hitAt + 0.3f);
            shieldFrom = c.transform.position;
            shieldUntil = blockUntil;
            return;
        }
    }

    /// <summary>A horse is charging straight at me: brace (spears), dodge, or block.</summary>
    void ReactToCharges()
    {
        Vector2 pos = transform.position;
        foreach (var r in CavalryAI.Riders)
        {
            if (!r || r.Team == self.team || !r.IsCharging) continue;
            Vector2 rp = r.transform.position;
            Vector2 v = r.Velocity;
            Vector2 toMe = pos - rp;
            float d = toMe.magnitude;
            float speed = v.magnitude;
            if (d > 7f || d < 0.3f || speed < 0.1f) continue;
            if (Vector2.Angle(v, toMe) > 25f) continue;
            float eta = d / speed;
            if (eta > 1.4f) continue;

            if (chargeSeen == r && Time.time < chargeSeenUntil) return;
            chargeSeen = r;
            chargeSeenUntil = Time.time + 2f;
            if (Random.value > Sk.readChance) return; // never saw it coming

            float now = Time.time;
            float react = now + Random.Range(Sk.reactionTime.x, Sk.reactionTime.y);
            float hitAt = now + eta;
            float noise = Random.Range(-Sk.timingError, Sk.timingError);

            if (loadout && loadout.Braces)
            {
                braceRider = r;
                braceUntil = hitAt + 0.4f;
                ReleaseToken();
            }
            else if (react < hitAt && Random.value < Sk.dodgeUnblockableChance)
            {
                dodgeAt = Mathf.Max(react, hitAt - 0.15f + noise);
                dodgeDir = new Vector2(-v.y, v.x).normalized * (Random.value < 0.5f ? -1f : 1f);
                pendingDodge = true;
            }
            else if (loadout && loadout.weapon && loadout.weapon.hasShield)
            {
                blockStartAt = react;
                blockUntil = hitAt + 0.3f;
                shieldFrom = rp;
                shieldUntil = blockUntil;
            }
            return;
        }
    }

    // ================= Targeting =================

    protected override float ScoreTarget(CombatStateMachine c)
    {
        float s = base.ScoreTarget(c);
        if (loadout && loadout.CanShoot)
        {
            // Archers: prefer targets their own side isn't already fighting, and reach doesn't matter much.
            s = Vector2.Distance(transform.position, c.transform.position) * 0.5f;
            s += AlliesNear(c.transform.position, 1.5f) * 4f * Sk.tacticalChance;
        }
        if (c.isMounted && loadout && loadout.Braces) s -= 3f; // spearmen go for horses
        return s;
    }
}
