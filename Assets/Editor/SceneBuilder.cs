using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the whole demo level out of primitives. Tweak the layout below and run
// "Dino Demo > Build Demo Scene" again to regenerate the scene from scratch; manual
// edits made to the generated scene are lost on the next build.
//
// Rooms sit wall to wall and connect through doorways closed by sliding doors, so the
// player and the raptors walk between them. A room leaves out a wall it shares with a
// neighbour, so the two rooms never build the same wall twice.
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/DinoDemo.unity";
    const string MaterialFolder = "Assets/Materials";
    const float WallHeight = 3.2f;
    const float WallThickness = 0.3f;
    const float DoorwayWidth = 1.8f;
    const float DoorwayHeight = 2.4f;

    [System.Flags]
    enum Side { None = 0, North = 1, South = 2, East = 4, West = 8 }

    static Material concrete, wall, metal, crate, door, suit, skin, hair, gunMetal;
    static Material raptorSkin, raptorDark, raptorEyes, blood, screenGlow, exitSign, doorSign;
    static Material ammoBox, medPak, keyCard;

    [MenuItem("Dino Demo/Build Demo Scene")]
    public static void Build()
    {
        CreateMaterials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetUpAtmosphere();

        PlayerController player = BuildPlayer(new Vector3(-5f, 0f, -3f), 30f);

        // The corridor runs north from the dock's north wall into the control room's south wall.
        Transform dock = BuildRoom("Loading Dock", new Vector3(0f, 0f, 0f), 16f, 12f, Side.None,
            (Side.North, 5f), (Side.West, 2f));
        Transform corridor = BuildRoom("Service Corridor", new Vector3(5f, 0f, 17f), 4f, 22f, Side.North | Side.South);
        Transform control = BuildRoom("Control Room", new Vector3(5f, 0f, 35f), 14f, 14f, Side.None,
            (Side.South, 0f));

        FurnishLoadingDock(dock);
        FurnishCorridor(corridor);
        FurnishControlRoom(control);

        BuildSlidingDoor(dock, "Door to Corridor", new Vector3(5f, 0f, 6f), 180f, doorSign, corridor.name);
        BuildSlidingDoor(control, "Door to Corridor", new Vector3(0f, 0f, -7f), 0f, doorSign, corridor.name);

        SlidingDoor exit = BuildSlidingDoor(dock, "Exit Door", new Vector3(-8f, 0f, 2f), 90f, exitSign, "");
        exit.requiredItem = "KeyCard";
        exit.lockedMessage = "The exit is locked. It needs a key card.";
        exit.endsDemo = true;
        // A bit of floor outside, so the open exit doesn't look out over nothing.
        Box("Exit Floor", dock, new Vector3(-9.15f, -0.05f, 2f), new Vector3(2f, 0.1f, DoorwayWidth + 0.6f), concrete);

        BuildCameraAndUI(player);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("Dino demo scene built: " + ScenePath);
    }

    // ---------- Rooms ----------

    static void FurnishLoadingDock(Transform room)
    {
        Box("Crate", room, new Vector3(2f, 0.6f, -1f), Vector3.one * 1.2f, crate);
        Box("Crate", room, new Vector3(3.3f, 0.5f, -1.5f), Vector3.one * 1f, crate);
        Box("Crate", room, new Vector3(2.2f, 1.6f, -1.1f), Vector3.one * 0.8f, crate);
        Box("Crate Stack", room, new Vector3(-2f, 0.6f, 3.5f), new Vector3(1.2f, 1.2f, 2.4f), crate);
        Box("Crate", room, new Vector3(6.5f, 0.6f, -4.5f), Vector3.one * 1.2f, crate);
        Box("Shelving", room, new Vector3(7.3f, 1.2f, 1.5f), new Vector3(0.8f, 2.4f, 3.5f), metal);
        Box("Pallet", room, new Vector3(-5.5f, 0.07f, 3.8f), new Vector3(1.6f, 0.14f, 1.2f), crate);

        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(6.5f, 1.35f, -4.5f), 10);

        PointLight(room, new Vector3(-4f, 2.9f, 0f), new Color(1f, 0.72f, 0.42f), 1.8f, 10f, flicker: false, shadows: true);
        PointLight(room, new Vector3(4f, 2.9f, 2f), new Color(0.6f, 0.75f, 1f), 1.5f, 9f, flicker: true, shadows: false);
    }

    static void FurnishCorridor(Transform room)
    {
        Box("Pipe", room, new Vector3(1.8f, 2.6f, 0f), new Vector3(0.15f, 0.15f, 22f), metal, collider: false);
        Box("Pipe", room, new Vector3(1.8f, 2.35f, 0f), new Vector3(0.1f, 0.1f, 22f), metal, collider: false);
        Box("Blood", room, new Vector3(0.3f, 0.01f, -2f), new Vector3(0.8f, 0.02f, 1.4f), blood, collider: false);
        Box("Blood Trail", room, new Vector3(-0.4f, 0.01f, 1.5f), new Vector3(0.3f, 0.02f, 4f), blood, collider: false);
        Box("Blood", room, new Vector3(-1.95f, 1.2f, 3.6f), new Vector3(0.02f, 0.9f, 0.7f), blood, collider: false);
        Box("Toppled Locker", room, new Vector3(-1.4f, 0.3f, -6f), new Vector3(0.9f, 0.6f, 1.8f), metal);

        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-1.4f, 0.75f, -6f), 50);

        PointLight(room, new Vector3(0f, 2.9f, -9.5f), new Color(0.8f, 0.85f, 1f), 0.8f, 5f, flicker: false, shadows: false);
        PointLight(room, new Vector3(1.5f, 2.8f, -3f), new Color(1f, 0.12f, 0.08f), 2.2f, 7f, flicker: true, shadows: true);
        PointLight(room, new Vector3(-1.5f, 2.8f, 6f), new Color(1f, 0.12f, 0.08f), 2.2f, 7f, flicker: true, shadows: false);
    }

    static void FurnishControlRoom(Transform room)
    {
        Box("Desk", room, new Vector3(-4f, 0.45f, 1f), new Vector3(3f, 0.9f, 1.2f), metal);
        Box("Monitor", room, new Vector3(-4.6f, 1.2f, 1.3f), new Vector3(0.9f, 0.6f, 0.08f), screenGlow, collider: false);
        Box("Monitor", room, new Vector3(-3.4f, 1.2f, 1.3f), new Vector3(0.9f, 0.6f, 0.08f), screenGlow, collider: false);
        Box("Desk", room, new Vector3(4f, 0.45f, -2f), new Vector3(1.2f, 0.9f, 3f), metal);
        Box("Server Rack", room, new Vector3(-5f, 1.2f, 6.3f), new Vector3(1.2f, 2.4f, 0.8f), metal);
        Box("Server Rack", room, new Vector3(-3.5f, 1.2f, 6.3f), new Vector3(1.2f, 2.4f, 0.8f), metal);
        Box("Server Rack", room, new Vector3(3f, 1.2f, 6.3f), new Vector3(1.2f, 2.4f, 0.8f), metal);
        Box("Side Table", room, new Vector3(5.5f, 0.4f, 5f), new Vector3(1.2f, 0.8f, 0.8f), metal);
        Box("Blood", room, new Vector3(1f, 0.01f, 2f), new Vector3(1.6f, 0.02f, 1.1f), blood, collider: false);

        BuildPickup(room, Pickup.Kind.KeyCard, new Vector3(5.5f, 0.88f, 5f), 1);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-5.8f, 0.15f, -5.5f), 10);

        BuildRaptor(room, new Vector3(0.5f, 0f, 4.5f), 200f);

        PointLight(room, new Vector3(0f, 2.9f, 0f), new Color(0.7f, 0.85f, 1f), 1.4f, 11f, flicker: true, shadows: true);
        PointLight(room, new Vector3(-4f, 1.5f, 0.4f), new Color(0.2f, 1f, 0.4f), 0.9f, 3.5f, flicker: false, shadows: false);
    }

    // ---------- Building blocks ----------

    // `sharedSides` are left out: the neighbouring room's wall closes them. Each doorway is a wall
    // side and an offset along that wall from the room's center, where the wall gets a door-sized gap.
    static Transform BuildRoom(string name, Vector3 center, float width, float depth, Side sharedSides, params (Side side, float offset)[] doorways)
    {
        Transform room = new GameObject(name).transform;
        room.position = center;
        float halfWidth = width / 2f;
        float halfDepth = depth / 2f;

        Box("Floor", room, new Vector3(0f, -0.05f, 0f), new Vector3(width, 0.1f, depth), concrete);

        // Walls overlap at the corners to close them, but stop at the center line of a shared
        // wall, so they end inside it instead of poking out the neighbour's side.
        float End(Side side) => (sharedSides & side) != 0 ? 0f : WallThickness / 2f;
        float westEnd = -halfWidth - End(Side.West), eastEnd = halfWidth + End(Side.East);
        float southEnd = -halfDepth - End(Side.South), northEnd = halfDepth + End(Side.North);

        foreach (Side side in new[] { Side.North, Side.South, Side.East, Side.West })
        {
            if ((sharedSides & side) != 0) continue;
            var gaps = new List<float>();
            foreach ((Side doorSide, float offset) in doorways)
            {
                if (doorSide == side) gaps.Add(offset);
            }

            bool alongX = side is Side.North or Side.South;
            Vector3 line = side switch
            {
                Side.North => new Vector3(0f, 0f, halfDepth),
                Side.South => new Vector3(0f, 0f, -halfDepth),
                Side.East => new Vector3(halfWidth, 0f, 0f),
                _ => new Vector3(-halfWidth, 0f, 0f),
            };
            BuildWall(room, side + " Wall", line, alongX, alongX ? westEnd : southEnd, alongX ? eastEnd : northEnd, gaps);
        }
        return room;
    }

    // A wall along the room's X or Z axis through `line`, from `start` to `end`, with a doorway
    // gap and a lintel above it at each of `gaps`.
    static void BuildWall(Transform room, string name, Vector3 line, bool alongX, float start, float end, List<float> gaps)
    {
        Vector3 along = alongX ? Vector3.right : Vector3.forward;
        void Piece(string pieceName, float from, float to, float bottom, float top)
        {
            float length = to - from;
            if (length <= 0.01f) return;
            Vector3 position = line + along * ((from + to) / 2f) + Vector3.up * ((bottom + top) / 2f);
            Vector3 size = alongX
                ? new Vector3(length, top - bottom, WallThickness)
                : new Vector3(WallThickness, top - bottom, length);
            Box(pieceName, room, position, size, wall);
        }

        gaps.Sort();
        float cursor = start;
        foreach (float gap in gaps)
        {
            float gapStart = gap - DoorwayWidth / 2f;
            float gapEnd = gap + DoorwayWidth / 2f;
            Piece(name, cursor, gapStart, 0f, WallHeight);
            Piece(name + " Lintel", gapStart, gapEnd, DoorwayHeight, WallHeight);
            cursor = gapEnd;
        }
        Piece(name, cursor, end, 0f, WallHeight);
    }

    // `wallPoint` is the doorway's middle on the wall's center line at floor level; `yawIntoRoom`
    // faces away from the wall into `room`. The halves slide into the wall's hollow when they open.
    static SlidingDoor BuildSlidingDoor(Transform room, string name, Vector3 wallPoint, float yawIntoRoom, Material signMaterial, string otherRoomName)
    {
        Transform root = new GameObject(name).transform;
        root.SetParent(room, false);
        root.localPosition = wallPoint;
        root.localRotation = Quaternion.Euler(0f, yawIntoRoom, 0f);

        Vector3 halfSize = new(DoorwayWidth / 2f, DoorwayHeight, 0.12f);
        Transform PanelHalf(string halfName, float side)
        {
            GameObject half = Box(halfName, root, new Vector3(side * DoorwayWidth / 4f, DoorwayHeight / 2f, 0f), halfSize, door);
            // Colliders that move need a kinematic body, or physics treats them as static scenery.
            half.AddComponent<Rigidbody>().isKinematic = true;
            return half.transform;
        }

        SlidingDoor slidingDoor = root.gameObject.AddComponent<SlidingDoor>();
        slidingDoor.leftPanel = PanelHalf("Left Half", -1f);
        slidingDoor.rightPanel = PanelHalf("Right Half", 1f);
        // A touch further than the gap, so an open half's edge doesn't flicker against the door frame.
        slidingDoor.slideDistance = DoorwayWidth / 2f + 0.03f;
        slidingDoor.frontRoomName = room.name.ToUpperInvariant();
        slidingDoor.backRoomName = otherRoomName.ToUpperInvariant();

        // On the lintel and a little thicker than the wall, so it shows from both rooms.
        Box("Sign", root, new Vector3(0f, DoorwayHeight + 0.2f, 0f), new Vector3(0.7f, 0.08f, WallThickness + 0.04f), signMaterial, collider: false);
        return slidingDoor;
    }

    static void PointLight(Transform room, Vector3 position, Color color, float intensity, float range, bool flicker, bool shadows)
    {
        GameObject lightObject = new(flicker ? "Flickering Light" : "Light");
        lightObject.transform.SetParent(room, false);
        lightObject.transform.localPosition = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        if (flicker) lightObject.AddComponent<FlickeringLight>();
        Box("Lamp", lightObject.transform, Vector3.up * 0.25f, new Vector3(0.6f, 0.08f, 0.25f), metal, collider: false);
    }

    static void BuildPickup(Transform room, Pickup.Kind kind, Vector3 position, int amount)
    {
        (Material material, Vector3 scale) = kind switch
        {
            Pickup.Kind.Ammo => (ammoBox, new Vector3(0.28f, 0.16f, 0.2f)),
            Pickup.Kind.MedPak => (medPak, new Vector3(0.3f, 0.18f, 0.3f)),
            _ => (keyCard, new Vector3(0.24f, 0.03f, 0.16f)),
        };
        GameObject pickup = Box(kind.ToString(), room, position, scale, material, collider: false);
        Pickup component = pickup.AddComponent<Pickup>();
        component.kind = kind;
        component.amount = amount;
    }

    static PlayerController BuildPlayer(Vector3 position, float yaw)
    {
        GameObject root = new("Player");
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.85f, 0f);
        controller.height = 1.7f;
        controller.radius = 0.3f;

        Part(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.75f, 0f), Vector3.zero, new Vector3(0.5f, 0.75f, 0.35f), suit);
        Part(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.58f, 0f), Vector3.zero, Vector3.one * 0.27f, skin);
        Part(PrimitiveType.Sphere, "Hair", root.transform, new Vector3(0f, 1.64f, -0.03f), Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f), hair);
        GameObject gun = Part(PrimitiveType.Cube, "Handgun", root.transform, new Vector3(0.24f, 0.95f, 0.12f), Vector3.zero, new Vector3(0.07f, 0.1f, 0.28f), gunMetal);

        Transform gunPoint = new GameObject("Gun Point").transform;
        gunPoint.SetParent(gun.transform, false);
        gunPoint.localPosition = new Vector3(0f, 0f, 0.6f);
        Light flash = gunPoint.gameObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.75f, 0.35f);
        flash.intensity = 5f;
        flash.range = 7f;
        flash.enabled = false;

        // A dim light that follows the player so the character stays readable in dark corners.
        GameObject fill = new("Fill Light");
        fill.transform.SetParent(root.transform, false);
        fill.transform.localPosition = new Vector3(0f, 2.3f, 0.6f);
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.intensity = 0.5f;
        fillLight.range = 4f;

        PlayerController player = root.AddComponent<PlayerController>();
        player.gunModel = gun.transform;
        player.gunPoint = gunPoint;
        player.muzzleFlash = flash;
        return player;
    }

    static void BuildRaptor(Transform room, Vector3 localPosition, float yaw)
    {
        GameObject root = new("Velociraptor");
        root.transform.SetParent(room, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.8f, 0f);
        controller.height = 1.4f;
        controller.radius = 0.45f;

        Transform t = root.transform;
        Part(PrimitiveType.Capsule, "Body", t, new Vector3(0f, 0.95f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.55f, 0.75f, 0.5f), raptorSkin);
        Part(PrimitiveType.Capsule, "Neck", t, new Vector3(0f, 1.25f, 0.7f), new Vector3(40f, 0f, 0f), new Vector3(0.2f, 0.3f, 0.2f), raptorSkin);
        Part(PrimitiveType.Cube, "Head", t, new Vector3(0f, 1.45f, 1f), Vector3.zero, new Vector3(0.28f, 0.28f, 0.55f), raptorSkin);
        Part(PrimitiveType.Cube, "Jaw", t, new Vector3(0f, 1.29f, 1.05f), new Vector3(8f, 0f, 0f), new Vector3(0.22f, 0.08f, 0.45f), raptorDark);
        Part(PrimitiveType.Sphere, "Eye", t, new Vector3(0.15f, 1.52f, 1.1f), Vector3.zero, Vector3.one * 0.06f, raptorEyes);
        Part(PrimitiveType.Sphere, "Eye", t, new Vector3(-0.15f, 1.52f, 1.1f), Vector3.zero, Vector3.one * 0.06f, raptorEyes);
        Part(PrimitiveType.Capsule, "Tail", t, new Vector3(0f, 1f, -1.15f), new Vector3(100f, 0f, 0f), new Vector3(0.22f, 0.7f, 0.22f), raptorDark);
        foreach (float side in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cube, "Thigh", t, new Vector3(0.2f * side, 0.65f, -0.05f), new Vector3(-15f, 0f, 0f), new Vector3(0.16f, 0.5f, 0.3f), raptorSkin);
            Part(PrimitiveType.Cube, "Shin", t, new Vector3(0.2f * side, 0.25f, 0.05f), new Vector3(10f, 0f, 0f), new Vector3(0.12f, 0.5f, 0.14f), raptorDark);
            Part(PrimitiveType.Cube, "Arm", t, new Vector3(0.18f * side, 0.95f, 0.6f), new Vector3(30f, 0f, 0f), new Vector3(0.06f, 0.25f, 0.06f), raptorDark);
        }

        root.AddComponent<Raptor>();
    }

    static void BuildCameraAndUI(PlayerController player)
    {
        GameObject cameraObject = new("Main Camera") { tag = "MainCamera" };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 60f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<ThirdPersonCamera>().player = player;

        GameObject systems = new("Game UI");
        systems.AddComponent<GameUI>().player = player;
    }

    static void SetUpAtmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.08f, 0.09f, 0.11f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.fogColor = new Color(0.01f, 0.012f, 0.015f);
    }

    // ---------- Primitives and materials ----------

    static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool collider = true)
    {
        GameObject box = Part(PrimitiveType.Cube, name, parent, localPosition, Vector3.zero, scale, material);
        if (collider) box.AddComponent<BoxCollider>();
        return box;
    }

    // Model parts carry no collider: characters collide through their CharacterController only.
    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localEuler, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.Euler(localEuler);
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    static void CreateMaterials()
    {
        concrete = Mat("Concrete", new Color(0.32f, 0.32f, 0.3f));
        wall = Mat("Wall", new Color(0.42f, 0.44f, 0.42f));
        metal = Mat("Metal", new Color(0.25f, 0.28f, 0.3f), smoothness: 0.5f);
        crate = Mat("Crate", new Color(0.45f, 0.33f, 0.18f));
        door = Mat("Door", new Color(0.3f, 0.36f, 0.42f), smoothness: 0.4f);
        suit = Mat("Suit", new Color(0.18f, 0.24f, 0.22f));
        skin = Mat("Skin", new Color(0.86f, 0.66f, 0.52f));
        hair = Mat("Hair", new Color(0.75f, 0.1f, 0.07f));
        gunMetal = Mat("Gun Metal", new Color(0.08f, 0.08f, 0.09f), smoothness: 0.7f);
        raptorSkin = Mat("Raptor Skin", new Color(0.36f, 0.4f, 0.2f));
        raptorDark = Mat("Raptor Dark", new Color(0.2f, 0.22f, 0.12f));
        raptorEyes = Mat("Raptor Eyes", new Color(1f, 0.8f, 0.1f), emission: new Color(1f, 0.7f, 0f));
        blood = Mat("Blood", new Color(0.25f, 0.01f, 0.01f), smoothness: 0.8f);
        screenGlow = Mat("Screen Glow", new Color(0.1f, 0.4f, 0.2f), emission: new Color(0.1f, 0.8f, 0.3f));
        exitSign = Mat("Exit Sign", new Color(0.6f, 0.05f, 0.05f), emission: new Color(1f, 0.05f, 0.05f));
        doorSign = Mat("Door Sign", new Color(0.05f, 0.4f, 0.1f), emission: new Color(0.1f, 0.9f, 0.2f));
        ammoBox = Mat("Ammo Box", new Color(0.8f, 0.6f, 0.2f), emission: new Color(0.35f, 0.25f, 0.05f));
        medPak = Mat("Med Pak", new Color(0.9f, 0.9f, 0.9f), emission: new Color(0.5f, 0.1f, 0.1f));
        keyCard = Mat("Key Card", new Color(0.2f, 0.7f, 1f), emission: new Color(0.1f, 0.6f, 1f));
        AssetDatabase.SaveAssets();
    }

    // Materials must be saved as assets; materials that exist only in memory are not
    // written into the scene file and show up pink after a reload.
    static Material Mat(string name, Color color, float smoothness = 0.15f, Color? emission = null)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Directory.CreateDirectory(MaterialFolder);
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        if (emission is Color emissionColor)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emissionColor);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(material);
        return material;
    }
}
