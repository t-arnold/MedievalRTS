using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Measures matchups: runs many 1v1 NPC duels in parallel (unit + tier A vs unit + tier B), records
/// wins, draws, average duel length and the winner's remaining HP.
/// Added automatically by TestArena. Keys: 6 start/stop, 7/8 tier A/B, Shift+7/Shift+8 unit A/B.
/// Cavalry and archers need room, so those matchups run in 2 wide lanes instead of 6 small cells.
/// XP gain is paused while it runs so tiers stay fixed.
/// </summary>
public class DuelTester : MonoBehaviour
{
    [Range(1, 6)] public int simultaneousDuels = 6;
    public int duelsPerSeries = 40;
    [Tooltip("Game speed while testing. Higher = faster results, more CPU.")]
    public float timeScale = 3f;
    [Tooltip("Game-time seconds before a duel counts as a draw.")]
    public float duelTimeout = 60f;

    public bool Running { get; private set; }
    public int IndexA { get; private set; } = 0;  // Levy
    public int IndexB { get; private set; } = 3;  // Veteran
    public int UnitA { get; private set; } = 0;   // Swordsman
    public int UnitB { get; private set; } = 0;
    public readonly List<string> History = new List<string>();

    class Duel
    {
        public Vector2 centre;
        public float spread;
        public CombatStateMachine a, b;
        public float startTime, doneAt;
        public bool done = true;
    }

    TestArena arena;
    readonly List<Duel> duels = new List<Duel>();
    int started, finished, winsA, winsB, draws;
    float totalDuration, winnerHpSum;

    CombatSkill[] Ladder => arena.SkillLadder;
    UnitType[] Units => arena.UnitTypes;
    public string NameA => $"{Ladder[IndexA].tierName} {Units[UnitA].shortName}";
    public string NameB => $"{Ladder[IndexB].tierName} {Units[UnitB].shortName}";

    void Awake() => arena = GetComponent<TestArena>();

    public void CycleA() { if (!Running) IndexA = (IndexA + 1) % Ladder.Length; }
    public void CycleB() { if (!Running) IndexB = (IndexB + 1) % Ladder.Length; }
    public void CycleUnitA() { if (!Running) UnitA = (UnitA + 1) % Units.Length; }
    public void CycleUnitB() { if (!Running) UnitB = (UnitB + 1) % Units.Length; }

    public void Toggle()
    {
        if (Running) End(false);
        else Begin();
    }

    public string Status
    {
        get
        {
            if (finished == 0 && !Running) return "no results yet";
            int decided = winsA + winsB;
            float pctA = decided > 0 ? 100f * winsA / decided : 0f;
            float avg = finished > 0 ? totalDuration / finished : 0f;
            float hp = decided > 0 ? winnerHpSum / decided : 0f;
            return $"{NameA} {winsA} – {winsB} {NameB}  (draws {draws}, {finished}/{duelsPerSeries})  ·  A wins {pctA:0}%  ·  avg {avg:0.0}s  ·  winner HP left {hp:0%}";
        }
    }

    void Begin()
    {
        arena.ClearNpcs();
        arena.SetPillarsActive(false);
        CombatExperience.GainEnabled = false;
        started = finished = winsA = winsB = draws = 0;
        totalDuration = winnerHpSum = 0f;
        Time.timeScale = timeScale;
        Time.fixedDeltaTime = 0.02f;

        duels.Clear();
        bool wide = NeedsRoom(Units[UnitA]) || NeedsRoom(Units[UnitB]);
        if (wide)
        {
            duels.Add(new Duel { centre = new Vector2(0f, 7f), spread = 7f });
            duels.Add(new Duel { centre = new Vector2(0f, -7f), spread = 7f });
        }
        else
        {
            Vector2[] centres =
            {
                new Vector2(-9f, 4.5f), new Vector2(0f, 4.5f), new Vector2(9f, 4.5f),
                new Vector2(-9f, -4.5f), new Vector2(0f, -4.5f), new Vector2(9f, -4.5f),
            };
            for (int i = 0; i < simultaneousDuels; i++) duels.Add(new Duel { centre = centres[i], spread = 1.6f });
        }

        Running = true;
        foreach (var d in duels) StartDuel(d);
    }

    static bool NeedsRoom(UnitType t) => t && (t.mounted || t.role == UnitRole.Archer || (t.weapon && t.weapon.IsRanged));

    void StartDuel(Duel d)
    {
        if (d.a) Destroy(d.a.gameObject);
        if (d.b) Destroy(d.b.gameObject);

        float side = Random.value < 0.5f ? 1f : -1f; // no positional advantage
        d.a = arena.SpawnUnit(Units[UnitA], 0, d.centre + new Vector2(-d.spread * side, 0f), TestArena.AllyColor, Ladder[IndexA]);
        d.b = arena.SpawnUnit(Units[UnitB], 1, d.centre + new Vector2(d.spread * side, 0f), TestArena.EnemyColor, Ladder[IndexB]);
        var ba = d.a.GetComponent<UnitBrain>();
        var bb = d.b.GetComponent<UnitBrain>();
        if (ba) ba.lockedTarget = d.b;
        if (bb) bb.lockedTarget = d.a;
        var ca = d.a.GetComponent<CavalryAI>();
        var cb = d.b.GetComponent<CavalryAI>();
        if (ca) ca.SetHeading(d.b.transform.position - d.a.transform.position);
        if (cb) cb.SetHeading(d.a.transform.position - d.b.transform.position);
        d.startTime = Time.time;
        d.done = false;
        started++;
    }

    void Update()
    {
        if (!Running) return;

        bool anyActive = false;
        foreach (var d in duels)
        {
            if (d.done)
            {
                if (started < duelsPerSeries && Time.time - d.doneAt > 1f) StartDuel(d);
                if (!d.done) anyActive = true;
                continue;
            }
            anyActive = true;

            bool aDead = !d.a || d.a.State == CombatState.Dead;
            bool bDead = !d.b || d.b.State == CombatState.Dead;
            bool timeout = Time.time - d.startTime > duelTimeout;
            if (!aDead && !bDead && !timeout) continue;

            if (aDead && !bDead) { winsB++; winnerHpSum += d.b.Stats.Health / d.b.Stats.maxHealth; }
            else if (bDead && !aDead) { winsA++; winnerHpSum += d.a.Stats.Health / d.a.Stats.maxHealth; }
            else draws++;

            totalDuration += Time.time - d.startTime;
            finished++;
            d.done = true;
            d.doneAt = Time.time;
        }

        if (!anyActive && started >= duelsPerSeries) End(true);
    }

    void End(bool completed)
    {
        Running = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        CombatExperience.GainEnabled = true;
        arena.SetPillarsActive(true);

        foreach (var d in duels)
        {
            if (d.a) Destroy(d.a.gameObject);
            if (d.b) Destroy(d.b.gameObject);
        }
        duels.Clear();

        if (finished > 0)
        {
            string line = (completed ? "" : "[stopped] ") + Status;
            History.Insert(0, line);
            if (History.Count > 8) History.RemoveAt(History.Count - 1);
            Debug.Log("Duel series: " + line);
        }
    }
}
