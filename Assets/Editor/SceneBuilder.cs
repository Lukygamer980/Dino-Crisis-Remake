using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// Builds the whole demo level out of primitives. Tweak the layout below and run
// "Dino Demo > Build Demo Scene" again to regenerate the scene from scratch; manual
// edits made to the generated scene are lost on the next build.
//
// The level is the first floor of a research facility: fenced yards outside where the player
// starts, a passage round the south side to a backup generator, and the building itself with
// its offices, entrance hall, lecture room and west wing.
//
// Every room is one or more floor rectangles in world coordinates (x east, z north, in meters),
// and the room objects sit at the origin, so everything is placed in those same coordinates.
// Walls are worked out from the rectangles: wherever two different rooms, or a room and the
// outside, meet, a wall goes up, with a gap wherever a door was placed.
//
// The lock flow: the generator key in Material Storage opens the passage to the generator, and
// switching the generator on powers the doors into the building. The military ID in the Control
// Room opens the Strategy Room, its code disk opens the Elevator Hall, and the researcher ID in
// the Lecture Room works the elevator, which ends the demo.
//
// The raptors walk a NavMesh baked from the level's colliders at the end of the build. Characters,
// ceilings and door panels are kept out of the bake, so doorways stay walkable.
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/DinoDemo.unity";
    const string MaterialFolder = "Assets/Materials";
    const string NavMeshPath = "Assets/Scenes/DinoDemo NavMesh.asset";
    const float WallHeight = 3.2f;
    const float WallThickness = 0.3f;
    const float DoorwayWidth = 1.8f;
    const float DoorwayHeight = 2.4f;

    // Open-air rooms get darker ground, fences instead of walls where they don't touch the
    // building, and no ceiling.
    class Room
    {
        public string name;
        public Transform root;
        public bool outdoor;
        public readonly List<Rect> rects = new();

        public bool Contains(Vector3 point) => rects.Exists(rect => rect.Contains(new Vector2(point.x, point.z)));
    }

    static readonly List<Room> rooms = new();
    // Each doorway: whether its wall runs along X, the wall line's coordinate, and the gap's middle along it.
    static readonly List<(bool alongX, float line, float center)> doorways = new();
    static readonly List<Light> poweredLights = new();
    static readonly List<GameObject> ambushRaptors = new();
    static GeneratorSwitch generator;

    static Room backyardFront, backyard, storage, passage, generatorRoom;
    static Room officeHallway, office, entrance, frontArea, elevatorHall, elevator;
    static Room lectureHallway, lectureRoom;
    static Room managementHallway, lockerRoom, managementOffice, westHallway, toilet, strategyRoom, controlRoom, controlHallway;

    static Material concrete, ground, wall, fence, ceiling, metal, crate, door, laser, tile, suit, skin, hair, gunMetal;
    static Material raptorSkin, raptorDark, raptorEyes, blood, screenGlow, lockSign, doorSign, hazard;
    static Material ammoBox, medPak, keyCard, militaryCard, codeDisk, brassKey;

    [MenuItem("Dino Demo/Build Demo Scene")]
    public static void Build()
    {
        CreateMaterials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetUpAtmosphere();
        rooms.Clear();
        doorways.Clear();
        poweredLights.Clear();
        ambushRaptors.Clear();

        LayOutRooms();
        PlayerController player = BuildPlayer(new Vector3(36f, 0f, -4f), 0f);

        // The generator area comes first: doors elsewhere take their power from it.
        BuildGeneratorArea();
        BuildBackyard();
        BuildOfficeWing();
        BuildEntrance();
        BuildLectureWing();
        BuildWestWing();
        generator.poweredLights = poweredLights.ToArray();
        generator.ambushRaptors = ambushRaptors.ToArray();

        BuildWalls();
        BuildCameraAndUI(player, backyardFront.name);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        BakeNavMesh();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("Dino demo scene built: " + ScenePath);
    }

    // ---------- Layout ----------

    // Rectangles are x0, z0, x1, z1. Rooms touch along shared edges; a door between two rooms
    // has to sit on such an edge, at least a meter from its ends so the halves have wall to slide into.
    static void LayOutRooms()
    {
        // Outside.
        backyardFront = AddRoom("Backyard Front", true, (28f, -8f, 44f, 4f));
        backyard = AddRoom("Facility Backyard", true, (20f, 4f, 44f, 22f));
        // U-shaped: south from the backyard's west end, west along the bottom of the map, then
        // north to the generator room.
        passage = AddRoom("Generator Passage", true, (14f, 0f, 20f, 10f), (-36f, -7f, 28f, 0f), (-36f, 0f, -30f, 18f));
        frontArea = AddRoom("Entrance Front Area", true, (-8f, 44f, 0f, 50f));

        // The building.
        storage = AddRoom("Material Storage", false, (32f, 22f, 39f, 28f));
        generatorRoom = AddRoom("Backup Generator Room", false, (-37f, 18f, -29f, 26f));
        // L-shaped: along the top of the office and down its east side to the backyard.
        officeHallway = AddRoom("Office Hallway", false, (4f, 36f, 24f, 39.5f), (20f, 22f, 24f, 36f));
        office = AddRoom("Office", false, (4f, 22f, 20f, 36f));
        entrance = AddRoom("Main Entrance", false, (-12f, 28f, 4f, 44f));
        elevatorHall = AddRoom("Elevator Hall", false, (-8f, 16f, 0f, 28f));
        elevator = AddRoom("Elevator", false, (-11f, 20f, -8f, 23f));
        // Wraps below the elevator hall and up its east side to the office.
        lectureHallway = AddRoom("Lecture Room Hallway", false, (-12f, 12f, 4f, 16f), (0f, 16f, 4f, 28f));
        lectureRoom = AddRoom("Lecture Room", false, (-10f, 0f, 4f, 12f));
        managementHallway = AddRoom("Management Office Hallway", false, (-28f, 40f, -12f, 44f));
        lockerRoom = AddRoom("Locker Room", false, (-26f, 44f, -18f, 50f));
        managementOffice = AddRoom("Management Office", false, (-26f, 30f, -16f, 40f));
        westHallway = AddRoom("West Hallway", false, (-16f, 4f, -12f, 40f));
        toilet = AddRoom("Toilet", false, (-22f, 24f, -16f, 30f));
        strategyRoom = AddRoom("Strategy Room", false, (-28f, 14f, -16f, 24f));
        controlRoom = AddRoom("Control Room", false, (-28f, 4f, -16f, 14f));
        controlHallway = AddRoom("Control Room Hallway", false, (-28f, 0f, -12f, 4f));
    }

    // ---------- Areas ----------

    // The passage round the south of the facility and the backup generator at its end. Both gates
    // into the passage take the generator key, and turning the generator on wakes a raptor out here.
    static void BuildGeneratorArea()
    {
        Transform room = generatorRoom.root;
        Box("Generator", room, new Vector3(-33f, 1f, 24f), new Vector3(4f, 2f, 2f), metal);
        Box("Hazard Stripe", room, new Vector3(-33f, 0.15f, 22.98f), new Vector3(4f, 0.3f, 0.02f), hazard, collider: false);
        Box("Fuel Tank", room, new Vector3(-36.2f, 0.7f, 19.2f), new Vector3(1f, 1.4f, 1f), metal);
        Box("Cable", room, new Vector3(-31f, 0.03f, 21f), new Vector3(0.12f, 0.06f, 4f), gunMetal, collider: false);
        Box("Shelf", room, new Vector3(-29.6f, 0.5f, 20.5f), new Vector3(0.6f, 1f, 1.6f), metal);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-29.6f, 1.09f, 20.5f), 50);

        GameObject panel = Box("Generator Switch", room, new Vector3(-33f, 1.1f, 22.95f), new Vector3(0.6f, 0.5f, 0.1f), gunMetal, collider: false);
        generator = panel.AddComponent<GeneratorSwitch>();
        generator.statusLamp = Box("Status Lamp", room, new Vector3(-32.5f, 2.1f, 23f), Vector3.one * 0.15f, lockSign, collider: false).GetComponent<Renderer>();
        generator.runningMaterial = doorSign;

        // A dim emergency light to find the switch by; the main light needs the power on.
        PointLight(room, new Vector3(-33f, 2.9f, 20f), new Color(1f, 0.15f, 0.1f), 0.8f, 6f, flicker: false, shadows: false);
        Powered(PointLight(room, new Vector3(-33f, 2.9f, 22f), new Color(1f, 0.9f, 0.75f), 1.6f, 9f, flicker: false, shadows: true));

        room = passage.root;
        Box("Crate", room, new Vector3(15.2f, 0.6f, 2f), Vector3.one * 1.2f, crate);
        Box("Crate", room, new Vector3(15.3f, 0.5f, 3.3f), Vector3.one * 1f, crate);
        Box("Crate Stack", room, new Vector3(8f, 0.6f, -6f), new Vector3(2.4f, 1.2f, 1.2f), crate);
        Barrel(room, 26f, -6.2f);
        Barrel(room, -5f, -6.3f);
        Barrel(room, -5.7f, -6.1f);
        Barrel(room, -35.2f, 10f);
        Box("Blood", room, new Vector3(-12f, 0.01f, -3f), new Vector3(1.4f, 0.02f, 0.9f), blood, collider: false);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-20f, 0.1f, -6.2f), 10);

        Floodlight(room, new Vector3(17f, 0f, -5.5f), flicker: false);
        Floodlight(room, new Vector3(0f, 0f, -1f), flicker: true);
        Floodlight(room, new Vector3(-18f, 0f, -6.5f), flicker: false);
        Floodlight(room, new Vector3(-33f, 0f, 3f), flicker: true);
        Floodlight(room, new Vector3(-33f, 0f, 15f), flicker: false);

        BuildRaptor(passage, new Vector3(-33f, 0f, 6f), 0f, ambush: true,
            new Vector3(-33f, 0f, 6f), new Vector3(-33f, 0f, 15f), new Vector3(-15f, 0f, -3.5f), new Vector3(5f, 0f, -3.5f));

        const string gateMessage = "Locked. A sign reads: BACKUP GENERATOR AREA.";
        Locked(Door(20f, 7f, lockSign), "GeneratorKey", gateMessage);
        Locked(Door(28f, -3.5f, lockSign), "GeneratorKey", gateMessage);
        Door(-33f, 18f);
    }

    // The yards where the demo starts, and the storage room with the generator key.
    static void BuildBackyard()
    {
        Transform room = backyardFront.root;
        Barrel(room, 30f, -7.2f);
        Barrel(room, 30.7f, -7.3f);
        Barrel(room, 43.2f, 2.6f);
        Box("Crate", room, new Vector3(42.6f, 0.6f, -6.6f), Vector3.one * 1.2f, crate);
        Floodlight(room, new Vector3(40f, 0f, -7.3f), flicker: false);
        Floodlight(room, new Vector3(30f, 0f, 2.5f), flicker: true);

        room = backyard.root;
        Box("Container", room, new Vector3(38f, 1.3f, 10f), new Vector3(2.5f, 2.6f, 6f), metal);
        Box("Crate", room, new Vector3(26f, 0.6f, 14f), Vector3.one * 1.2f, crate);
        Box("Crate", room, new Vector3(27.3f, 0.5f, 13.4f), Vector3.one * 1f, crate);
        Box("Crate Stack", room, new Vector3(31f, 0.6f, 18f), new Vector3(1.2f, 1.2f, 2.4f), crate);
        Barrel(room, 43.2f, 21.2f);
        Barrel(room, 42.5f, 21.3f);
        Box("Blood Trail", room, new Vector3(22.5f, 0.01f, 19f), new Vector3(0.3f, 0.02f, 4f), blood, collider: false);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(26f, 1.29f, 14f), 50);
        Floodlight(room, new Vector3(24f, 0f, 20.5f), flicker: true);
        Floodlight(room, new Vector3(34f, 0f, 5.5f), flicker: false);
        Floodlight(room, new Vector3(43f, 0f, 16f), flicker: false);

        // Waits for the way back: it only shows up once the generator is on.
        BuildRaptor(backyard, new Vector3(40f, 0f, 19f), 200f, ambush: true,
            new Vector3(40f, 0f, 19f), new Vector3(25f, 0f, 17f), new Vector3(26f, 0f, 7f), new Vector3(41f, 0f, 6f));

        room = storage.root;
        Box("Shelving", room, new Vector3(32.6f, 1.2f, 25f), new Vector3(0.8f, 2.4f, 4.5f), metal);
        Box("Shelving", room, new Vector3(38.4f, 1.2f, 25f), new Vector3(0.8f, 2.4f, 4.5f), metal);
        Box("Crate", room, new Vector3(35.5f, 0.5f, 27.2f), Vector3.one * 1f, crate);
        Box("Table", room, new Vector3(35.5f, 0.4f, 25.8f), new Vector3(1.4f, 0.8f, 0.8f), crate);
        BuildItem(room, "GeneratorKey", "Generator Area key", "It opens the gates to the generator passage.", new Vector3(35.5f, 0.84f, 25.8f), brassKey);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(34f, 0.1f, 27.3f), 10);
        PointLight(room, new Vector3(35.5f, 2.9f, 25f), new Color(1f, 0.75f, 0.45f), 1.3f, 7f, flicker: true, shadows: false);

        Door(36f, 4f);
        Door(35.5f, 22f);
        Powered(Door(22f, 22f, lockSign), "The door is dead. There's no power in the building.");
    }

    // The office where the first raptor waits, and the L-shaped hallway round it.
    static void BuildOfficeWing()
    {
        Transform room = office.root;
        foreach (Vector3 desk in new[] { new Vector3(8f, 0f, 32f), new Vector3(8f, 0f, 28f), new Vector3(13f, 0f, 32f), new Vector3(13f, 0f, 28f) })
        {
            Box("Desk", room, desk + Vector3.up * 0.45f, new Vector3(3f, 0.9f, 1.2f), metal);
            Box("Monitor", room, desk + new Vector3(-0.6f, 1.2f, 0.3f), new Vector3(0.8f, 0.55f, 0.08f), screenGlow, collider: false);
        }
        Box("Filing Cabinet", room, new Vector3(19.4f, 0.7f, 24.5f), new Vector3(0.8f, 1.4f, 2f), metal);
        Box("Filing Cabinet", room, new Vector3(19.4f, 0.7f, 33f), new Vector3(0.8f, 1.4f, 2f), metal);
        Box("Toppled Chair", room, new Vector3(10.5f, 0.25f, 25f), new Vector3(0.6f, 0.5f, 0.6f), gunMetal);
        Box("Blood", room, new Vector3(16f, 0.01f, 27f), new Vector3(1.6f, 0.02f, 1.1f), blood, collider: false);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(13f, 0.98f, 28f), 10);
        PointLight(room, new Vector3(9f, 2.9f, 30f), new Color(0.7f, 0.85f, 1f), 1.4f, 10f, flicker: true, shadows: true);
        PointLight(room, new Vector3(16f, 2.9f, 25f), new Color(0.7f, 0.85f, 1f), 1f, 7f, flicker: false, shadows: false);

        BuildRaptor(office, new Vector3(17f, 0f, 30f), 270f, ambush: false,
            new Vector3(17f, 0f, 30f), new Vector3(16f, 0f, 24f), new Vector3(6f, 0f, 24f), new Vector3(10f, 0f, 34.5f));

        room = officeHallway.root;
        Box("Vending Machine", room, new Vector3(23.4f, 1f, 38.6f), new Vector3(0.9f, 2f, 0.9f), screenGlow);
        Plant(room, 6f, 38.9f);
        Plant(room, 23.4f, 23f);
        PointLight(room, new Vector3(12f, 2.9f, 37.75f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: false, shadows: false);
        PointLight(room, new Vector3(22f, 2.8f, 30f), new Color(1f, 0.12f, 0.08f), 2f, 7f, flicker: true, shadows: false);

        Door(12f, 36f);
        Door(4f, 25f);
        Powered(Door(4f, 37.75f, lockSign, laserShutter: true), "A laser shutter. It won't open without power.");
    }

    // The entrance hall with its sealed stairs, the elevator hall behind the code lock, and the
    // elevator itself, which ends the demo.
    static void BuildEntrance()
    {
        Transform room = entrance.root;
        foreach (Vector3 pillar in new[] { new Vector3(-8f, 0f, 33f), new Vector3(0f, 0f, 33f), new Vector3(-8f, 0f, 39f), new Vector3(0f, 0f, 39f) })
        {
            Box("Pillar", room, pillar + Vector3.up * (WallHeight / 2f), new Vector3(0.8f, WallHeight, 0.8f), wall);
        }
        Box("Reception Desk", room, new Vector3(-4f, 0.5f, 36.5f), new Vector3(3f, 1f, 1f), crate);
        Box("Bench", room, new Vector3(-10.9f, 0.25f, 34f), new Vector3(0.6f, 0.5f, 2.4f), metal);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-10.9f, 0.59f, 34f), 50);

        // The stairs up to the next floor, rising toward the east wall behind a railing and a shutter.
        for (int i = 0; i < 6; i++)
        {
            float height = 0.3f * (i + 1);
            Box("Step", room, new Vector3(0.5f + i * 0.6f, height / 2f, 42.4f), new Vector3(0.6f, height, 2.8f), concrete);
        }
        Box("Railing", room, new Vector3(1.9f, 0.5f, 40.95f), new Vector3(3.9f, 1f, 0.1f), metal);
        GameObject shutter = Box("Stair Shutter", room, new Vector3(0.1f, 1.2f, 42.4f), new Vector3(0.1f, 2.4f, 2.8f), metal);
        Examinable(shutter, "The stairs to the second floor are shuttered. There's no way up for now.");

        PointLight(room, new Vector3(-4f, 2.9f, 31f), new Color(0.75f, 0.85f, 1f), 1.4f, 10f, flicker: false, shadows: true);
        PointLight(room, new Vector3(-4f, 2.9f, 41f), new Color(0.75f, 0.85f, 1f), 1.2f, 9f, flicker: true, shadows: false);

        Floodlight(frontArea.root, new Vector3(-4f, 0f, 48f), flicker: false);

        room = elevatorHall.root;
        Box("Bench", room, new Vector3(-0.6f, 0.25f, 22f), new Vector3(0.6f, 0.5f, 2.4f), metal);
        Box("Call Panel", room, new Vector3(-7.83f, 1.2f, 23.1f), new Vector3(0.05f, 0.3f, 0.2f), screenGlow, collider: false);
        PointLight(room, new Vector3(-4f, 2.9f, 22f), new Color(0.8f, 0.85f, 1f), 1.1f, 8f, flicker: false, shadows: false);
        PointLight(elevator.root, new Vector3(-9.5f, 2.9f, 21.5f), new Color(1f, 0.95f, 0.85f), 0.8f, 4f, flicker: false, shadows: false);

        Locked(Door(-4f, 44f, lockSign), "EntranceKey", "The front doors are locked. They need the entrance key.");
        Door(-12f, 42f);
        Locked(Door(-4f, 28f, lockSign), "CodeDiskN", "A code lock marked \"N\". It needs the matching code disk.");
        SlidingDoor lift = Door(-8f, 21.5f, lockSign);
        Locked(lift, "ResearcherID", "The elevator to B1 needs a researcher's ID card.");
        lift.endsDemo = true;
    }

    // The hallway below the elevator hall and the lecture room, where two raptors hunt together
    // over the researcher ID.
    static void BuildLectureWing()
    {
        Transform room = lectureHallway.root;
        Box("Bench", room, new Vector3(-8f, 0.25f, 15.4f), new Vector3(2.4f, 0.5f, 0.6f), metal);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(3.4f, 0.1f, 16.6f), 10);
        PointLight(room, new Vector3(-6f, 2.9f, 14f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: false, shadows: false);
        PointLight(room, new Vector3(2f, 2.9f, 22f), new Color(1f, 0.12f, 0.08f), 2f, 7f, flicker: true, shadows: false);

        room = lectureRoom.root;
        // Rows of desks facing the screen on the south wall, with an aisle in line with the door.
        foreach (float rowZ in new[] { 9f, 6.5f })
        {
            Box("Desk Row", room, new Vector3(-6.5f, 0.4f, rowZ), new Vector3(4f, 0.8f, 0.8f), crate);
            Box("Desk Row", room, new Vector3(0.5f, 0.4f, rowZ), new Vector3(4f, 0.8f, 0.8f), crate);
        }
        Box("Lectern", room, new Vector3(-3f, 0.6f, 2.2f), new Vector3(1f, 1.2f, 0.6f), crate);
        Box("Screen", room, new Vector3(-3f, 1.8f, 0.18f), new Vector3(5f, 1.8f, 0.05f), screenGlow, collider: false);
        Box("Blood", room, new Vector3(1f, 0.01f, 3f), new Vector3(1.6f, 0.02f, 1.1f), blood, collider: false);
        BuildItem(room, "ResearcherID", "researcher ID card", "It should work the elevator down to B1.", new Vector3(-3f, 1.24f, 2.2f), keyCard);
        PointLight(room, new Vector3(-6f, 2.9f, 6f), new Color(0.7f, 0.85f, 1f), 1.2f, 9f, flicker: false, shadows: true);
        PointLight(room, new Vector3(1f, 2.9f, 4f), new Color(0.7f, 0.85f, 1f), 1f, 7f, flicker: true, shadows: false);

        Vector3[] round = { new(-8f, 0f, 4f), new(2f, 0f, 4f), new(2f, 0f, 11f), new(-8f, 0f, 11f) };
        BuildRaptor(lectureRoom, new Vector3(-7f, 0f, 3f), 90f, ambush: false, round);
        BuildRaptor(lectureRoom, new Vector3(1.5f, 0f, 10.5f), 270f, ambush: false, round);

        Door(-3f, 12f);
        Door(-12f, 14f);
    }

    // The west wing, dark until the generator runs: the management office (a safe room no raptor
    // gets into), the strategy room, the control room and the hallways between them.
    static void BuildWestWing()
    {
        Transform room = managementHallway.root;
        Plant(room, -27.4f, 43.4f);
        Powered(PointLight(room, new Vector3(-20f, 2.9f, 42f), new Color(0.8f, 0.85f, 1f), 1f, 8f, flicker: false, shadows: false));
        Powered(PointLight(room, new Vector3(-14f, 2.9f, 42f), new Color(0.8f, 0.85f, 1f), 0.8f, 6f, flicker: true, shadows: false));

        room = lockerRoom.root;
        Box("Lockers", room, new Vector3(-22f, 1f, 49.5f), new Vector3(6f, 2f, 0.6f), metal);
        Box("Bench", room, new Vector3(-22f, 0.25f, 47f), new Vector3(4f, 0.5f, 0.5f), crate);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-20.5f, 0.58f, 47f), 10);
        Powered(PointLight(room, new Vector3(-22f, 2.9f, 47f), new Color(0.85f, 0.9f, 1f), 1f, 6f, flicker: false, shadows: false));

        room = managementOffice.root;
        Box("Desk", room, new Vector3(-22f, 0.45f, 35f), new Vector3(3f, 0.9f, 1.2f), metal);
        Box("Sofa", room, new Vector3(-25.4f, 0.35f, 37.5f), new Vector3(1f, 0.7f, 2.4f), suit);
        GameObject terminal = Box("Terminal", room, new Vector3(-25.5f, 0.6f, 32f), new Vector3(0.6f, 1.2f, 0.8f), screenGlow);
        Examinable(terminal, "SECURE ROOM: the doors here only open for staff. Nothing else gets in.");
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-22f, 0.99f, 35f), 50);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-17f, 0.1f, 31f), 10);
        Powered(PointLight(room, new Vector3(-21f, 2.9f, 35f), new Color(1f, 0.85f, 0.6f), 1.3f, 8f, flicker: false, shadows: false));

        room = westHallway.root;
        Box("Toppled Locker", room, new Vector3(-15.4f, 0.3f, 30.5f), new Vector3(0.9f, 0.6f, 1.8f), metal);
        Box("Blood Trail", room, new Vector3(-13.5f, 0.01f, 16f), new Vector3(0.3f, 0.02f, 4f), blood, collider: false);
        Powered(PointLight(room, new Vector3(-14f, 2.9f, 10f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: false, shadows: false));
        Powered(PointLight(room, new Vector3(-14f, 2.8f, 22f), new Color(1f, 0.12f, 0.08f), 2f, 7f, flicker: true, shadows: false));
        Powered(PointLight(room, new Vector3(-14f, 2.9f, 34f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: true, shadows: false));
        BuildRaptor(westHallway, new Vector3(-14f, 0f, 22f), 180f, ambush: false,
            new Vector3(-14f, 0f, 8f), new Vector3(-24f, 0f, 2f), new Vector3(-14f, 0f, 8f), new Vector3(-14f, 0f, 37f));

        room = toilet.root;
        Box("Sinks", room, new Vector3(-19f, 0.45f, 29.55f), new Vector3(2.5f, 0.9f, 0.6f), tile);
        Box("Stall Wall", room, new Vector3(-20.5f, 1f, 25.1f), new Vector3(0.08f, 2f, 2.2f), tile);
        Box("Stall Wall", room, new Vector3(-19f, 1f, 25.1f), new Vector3(0.08f, 2f, 2.2f), tile);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-21.3f, 0.1f, 24.7f), 50);
        Powered(PointLight(room, new Vector3(-19f, 2.9f, 27f), new Color(0.85f, 1f, 0.9f), 0.9f, 6f, flicker: true, shadows: false));

        room = strategyRoom.root;
        Box("Briefing Table", room, new Vector3(-22f, 0.45f, 19f), new Vector3(5f, 0.9f, 2.5f), metal);
        Box("Map Screen", room, new Vector3(-27.82f, 1.6f, 19f), new Vector3(0.05f, 1.6f, 4f), screenGlow, collider: false);
        BuildItem(room, "CodeDiskN", "code disk \"N\"", "It fits a code lock marked N.", new Vector3(-22f, 0.93f, 19f), codeDisk);
        Powered(PointLight(room, new Vector3(-22f, 2.9f, 19f), new Color(0.75f, 0.85f, 1f), 1.3f, 9f, flicker: false, shadows: true));

        room = controlRoom.root;
        Box("Console", room, new Vector3(-27.4f, 0.5f, 9f), new Vector3(0.8f, 1f, 6f), metal);
        Box("Console Screens", room, new Vector3(-27.75f, 1.45f, 9f), new Vector3(0.05f, 0.7f, 5.6f), screenGlow, collider: false);
        Box("Desk", room, new Vector3(-20f, 0.45f, 9.5f), new Vector3(1.2f, 0.9f, 3f), metal);
        BuildItem(room, "MilitaryID", "military ID card", "It should open the Strategy Room.", new Vector3(-20f, 0.93f, 9.5f), militaryCard);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-17f, 0.1f, 13.2f), 10);
        GameObject stairs = Box("Stair Shutter", room, new Vector3(-24f, 1.2f, 13.75f), new Vector3(1.8f, 2.4f, 0.2f), metal);
        Examinable(stairs, "The stairway down to the basement. Its shutter is down and won't move.");
        Powered(PointLight(room, new Vector3(-22f, 2.9f, 9f), new Color(0.7f, 1f, 0.8f), 1.2f, 9f, flicker: true, shadows: false));

        room = controlHallway.root;
        Box("Pipe", room, new Vector3(-20f, 2.6f, 0.4f), new Vector3(16f, 0.15f, 0.15f), metal, collider: false);
        Powered(PointLight(room, new Vector3(-20f, 2.9f, 2f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: false, shadows: false));

        Powered(Door(-14f, 40f, lockSign, laserShutter: true), "A laser shutter. It won't open without power.");
        Door(-22f, 44f);
        Door(-21f, 40f).keepsRaptorsOut = true;
        Door(-16f, 35f).keepsRaptorsOut = true;
        Door(-16f, 27f);
        Locked(Door(-16f, 19f, lockSign), "MilitaryID", "Locked. It needs a military ID card.");
        Door(-14f, 4f);
        Door(-22f, 4f);
    }

    // ---------- Building blocks ----------

    static Room AddRoom(string name, bool outdoor, params (float x0, float z0, float x1, float z1)[] rects)
    {
        var room = new Room { name = name, outdoor = outdoor, root = new GameObject(name).transform };
        foreach ((float x0, float z0, float x1, float z1) in rects)
        {
            Rect rect = Rect.MinMaxRect(x0, z0, x1, z1);
            room.rects.Add(rect);
            Vector3 center = new(rect.center.x, 0f, rect.center.y);
            Box("Floor", room.root, center + Vector3.down * 0.05f, new Vector3(rect.width, 0.1f, rect.height), outdoor ? ground : concrete);
            if (outdoor) continue;
            // A collider too, so the camera stays under it.
            GameObject roof = Box("Ceiling", room.root, center + Vector3.up * (WallHeight + 0.05f), new Vector3(rect.width + WallThickness, 0.1f, rect.height + WallThickness), ceiling);
            ExcludeFromNavMesh(roof);
        }
        rooms.Add(room);
        return room;
    }

    static Room RoomAt(Vector3 point) => rooms.Find(room => room.Contains(point));

    // Wherever two different rooms (or a room and the outside) meet along an edge, a wall goes up.
    // Walls are built from the room rectangles' edges, line by line, skipping doorways.
    static void BuildWalls()
    {
        var lines = new Dictionary<(bool alongX, float at), List<float>>();
        void AddEdge(bool alongX, float at, float from, float to)
        {
            if (!lines.TryGetValue((alongX, at), out List<float> cuts)) lines[(alongX, at)] = cuts = new List<float>();
            cuts.Add(from);
            cuts.Add(to);
        }
        foreach (Room room in rooms)
        {
            foreach (Rect rect in room.rects)
            {
                AddEdge(true, rect.yMin, rect.xMin, rect.xMax);
                AddEdge(true, rect.yMax, rect.xMin, rect.xMax);
                AddEdge(false, rect.xMin, rect.yMin, rect.yMax);
                AddEdge(false, rect.xMax, rect.yMin, rect.yMax);
            }
        }

        Transform walls = new GameObject("Walls").transform;
        foreach (((bool alongX, float at), List<float> cuts) in lines)
        {
            Vector3 Point(float along, float across) => alongX ? new Vector3(along, 0f, at + across) : new Vector3(at + across, 0f, along);

            // Between neighbouring cuts the rooms either side stay the same, so each stretch is
            // either all wall or none. Stretches of the same kind merge into one run.
            cuts.Sort();
            var runs = new List<(float from, float to, Material material)>();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                float a = cuts[i], b = cuts[i + 1];
                if (b - a < 0.01f) continue;
                Room one = RoomAt(Point((a + b) / 2f, -0.05f));
                Room other = RoomAt(Point((a + b) / 2f, 0.05f));
                if (one == other) continue;
                // Building walls wherever the building is; fences between the yards and the outside.
                Material material = (one != null && !one.outdoor) || (other != null && !other.outdoor) ? wall : fence;
                if (runs.Count > 0 && Mathf.Approximately(runs[^1].to, a) && runs[^1].material == material) runs[^1] = (runs[^1].from, b, material);
                else runs.Add((a, b, material));
            }

            for (int r = 0; r < runs.Count; r++)
            {
                (float from, float to, Material material) = runs[r];
                string name = material == wall ? "Wall" : "Fence";
                void Piece(string pieceName, float start, float end, float bottom, float top)
                {
                    if (end - start <= 0.01f) return;
                    Vector3 position = Point((start + end) / 2f, 0f) + Vector3.up * ((bottom + top) / 2f);
                    Vector3 size = alongX ? new Vector3(end - start, top - bottom, WallThickness) : new Vector3(WallThickness, top - bottom, end - start);
                    Box(pieceName, walls, position, size, material);
                }

                // Free ends reach half a wall further, which closes the corners.
                bool joinedBefore = r > 0 && Mathf.Approximately(runs[r - 1].to, from);
                bool joinedAfter = r + 1 < runs.Count && Mathf.Approximately(runs[r + 1].from, to);
                float cursor = from - (joinedBefore ? 0f : WallThickness / 2f);
                float stop = to + (joinedAfter ? 0f : WallThickness / 2f);

                var gaps = doorways.FindAll(d => d.alongX == alongX && Mathf.Approximately(d.line, at) && d.center > from && d.center < to);
                gaps.Sort((x, y) => x.center.CompareTo(y.center));
                foreach ((_, _, float center) in gaps)
                {
                    Piece(name, cursor, center - DoorwayWidth / 2f, 0f, WallHeight);
                    Piece(name + " Lintel", center - DoorwayWidth / 2f, center + DoorwayWidth / 2f, DoorwayHeight, WallHeight);
                    cursor = center + DoorwayWidth / 2f;
                }
                Piece(name, cursor, stop, 0f, WallHeight);
            }
        }
    }

    // A double sliding door at (x, z), which must lie on an edge between two rooms. Its local +Z
    // faces north or east into the front room. A laser shutter is the same door with beams for halves.
    static SlidingDoor Door(float x, float z, Material signMaterial = null, bool laserShutter = false)
    {
        bool alongX = rooms.Exists(room => room.rects.Exists(rect =>
            (Mathf.Approximately(z, rect.yMin) || Mathf.Approximately(z, rect.yMax)) && x > rect.xMin && x < rect.xMax));
        Vector3 point = new(x, 0f, z);
        Vector3 normal = alongX ? Vector3.forward : Vector3.right;
        Room front = RoomAt(point + normal * 0.5f);
        Room back = RoomAt(point - normal * 0.5f);
        doorways.Add((alongX, alongX ? z : x, alongX ? x : z));

        Transform root = new GameObject($"Door {front.name} / {back.name}").transform;
        root.SetParent(front.root, false);
        root.SetLocalPositionAndRotation(point, Quaternion.Euler(0f, alongX ? 0f : 90f, 0f));

        Vector3 halfSize = new(DoorwayWidth / 2f, DoorwayHeight, 0.12f);
        Transform PanelHalf(string halfName, float side)
        {
            Vector3 position = new(side * DoorwayWidth / 4f, DoorwayHeight / 2f, 0f);
            GameObject half;
            if (laserShutter)
            {
                half = new GameObject(halfName);
                half.transform.SetParent(root, false);
                half.transform.localPosition = position;
                half.AddComponent<BoxCollider>().size = halfSize;
                for (int i = 0; i < 7; i++)
                {
                    Part(PrimitiveType.Cube, "Beam", half.transform, new Vector3(0f, -DoorwayHeight / 2f + 0.2f + i * 0.33f, 0f), Vector3.zero, new Vector3(DoorwayWidth / 2f, 0.03f, 0.03f), laser);
                }
            }
            else
            {
                half = Box(halfName, root, position, halfSize, door);
            }
            // Colliders that move need a kinematic body, or physics treats them as static scenery.
            half.AddComponent<Rigidbody>().isKinematic = true;
            ExcludeFromNavMesh(half);
            return half.transform;
        }

        SlidingDoor slidingDoor = root.gameObject.AddComponent<SlidingDoor>();
        slidingDoor.leftPanel = PanelHalf("Left Half", -1f);
        slidingDoor.rightPanel = PanelHalf("Right Half", 1f);
        // A touch further than the gap, so an open half's edge doesn't flicker against the door frame.
        slidingDoor.slideDistance = DoorwayWidth / 2f + 0.03f;
        slidingDoor.frontRoomName = front.name.ToUpperInvariant();
        slidingDoor.backRoomName = back.name.ToUpperInvariant();

        // Off until the door decides raptors can't get through; carving makes the agents path around it.
        NavMeshObstacle blocker = root.gameObject.AddComponent<NavMeshObstacle>();
        blocker.shape = NavMeshObstacleShape.Box;
        blocker.center = new Vector3(0f, DoorwayHeight / 2f, 0f);
        blocker.size = new Vector3(DoorwayWidth, DoorwayHeight, WallThickness);
        blocker.carving = true;
        blocker.carveOnlyStationary = false;
        blocker.enabled = false;
        slidingDoor.blocker = blocker;

        // On the lintel and a little thicker than the wall, so it shows from both rooms.
        Box("Sign", root, new Vector3(0f, DoorwayHeight + 0.2f, 0f), new Vector3(0.7f, 0.08f, WallThickness + 0.04f), signMaterial != null ? signMaterial : doorSign, collider: false);
        return slidingDoor;
    }

    static void Locked(SlidingDoor door, string item, string message)
    {
        door.requiredItem = item;
        door.lockedMessage = message;
    }

    static void Powered(SlidingDoor door, string message)
    {
        door.powerSource = generator;
        door.lockedMessage = message;
    }

    // A light that stays off until the generator runs.
    static void Powered(Light light) => poweredLights.Add(light);

    static Light PointLight(Transform room, Vector3 position, Color color, float intensity, float range, bool flicker, bool shadows)
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
        return light;
    }

    // A cold lamp on a pole, to light the yards.
    static void Floodlight(Transform room, Vector3 foot, bool flicker)
    {
        Box("Light Pole", room, foot + Vector3.up * 2f, new Vector3(0.15f, 4f, 0.15f), gunMetal);
        Light light = PointLight(room, foot + Vector3.up * 3.75f, new Color(0.6f, 0.72f, 1f), 1.6f, 13f, flicker, shadows: false);
        light.name = "Floodlight";
    }

    static void Barrel(Transform room, float x, float z)
    {
        GameObject barrel = Part(PrimitiveType.Cylinder, "Barrel", room, new Vector3(x, 0.45f, z), Vector3.zero, new Vector3(0.6f, 0.45f, 0.6f), hazard);
        barrel.AddComponent<BoxCollider>();
    }

    static void Plant(Transform room, float x, float z)
    {
        Box("Planter", room, new Vector3(x, 0.3f, z), new Vector3(0.5f, 0.6f, 0.5f), concrete);
        Part(PrimitiveType.Sphere, "Leaves", room, new Vector3(x, 0.95f, z), Vector3.zero, new Vector3(0.7f, 0.8f, 0.7f), raptorDark);
    }

    static void Examinable(GameObject target, string message) => target.AddComponent<Notice>().message = message;

    static void BuildPickup(Transform room, Pickup.Kind kind, Vector3 position, int amount)
    {
        (Material material, Vector3 scale) = kind == Pickup.Kind.Ammo
            ? (ammoBox, new Vector3(0.28f, 0.16f, 0.2f))
            : (medPak, new Vector3(0.3f, 0.18f, 0.3f));
        GameObject pickup = Box(kind.ToString(), room, position, scale, material, collider: false);
        Pickup component = pickup.AddComponent<Pickup>();
        component.kind = kind;
        component.amount = amount;
    }

    static void BuildItem(Transform room, string item, string itemName, string hint, Vector3 position, Material material)
    {
        GameObject pickup = Box(itemName, room, position, new Vector3(0.24f, 0.03f, 0.16f), material, collider: false);
        Pickup component = pickup.AddComponent<Pickup>();
        component.kind = Pickup.Kind.Item;
        component.item = item;
        component.itemName = itemName;
        component.hint = hint;
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
        ExcludeFromNavMesh(root);
        return player;
    }

    // An ambush raptor starts inactive and appears when the generator is switched on. It patrols
    // `patrol` in order; the points become markers in its room.
    static void BuildRaptor(Room room, Vector3 position, float yaw, bool ambush, params Vector3[] patrol)
    {
        GameObject root = new(ambush ? "Velociraptor (Ambush)" : "Velociraptor");
        root.transform.SetParent(room.root, false);
        root.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
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

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.45f;
        agent.height = 1.6f;
        agent.acceleration = 20f;
        agent.stoppingDistance = 0.2f;
        ExcludeFromNavMesh(root);

        var waypoints = new Transform[patrol.Length];
        for (int i = 0; i < patrol.Length; i++)
        {
            waypoints[i] = new GameObject("Patrol Point").transform;
            waypoints[i].SetParent(room.root, false);
            waypoints[i].localPosition = patrol[i];
        }
        root.AddComponent<Raptor>().waypoints = waypoints;

        if (!ambush) return;
        root.SetActive(false);
        ambushRaptors.Add(root);
    }

    static void BuildCameraAndUI(PlayerController player, string startRoomName)
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
        GameUI ui = systems.AddComponent<GameUI>();
        ui.player = player;
        ui.startRoomName = startRoomName.ToUpperInvariant();
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

    // ---------- Navigation ----------

    // Keeps an object and its children out of the bake, so its collider doesn't punch a hole in the NavMesh.
    static void ExcludeFromNavMesh(GameObject target) => target.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

    // Bakes the walkable floor from the level's colliders and saves it as an asset next to the
    // scene, so the scene keeps it after a reload.
    static void BakeNavMesh()
    {
        NavMeshSurface surface = new GameObject("NavMesh").AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        AssetDatabase.DeleteAsset(NavMeshPath);
        AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
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
        ground = Mat("Ground", new Color(0.13f, 0.14f, 0.12f), smoothness: 0.05f);
        wall = Mat("Wall", new Color(0.42f, 0.44f, 0.42f));
        fence = Mat("Fence", new Color(0.2f, 0.22f, 0.22f), smoothness: 0.3f);
        ceiling = Mat("Ceiling", new Color(0.18f, 0.19f, 0.19f));
        metal = Mat("Metal", new Color(0.25f, 0.28f, 0.3f), smoothness: 0.5f);
        crate = Mat("Crate", new Color(0.45f, 0.33f, 0.18f));
        door = Mat("Door", new Color(0.3f, 0.36f, 0.42f), smoothness: 0.4f);
        laser = Mat("Laser", new Color(1f, 0.1f, 0.1f), emission: new Color(1f, 0.05f, 0.05f));
        tile = Mat("Tile", new Color(0.7f, 0.74f, 0.72f), smoothness: 0.6f);
        hazard = Mat("Hazard", new Color(0.75f, 0.55f, 0.1f), smoothness: 0.3f);
        suit = Mat("Suit", new Color(0.18f, 0.24f, 0.22f));
        skin = Mat("Skin", new Color(0.86f, 0.66f, 0.52f));
        hair = Mat("Hair", new Color(0.75f, 0.1f, 0.07f));
        gunMetal = Mat("Gun Metal", new Color(0.08f, 0.08f, 0.09f), smoothness: 0.7f);
        raptorSkin = Mat("Raptor Skin", new Color(0.36f, 0.4f, 0.2f));
        raptorDark = Mat("Raptor Dark", new Color(0.2f, 0.22f, 0.12f));
        raptorEyes = Mat("Raptor Eyes", new Color(1f, 0.8f, 0.1f), emission: new Color(1f, 0.7f, 0f));
        blood = Mat("Blood", new Color(0.25f, 0.01f, 0.01f), smoothness: 0.8f);
        screenGlow = Mat("Screen Glow", new Color(0.1f, 0.4f, 0.2f), emission: new Color(0.1f, 0.8f, 0.3f));
        lockSign = Mat("Lock Sign", new Color(0.6f, 0.05f, 0.05f), emission: new Color(1f, 0.05f, 0.05f));
        doorSign = Mat("Door Sign", new Color(0.05f, 0.4f, 0.1f), emission: new Color(0.1f, 0.9f, 0.2f));
        ammoBox = Mat("Ammo Box", new Color(0.8f, 0.6f, 0.2f), emission: new Color(0.35f, 0.25f, 0.05f));
        medPak = Mat("Med Pak", new Color(0.9f, 0.9f, 0.9f), emission: new Color(0.5f, 0.1f, 0.1f));
        keyCard = Mat("Key Card", new Color(0.2f, 0.7f, 1f), emission: new Color(0.1f, 0.6f, 1f));
        militaryCard = Mat("Military Card", new Color(0.3f, 0.6f, 0.2f), emission: new Color(0.2f, 0.6f, 0.1f));
        codeDisk = Mat("Code Disk", new Color(0.6f, 0.3f, 0.9f), emission: new Color(0.5f, 0.2f, 0.9f));
        brassKey = Mat("Brass Key", new Color(0.85f, 0.7f, 0.3f), smoothness: 0.7f, emission: new Color(0.4f, 0.3f, 0.05f));
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
