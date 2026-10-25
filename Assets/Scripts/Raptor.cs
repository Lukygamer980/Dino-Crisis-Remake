using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ponytail: the raptor paths over the NavMesh baked by the scene builder through a NavMeshAgent,
// so it follows the player around props and through doorways. The agent only plans and moves;
// turning stays here at turnSpeed, so the raptor still swings its body round before it lunges.
// Unlocked sliding doors aren't part of the NavMesh at all: they open for a raptor walking up to
// them. The CharacterController is kept only as the body that shots and sight lines hit.
[RequireComponent(typeof(NavMeshAgent), typeof(CharacterController))]
public class Raptor : MonoBehaviour
{
    public static readonly List<Raptor> All = new();

    public float wanderSpeed = 1.3f;
    public float investigateSpeed = 3f;
    public float searchSpeed = 2.4f;
    public float chaseSpeed = 4.7f;
    public float turnSpeed = 280f;
    public float sightRange = 11f;
    public float attackRange = 1.9f;
    public float attackWindUp = 0.45f;
    public float attackRecovery = 1.1f;
    public int attackDamage = 18;
    public int health = 8;
    public float staggerDuration = 0.25f;

    [Header("Patrol and search")]
    // Patrol points, visited in order (assigned by the scene builder).
    public Transform[] waypoints = new Transform[0];
    public float patrolPause = 2f;
    // How long the player can stay out of sight before the raptor stops chasing and searches.
    public float losePlayerTime = 1.5f;
    // How long it looks around at a noise or the player's last seen position before giving up.
    public float lookAroundDuration = 6f;
    // Its screech on spotting the player calls other raptors this close over to look.
    public float packCallRadius = 14f;

    enum State { Patrol, Investigate, Chase, Search, LookAround, WindUp, Recover, Stagger, Dead }

    State state = State.Patrol;
    float stateTimer;
    float timeWithoutSight;
    int waypointIndex;
    bool pausedAtWaypoint;
    Vector3 lastSeenPosition;
    Vector3 lookTarget;
    float nextGlanceTime;
    Vector3 knockback;
    PlayerController player;
    NavMeshAgent agent;
    CharacterController body;
    Renderer[] bodyRenderers;
    Color[] originalColors;

    void OnEnable() => All.Add(this);

    void OnDisable() => All.Remove(this);

    // In Awake, so a raptor woken mid-frame (by the generator) can already react to noise.
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        body = GetComponent<CharacterController>();
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
        bodyRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++) originalColors[i] = bodyRenderers[i].material.color;
        // Unless a noise already sent it somewhere.
        if (state == State.Patrol) ResumePatrol();
    }

    void Update()
    {
        if (state == State.Dead) return;

        stateTimer -= Time.deltaTime;
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;
        bool seesPlayer = !player.IsDead && distanceToPlayer < sightRange && HasLineOfSight();

        // Knockback and the lunge push the agent along the NavMesh, so they can't shove it into walls.
        if (knockback.sqrMagnitude > 0.0001f) agent.Move(knockback * Time.deltaTime);
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 10f * Time.deltaTime);

        bool busy = state is State.WindUp or State.Recover or State.Stagger;
        agent.isStopped = busy;
        if (seesPlayer && !busy)
        {
            lastSeenPosition = player.transform.position;
            Alert();
        }

        switch (state)
        {
            case State.Patrol:
                if (waypoints.Length == 0 || !Arrived()) break;
                if (!pausedAtWaypoint)
                {
                    pausedAtWaypoint = true;
                    stateTimer = patrolPause;
                }
                else if (stateTimer <= 0f)
                {
                    pausedAtWaypoint = false;
                    waypointIndex = (waypointIndex + 1) % waypoints.Length;
                    GoTo(waypoints[waypointIndex].position, wanderSpeed);
                }
                break;

            case State.Investigate:
            case State.Search:
                if (Arrived()) LookAround();
                break;

            case State.LookAround:
                // Glances in a new direction every so often, then gives up and goes back to patrolling.
                if (Time.time >= nextGlanceTime)
                {
                    nextGlanceTime = Time.time + Random.Range(0.9f, 1.6f);
                    lookTarget = transform.position + Quaternion.Euler(0f, Random.Range(-140f, 140f), 0f) * transform.forward;
                }
                Face(lookTarget);
                if (stateTimer <= 0f) ResumePatrol();
                break;

            case State.Chase:
                timeWithoutSight = seesPlayer ? 0f : timeWithoutSight + Time.deltaTime;
                if (player.IsDead)
                {
                    ResumePatrol();
                }
                else if (timeWithoutSight > losePlayerTime)
                {
                    // Lost it: head for where the player was last seen.
                    state = State.Search;
                    GoTo(lastSeenPosition, searchSpeed);
                }
                else if (distanceToPlayer < attackRange)
                {
                    state = State.WindUp;
                    stateTimer = attackWindUp;
                }
                else
                {
                    GoTo(player.transform.position, chaseSpeed);
                }
                break;

            case State.WindUp:
                Face(player.transform.position);
                if (stateTimer <= 0f)
                {
                    // The lunge only connects if the player is still in reach, so stepping
                    // back during the wind-up is a real dodge.
                    knockback = transform.forward * 5f;
                    if (distanceToPlayer < attackRange + 0.5f) player.TakeDamage(attackDamage, transform.position);
                    state = State.Recover;
                    stateTimer = attackRecovery;
                }
                break;

            case State.Recover:
            case State.Stagger:
                if (stateTimer <= 0f)
                {
                    state = State.Chase;
                    timeWithoutSight = 0f;
                }
                break;
        }

        if (!busy && state != State.LookAround && agent.velocity.sqrMagnitude > 0.01f) Face(agent.steeringTarget);
    }

    void GoTo(Vector3 destination, float speed)
    {
        agent.speed = speed;
        agent.SetDestination(destination);
    }

    // Also true at the end of a partial path, when the destination itself can't be reached.
    bool Arrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;

    void Face(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
    }

    bool HasLineOfSight()
    {
        Vector3 eye = transform.position + Vector3.up * 1.3f;
        Vector3 playerChest = player.transform.position + Vector3.up * 1.2f;
        return Physics.Linecast(eye, playerChest, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<PlayerController>() != null;
    }

    void LookAround()
    {
        state = State.LookAround;
        stateTimer = lookAroundDuration;
        nextGlanceTime = 0f;
        agent.ResetPath();
    }

    // Walks back to the nearest patrol point and carries on the round from there.
    void ResumePatrol()
    {
        state = State.Patrol;
        pausedAtWaypoint = false;
        if (waypoints.Length == 0)
        {
            agent.ResetPath();
            return;
        }
        float nearest = float.MaxValue;
        for (int i = 0; i < waypoints.Length; i++)
        {
            float distance = Vector3.Distance(transform.position, waypoints[i].position);
            if (distance < nearest)
            {
                nearest = distance;
                waypointIndex = i;
            }
        }
        GoTo(waypoints[waypointIndex].position, wanderSpeed);
    }

    // Starts the chase. Screeches only when the raptor wasn't already after the player, and the
    // screech brings the rest of the pack, so raptors hunt together.
    public void Alert()
    {
        if (IsHunting) return;
        state = State.Chase;
        timeWithoutSight = 0f;
        GameUI.Instance.ShowMessage("*SCREECH*", 1.2f);
        MakeNoise(transform.position, packCallRadius);
    }

    bool IsHunting => state is State.Chase or State.WindUp or State.Recover or State.Stagger or State.Dead;

    // Something loud happened at `position`: every raptor within `radius` that isn't already
    // after the player goes there to look.
    public static void MakeNoise(Vector3 position, float radius)
    {
        foreach (Raptor raptor in All)
        {
            if (Vector3.Distance(raptor.transform.position, position) < radius) raptor.Investigate(position);
        }
    }

    void Investigate(Vector3 position)
    {
        if (IsHunting) return;
        state = State.Investigate;
        GoTo(position, investigateSpeed);
    }

    public void TakeHit(int damage, Vector3 direction)
    {
        if (state == State.Dead) return;
        health -= damage;
        CancelInvoke(nameof(RestoreColors));
        foreach (Renderer bodyRenderer in bodyRenderers) bodyRenderer.material.color = Color.red;
        Invoke(nameof(RestoreColors), 0.08f);

        if (health <= 0)
        {
            state = State.Dead;
            agent.enabled = false;
            body.enabled = false;
            transform.Rotate(0f, 0f, 90f);
            transform.position += Vector3.up * 0.4f;
            All.Remove(this);
            return;
        }

        direction.y = 0f;
        knockback = direction.normalized * 2.5f;
        state = State.Stagger;
        stateTimer = staggerDuration;
        // Being shot gives the shooter away.
        lastSeenPosition = player.transform.position;
    }

    void RestoreColors()
    {
        for (int i = 0; i < bodyRenderers.Length; i++) bodyRenderers[i].material.color = originalColors[i];
    }
}
