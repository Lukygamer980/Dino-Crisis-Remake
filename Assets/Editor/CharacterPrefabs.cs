using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// The characters as prefabs in Assets/Prefabs, so the level (SceneBuilder) and the character test
// arena (TestArenaBuilder) both place the very same player and raptor. Which models, clips and
// handgun go in is picked in the Player Character and Raptor Setup assets. Once a prefab exists it is the
// source of truth: tune it in Prefab Mode or apply overrides from the test arena, and rebuilding
// either scene keeps those changes, as the scene builders only create prefabs that are missing.
//
// "Dino Demo > Rebuild Character Prefabs" builds them from the code below again and so discards
// any tuning done on the prefabs. Rebuild both scenes afterwards: the scene instances' overrides
// (names, patrols) point at the old prefab contents.
public static class CharacterPrefabs
{
    public const string Folder = "Assets/Prefabs";
    public const string PlayerPath = Folder + "/Player.prefab";
    public const string RaptorPath = Folder + "/Velociraptor.prefab";

    public static GameObject PlayerPrefab => AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
    public static GameObject RaptorPrefab => AssetDatabase.LoadAssetAtPath<GameObject>(RaptorPath);

    // Called by the scene builders after SceneBuilder.CreateMaterials. Rebuilds the Animator
    // Controllers in place (the prefabs keep pointing at them) and creates any prefab that is missing.
    internal static void Prepare()
    {
        PlayerAnimatorBuilder.Build();
        RaptorAnimatorBuilder.Build();
        if (PlayerPrefab == null) Save(BuildPlayer(), PlayerPath);
        if (RaptorPrefab == null) Save(BuildRaptor(), RaptorPath);
        PickupPrefabs.Prepare(rebuild: false);
    }

    [MenuItem("Dino Demo/Rebuild Character Prefabs")]
    static void RebuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("Rebuild Character Prefabs",
                "Rebuild the player, raptor and pickup prefabs from code? Changes made to the prefabs are lost, " +
                "and both scenes should be rebuilt afterwards.", "Rebuild", "Cancel"))
            return;
        Rebuild();
    }

    public static void Rebuild()
    {
        SceneBuilder.CreateMaterials();
        PlayerAnimatorBuilder.Build();
        Save(BuildPlayer(), PlayerPath);
        Save(BuildRaptor(), RaptorPath);
        PickupPrefabs.Prepare(rebuild: true);
    }

    static void Save(GameObject root, string path)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
        Debug.Log("Character prefab saved: " + path);
    }

    // ---------- The player ----------

    // Built from the Player Character asset (PlayerCharacterSetup): the model, the handgun in a socket
    // on her hand that moves to the holster socket when she puts it away, and the lights.
    static GameObject BuildPlayer()
    {
        PlayerCharacterSetup setup = PlayerAnimatorBuilder.Setup;
        GameObject root = new("Player");
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.85f, 0f);
        controller.height = 1.7f;
        controller.radius = 0.3f;

        Animator animator = AttachModel(setup, root.transform, out Transform handSocket, out Transform holsterSocket);
        Transform gun;
        Transform gunPoint = new GameObject("Gun Point").transform;
        if (handSocket != null && setup.handgun != null)
        {
            gun = ((GameObject)PrefabUtility.InstantiatePrefab(setup.handgun)).transform;
            gun.name = "Handgun";
            gun.SetParent(handSocket, false);
            // The socket scales with the model; the gun keeps its own size.
            gun.localScale = Vector3.one / Mathf.Max(0.01f, setup.modelScale);
            StripPhysics(gun.gameObject);
            gunPoint.SetParent(gun, false);
            gunPoint.localPosition = setup.muzzle;
        }
        else
        {
            gun = SceneBuilder.Part(PrimitiveType.Cube, "Handgun", root.transform, new Vector3(0.24f, 0.95f, 0.12f), Vector3.zero,
                new Vector3(0.07f, 0.1f, 0.28f), SceneBuilder.gunMetal).transform;
            if (handSocket != null) gun.SetParent(handSocket, false);
            gunPoint.SetParent(gun, false);
            gunPoint.localPosition = new Vector3(0f, 0f, 0.6f);
        }
        Light flash = gunPoint.gameObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.75f, 0.35f);
        flash.intensity = 5f * SceneBuilder.UrpLightScale;
        flash.range = 7f;
        flash.enabled = false;

        // A dim light that follows the player so the character stays readable in dark corners.
        GameObject fill = new("Fill Light");
        fill.transform.SetParent(root.transform, false);
        fill.transform.localPosition = new Vector3(0f, 2.3f, 0.6f);
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.intensity = 0.5f * SceneBuilder.UrpLightScale;
        fillLight.range = 4f;

        PlayerController player = root.AddComponent<PlayerController>();
        player.gunModel = gun;
        player.gunPoint = gunPoint;
        player.gunHandSocket = handSocket;
        player.gunHolsterSocket = holsterSocket;
        player.locomotion = AssetDatabase.LoadAssetAtPath<LocomotionProfile>(PlayerAnimatorBuilder.ProfilePath);
        player.muzzleFlash = flash;
        player.animator = animator;
        GameFeedback.SetUpPlayer(player);
        SceneBuilder.ExcludeFromNavMesh(root);
        return root;
    }

    // The model replaces the capsule body; without one, the old capsule body is built instead.
    static Animator AttachModel(PlayerCharacterSetup setup, Transform player, out Transform handSocket, out Transform holsterSocket)
    {
        handSocket = holsterSocket = null;
        if (setup.model == null)
        {
            SceneBuilder.Part(PrimitiveType.Capsule, "Body", player, new Vector3(0f, 0.75f, 0f), Vector3.zero, new Vector3(0.5f, 0.75f, 0.35f), SceneBuilder.suit);
            SceneBuilder.Part(PrimitiveType.Sphere, "Head", player, new Vector3(0f, 1.58f, 0f), Vector3.zero, Vector3.one * 0.27f, SceneBuilder.skin);
            SceneBuilder.Part(PrimitiveType.Sphere, "Hair", player, new Vector3(0f, 1.64f, -0.03f), Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f), SceneBuilder.hair);
            return null;
        }

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(setup.model);
        model.name = "Model";
        model.transform.SetParent(player, false);
        model.transform.localScale = Vector3.one * setup.modelScale;
        foreach (string part in setup.hiddenParts)
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == part) child.gameObject.SetActive(false);
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool swapped = false;
            for (int i = 0; i < materials.Length; i++)
                foreach (PlayerCharacterSetup.MaterialSwap swap in setup.materialSwaps)
                    if (swap.from != null && swap.to != null && materials[i] == swap.from)
                    {
                        materials[i] = swap.to;
                        swapped = true;
                    }
            if (swapped) renderer.sharedMaterials = materials;
        }
        StripPhysics(model);

        Animator animator = model.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
        // Root motion is on only so RootMotionRelay can read how fast the animated steps go.
        // The relay keeps the model in place; PlayerController moves the body.
        animator.applyRootMotion = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (animator.GetComponent<RootMotionRelay>() == null) animator.gameObject.AddComponent<RootMotionRelay>();
        if (setup.footIK)
        {
            // HomebrewIK casts from each foot down onto the level (the Default layer) and plants it there.
            var footIK = animator.gameObject.AddComponent<FischlWorks.csHomebrewIK>();
            var serialized = new SerializedObject(footIK);
            serialized.FindProperty("groundLayers").intValue = 1 << 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        handSocket = Socket("Gun Socket", Parent(model, setup.handSocketParent, animator, HumanBodyBones.RightHand), setup.gripPosition, setup.gripRotation);
        holsterSocket = Socket("Holster Socket", Parent(model, setup.holsterSocketParent, animator, HumanBodyBones.RightUpperLeg), setup.holsterPosition, setup.holsterRotation);
        return animator;
    }

    // A named transform in the model, or else the humanoid bone.
    static Transform Parent(GameObject model, string name, Animator animator, HumanBodyBones bone)
    {
        if (!string.IsNullOrEmpty(name))
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
        return animator.GetBoneTransform(bone);
    }

    static Transform Socket(string name, Transform parent, Vector3 position, Vector3 rotation)
    {
        if (parent == null) return null;
        Transform socket = new GameObject(name).transform;
        socket.SetParent(parent, false);
        socket.SetLocalPositionAndRotation(position, Quaternion.Euler(rotation));
        return socket;
    }

    // Props and models carry no colliders or rigidbodies here: characters collide through their
    // CharacterController, and pickups are found by distance.
    internal static void StripPhysics(GameObject target)
    {
        foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body, true);
        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider, true);
    }

    // ---------- Raptor ----------

    // The raptor from the Raptor Setup asset: the animated model with its Animator, a hitbox along
    // its body for shots, and walking speeds that match its clips. Without a model, the primitive
    // raptor. Its patrol points are scene objects, so each scene fills them in.
    static GameObject BuildRaptor()
    {
        RaptorSetup setup = RaptorAnimatorBuilder.Setup;
        RuntimeAnimatorController animatorController = RaptorAnimatorBuilder.Build();
        GameObject root = new("Velociraptor");
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.height = 1.4f;
        controller.radius = 0.45f;

        Raptor raptor;
        if (setup.model != null && animatorController != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(setup.model);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * setup.modelScale;
            StripPhysics(model);
            Animator animator = model.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
            // Off screen it still walks its patrol, but its bones stop updating.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (AudioSource source in model.GetComponentsInChildren<AudioSource>(true))
            {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = 30f;
                source.playOnAwake = false;
            }
            // Shots and sight lines hit this as well as the CharacterController round its legs.
            var hitbox = new GameObject("Hitbox").AddComponent<BoxCollider>();
            hitbox.transform.SetParent(root.transform, false);
            hitbox.center = new Vector3(0f, 1.05f, 0.45f);
            hitbox.size = new Vector3(0.5f, 0.75f, 2f);

            raptor = AddBrain(root);
            // Gaits at the pace the clips step: it patrols at the walk and investigates at the trot.
            raptor.wanderSpeed = Round(setup.walkSpeed);
            raptor.investigateSpeed = Round(setup.runSpeed);
            raptor.searchSpeed = Round(setup.runSpeed);
            // It stops to screech for as long as the screech clip plays before handing back to running.
            if (setup.alert != null) raptor.screechTime = Round(setup.alert.length * RaptorAnimatorBuilder.ScreechExit);
        }
        else
        {
            Transform t = root.transform;
            Material skin = SceneBuilder.raptorSkin, dark = SceneBuilder.raptorDark, eyes = SceneBuilder.raptorEyes;
            SceneBuilder.Part(PrimitiveType.Capsule, "Body", t, new Vector3(0f, 0.95f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.55f, 0.75f, 0.5f), skin);
            SceneBuilder.Part(PrimitiveType.Capsule, "Neck", t, new Vector3(0f, 1.25f, 0.7f), new Vector3(40f, 0f, 0f), new Vector3(0.2f, 0.3f, 0.2f), skin);
            SceneBuilder.Part(PrimitiveType.Cube, "Head", t, new Vector3(0f, 1.45f, 1f), Vector3.zero, new Vector3(0.28f, 0.28f, 0.55f), skin);
            SceneBuilder.Part(PrimitiveType.Cube, "Jaw", t, new Vector3(0f, 1.29f, 1.05f), new Vector3(8f, 0f, 0f), new Vector3(0.22f, 0.08f, 0.45f), dark);
            SceneBuilder.Part(PrimitiveType.Sphere, "Eye", t, new Vector3(0.15f, 1.52f, 1.1f), Vector3.zero, Vector3.one * 0.06f, eyes);
            SceneBuilder.Part(PrimitiveType.Sphere, "Eye", t, new Vector3(-0.15f, 1.52f, 1.1f), Vector3.zero, Vector3.one * 0.06f, eyes);
            SceneBuilder.Part(PrimitiveType.Capsule, "Tail", t, new Vector3(0f, 1f, -1.15f), new Vector3(100f, 0f, 0f), new Vector3(0.22f, 0.7f, 0.22f), dark);
            foreach (float side in new[] { -1f, 1f })
            {
                SceneBuilder.Part(PrimitiveType.Cube, "Thigh", t, new Vector3(0.2f * side, 0.65f, -0.05f), new Vector3(-15f, 0f, 0f), new Vector3(0.16f, 0.5f, 0.3f), skin);
                SceneBuilder.Part(PrimitiveType.Cube, "Shin", t, new Vector3(0.2f * side, 0.25f, 0.05f), new Vector3(10f, 0f, 0f), new Vector3(0.12f, 0.5f, 0.14f), dark);
                SceneBuilder.Part(PrimitiveType.Cube, "Arm", t, new Vector3(0.18f * side, 0.95f, 0.6f), new Vector3(30f, 0f, 0f), new Vector3(0.06f, 0.25f, 0.06f), dark);
            }
            raptor = AddBrain(root);
        }
        SceneBuilder.ExcludeFromNavMesh(root);
        return root;
    }

    static float Round(float value) => Mathf.Round(value * 100f) / 100f;

    // The NavMeshAgent that paths it and the Raptor component that decides where to.
    static Raptor AddBrain(GameObject root)
    {
        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.45f;
        agent.height = 1.6f;
        agent.acceleration = 20f;
        agent.stoppingDistance = 0.2f;
        return root.AddComponent<Raptor>();
    }
}
