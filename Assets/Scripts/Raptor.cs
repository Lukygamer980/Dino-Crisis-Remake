using System.Collections.Generic;
using UnityEngine;

// ponytail: steers straight at its target and slides along walls through the
// CharacterController, with no pathfinding. That holds for open rooms but gets stuck
// behind large obstacles, and a raptor chasing the player opens a door it walks into but can't
// find its way through one; the upgrade path is a NavMeshAgent (AI Navigation package).
[RequireComponent(typeof(CharacterController))]
public class Raptor : MonoBehaviour
{
    public static readonly List<Raptor> All = new();

    public float wanderSpeed = 1.3f;
    public float chaseSpeed = 4.7f;
    public float turnSpeed = 280f;
    public float sightRange = 11f;
    public float attackRange = 1.9f;
    public float attackWindUp = 0.45f;
    public float attackRecovery = 1.1f;
    public int attackDamage = 18;
    public int health = 8;
    public float staggerDuration = 0.25f;

    enum State { Wander, Chase, WindUp, Recover, Stagger, Dead }

    State state = State.Wander;
    float stateTimer;
    float timeWithoutSight;
    Vector3 home;
    Vector3 wanderTarget;
    Vector3 knockback;
    float verticalVelocity;
    PlayerController player;
    CharacterController controller;
    Renderer[] bodyRenderers;
    Color[] originalColors;

    void OnEnable() => All.Add(this);

    void OnDisable() => All.Remove(this);

    void Start()
    {
        controller = GetComponent<CharacterController>();
        player = FindAnyObjectByType<PlayerController>();
        bodyRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++) originalColors[i] = bodyRenderers[i].material.color;
        home = transform.position;
        PickWanderTarget();
    }

    void Update()
    {
        if (state == State.Dead) return;

        stateTimer -= Time.deltaTime;
        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;
        bool seesPlayer = !player.IsDead && distanceToPlayer < sightRange && HasLineOfSight();
        Vector3 motion = knockback;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 10f * Time.deltaTime);

        switch (state)
        {
            case State.Wander:
                motion += SteerTowards(wanderTarget, wanderSpeed);
                if (stateTimer <= 0f || Vector3.Distance(transform.position, wanderTarget) < 0.6f) PickWanderTarget();
                if (seesPlayer) Alert();
                break;

            case State.Chase:
                timeWithoutSight = seesPlayer ? 0f : timeWithoutSight + Time.deltaTime;
                if (player.IsDead || timeWithoutSight > 4f)
                {
                    PickWanderTarget();
                }
                else if (distanceToPlayer < attackRange)
                {
                    state = State.WindUp;
                    stateTimer = attackWindUp;
                }
                else
                {
                    motion += SteerTowards(player.transform.position, chaseSpeed);
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
                if (stateTimer <= 0f) state = State.Chase;
                break;
        }

        verticalVelocity = controller.isGrounded ? -1f : verticalVelocity - 20f * Time.deltaTime;
        motion.y = verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }

    Vector3 SteerTowards(Vector3 target, float speed)
    {
        Face(target);
        return transform.forward * speed;
    }

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

    void PickWanderTarget()
    {
        state = State.Wander;
        Vector2 offset = Random.insideUnitCircle * 4f;
        wanderTarget = home + new Vector3(offset.x, 0f, offset.y);
        stateTimer = Random.Range(3f, 6f);
    }

    public void Alert()
    {
        if (state != State.Wander) return;
        state = State.Chase;
        timeWithoutSight = 0f;
        GameUI.Instance.ShowMessage("*SCREECH*", 1.2f);
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
            controller.enabled = false;
            transform.Rotate(0f, 0f, 90f);
            transform.position += Vector3.up * 0.4f;
            All.Remove(this);
            return;
        }

        direction.y = 0f;
        knockback = direction.normalized * 2.5f;
        state = State.Stagger;
        stateTimer = staggerDuration;
    }

    void RestoreColors()
    {
        for (int i = 0; i < bodyRenderers.Length; i++) bodyRenderers[i].material.color = originalColors[i];
    }
}
