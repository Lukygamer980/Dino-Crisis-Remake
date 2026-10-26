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
// The level follows the room layout of the original game's Facility 1F (the official guide
// book's floor map, at about 10 cm per map pixel): the backyards outside where the player
// starts, the passage round to the backup generator, and the building with the offices, the
// main entrance, the elevator hall, the lecture room and the west wing. Two piping ducts link
// vents in different rooms; they are built off to the side, away from the floor.
//
// Every room is one or more floor rectangles in world coordinates (x east, z north, in meters),
// and the room objects sit at the origin, so everything is placed in those same coordinates.
// Walls are worked out from the rectangles: wherever two different rooms, or a room and the
// outside, meet, a wall goes up, with a gap wherever a door was placed. Corridors that run into
// each other on the map are marked open, so no wall goes between them.
//
// The lock flow: the Backup Power Area key in Material Storage opens the gate to the generator
// passage, and switching the generator on powers the door into the building. The Office
// Hallway's laser shutter blocks the way on, so the player goes through duct A, which comes out
// by the Main Entrance and in the west wing. The Management Office (the safe room) has the entrance
// key and the console that releases the laser shutters, which opens shortcuts between the wings.
// The entrance key opens the plaza in front of the Main Entrance, where the code disk N lies; the
// disk opens the Elevator Hall. The elevator down to B1 is the goal: the prototype says there that
// the game doesn't continue yet. The Strategy Room, the Control Room lift, the stairs down and
// the doors out of the floor stay locked, as their keys belong to later parts of the game.
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
    // Pairs of rooms that meet without a wall, like corridors running into each other.
    static readonly List<(Room, Room)> openings = new();
    // Each doorway: whether its wall runs along X, the wall line's coordinate, and the gap's middle along it.
    static readonly List<(bool alongX, float line, float center)> doorways = new();
    static readonly List<Light> poweredLights = new();
    static readonly List<GameObject> ambushRaptors = new();
    static PowerSwitch generator, laserConsole;

    // Room numbers from the guide book's map.
    static Room backyardFront, backyard, storage, passage, generatorRoom;                       // 1-3, 1-1, 1-2, 1-4, 1-5
    static Room officeHallway, officeHallwayEnd, office;                                         // 1-6, 1-20
    static Room entrance, entrancePlaza, elevatorHall;                                           // 1-13, 1-14, 1-15
    static Room lectureHallway, lectureRoom;                                                     // 1-16, 1-19
    static Room managementHallway, managementHallwayEnd, managementVestibule, lockerRoom, toilet; // 1-11, 1-12, 1-18
    static Room managementOffice, strategyRoom, controlHall, controlHallEnd, controlRoom;        // 1-10, 1-21, 1-8, 1-9
    static Room ductA, ductB;                                                                    // 1-7, 1-17

    static Material concrete, ground, wall, fence, ceiling, metal, crate, door, laser, tile, duct, suit, skin, hair, gunMetal;
    static Material raptorSkin, raptorDark, raptorEyes, blood, screenGlow, lockSign, doorSign, hazard;
    static Material ammoBox, medPak, keyCard, militaryCard, codeDisk, brassKey;

    [MenuItem("Dino Demo/Build Demo Scene")]
    public static void Build()
    {
        CreateMaterials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetUpAtmosphere();
        rooms.Clear();
        openings.Clear();
        doorways.Clear();
        poweredLights.Clear();
        ambushRaptors.Clear();

        LayOutRooms();
        PlayerController player = BuildPlayer(new Vector3(39.5f, 0f, -3f), 0f);

        // The generator and the laser console come first: doors elsewhere take their power from them.
        BuildGeneratorArea();
        BuildManagementOffice();
        BuildBackyard();
        BuildOfficeWing();
        BuildEntrance();
        BuildLectureWing();
        BuildWestWing();
        BuildDucts();
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
    // has to sit on such an edge, about a meter from its ends so the halves have wall to slide into.
    // A room split by a laser shutter or a metal shutter is two rooms of the same name.
    static void LayOutRooms()
    {
        // Outside, on the east and south.
        backyardFront = AddRoom("Backyard Front", true, (35f, -6.5f, 44f, 0f));
        backyard = AddRoom("Facility Backyard", true, (20f, 9f, 33f, 14.5f), (28f, 6f, 33f, 9f), (28f, 0f, 44f, 6f), (41f, 6f, 44f, 14.5f));
        // From the backyard south and west round the lecture room, then a U down and back up to the generator room.
        passage = AddRoom("Generator Passage", true, (20f, 0f, 28f, 9f), (20f, -6.5f, 35f, 0f), (20f, -26.5f, 23f, -6.5f),
            (5f, -30f, 23f, -26.5f), (5f, -34f, 13f, -30f), (5f, -26.5f, 7.5f, -9f));
        // In front of the main entrance, wrapping round its north side.
        entrancePlaza = AddRoom("Entrance Plaza", true, (-2.5f, 40f, 29f, 46.5f), (-2.5f, 35f, 23f, 40f), (-2.5f, 31f, 0f, 35f), (20f, 31f, 23f, 35f));

        // East side of the building.
        storage = AddRoom("Material Storage", false, (33f, 6f, 41f, 14.5f));
        generatorRoom = AddRoom("Backup Generator Room", false, (0f, -9f, 7.5f, 0f));
        office = AddRoom("Office", false, (20f, 14.5f, 41f, 27.5f));
        // Along the top of the office and down its east side to the backyard; the short end by the
        // main entrance is behind a laser shutter.
        officeHallway = AddRoom("Office Hallway", false, (23f, 27.5f, 44f, 31f), (41f, 14.5f, 44f, 27.5f));
        officeHallwayEnd = AddRoom("Office Hallway", false, (20f, 27.5f, 23f, 31f));

        // The middle.
        entrance = AddRoom("Main Entrance", false, (0f, 24f, 20f, 35f));
        // The elevator stands in the alcove on the south side.
        elevatorHall = AddRoom("Elevator Hall", false, (0f, 17f, 18f, 24f), (5.5f, 14.5f, 18f, 17f));
        // Up the east side of the elevator hall, along below it, and down the west side of the lecture room.
        lectureHallway = AddRoom("Lecture Room Hallway", false, (18f, 12.5f, 20f, 24f), (0f, 12.5f, 18f, 14.5f), (0f, 14.5f, 5.5f, 17f), (0f, 0f, 2f, 12.5f));
        lectureRoom = AddRoom("Lecture Room", false, (2f, 0f, 20f, 12.5f));

        // The west wing. The management office hallway runs along the top and down past the
        // locker room; laser shutters close off its end by the entrance and the bit by the toilet.
        managementHallway = AddRoom("Management Office Hallway", false, (-15.5f, 27.5f, -3.5f, 31f), (-15.5f, 24f, -11.5f, 27.5f));
        managementHallwayEnd = AddRoom("Management Office Hallway", false, (-3.5f, 27.5f, 0f, 31f));
        managementVestibule = AddRoom("Management Office Hallway", false, (-15.5f, 19.5f, -11.5f, 24f));
        lockerRoom = AddRoom("Locker Room", false, (-22f, 23.5f, -15.5f, 31f));
        toilet = AddRoom("Toilet", false, (-22f, 18.5f, -15.5f, 23.5f));
        managementOffice = AddRoom("Management Office", false, (-11.5f, 19.5f, 0f, 27.5f));
        strategyRoom = AddRoom("Strategy Room", false, (-15.5f, 11.5f, -3f, 19.5f));
        // From the management office's back door down, along the top of the control room and down
        // its west side to a metal shutter, with the stairs down to B1 behind it.
        controlHall = AddRoom("Control Room Hall", false, (-15.5f, 9f, 0f, 11.5f), (-3f, 11.5f, 0f, 19.5f), (-15.5f, 3.8f, -13f, 9f));
        controlHallEnd = AddRoom("Control Room Hall", false, (-15.5f, -4f, -13f, 3.8f), (-13f, -4f, -6f, 0f));
        controlRoom = AddRoom("Control Room", false, (-11.5f, 0f, 0f, 9f));
        Open(controlHall, lectureHallway);

        // The piping ducts, off to the southwest. A links the Office Hallway, the Control Room
        // Hall and the end of the Management Office Hallway; B links the toilet and the Lecture
        // Room Hallway.
        ductA = AddRoom("Piping Passage A", false, (-48f, -36f, -24f, -34f), (-48f, -34f, -46f, -26f), (-26f, -34f, -24f, -26f), (-38f, -38f, -36f, -36f));
        ductB = AddRoom("Piping Passage B", false, (-48f, -22f, -28f, -20f), (-48f, -20f, -46f, -12f), (-30f, -20f, -28f, -12f));
    }

    // ---------- Areas ----------

    // 1-4 and 1-5: the passage round the south of the facility to the backup generator. Its gate
    // from the Backyard Front takes the Backup Power Area key; the gate from the backyard takes a
    // key from the lecture room. Turning the generator on powers the building and wakes a raptor
    // in the backyard.
    static void BuildGeneratorArea()
    {
        Transform room = generatorRoom.root;
        Box("Generator", room, new Vector3(3f, 1f, -4f), new Vector3(4f, 2f, 2f), metal);
        Box("Hazard Stripe", room, new Vector3(3f, 0.15f, -5.02f), new Vector3(4f, 0.3f, 0.02f), hazard, collider: false);
        Box("Fuel Tank", room, new Vector3(6.6f, 0.7f, -0.9f), new Vector3(1f, 1.4f, 1f), metal);
        Box("Cable", room, new Vector3(5.5f, 0.03f, -4f), new Vector3(0.12f, 0.06f, 3f), gunMetal, collider: false);
        Box("Shelf", room, new Vector3(0.4f, 0.5f, -7.5f), new Vector3(0.6f, 1f, 1.6f), metal);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(0.4f, 1.09f, -7.5f), 50);

        GameObject panel = Box("Generator Switch", room, new Vector3(3f, 1.1f, -5.05f), new Vector3(0.6f, 0.5f, 0.1f), gunMetal, collider: false);
        generator = panel.AddComponent<PowerSwitch>();
        generator.prompt = "Switch on the generator";
        generator.message = "The generator rumbles to life. Power is back in the building.";
        generator.statusLamp = Box("Status Lamp", room, new Vector3(3.6f, 2.1f, -5f), Vector3.one * 0.15f, lockSign, collider: false).GetComponent<Renderer>();
        generator.runningMaterial = doorSign;

        // A dim emergency light to find the switch by; the main light needs the power on.
        PointLight(room, new Vector3(5.5f, 2.9f, -7f), new Color(1f, 0.15f, 0.1f), 0.8f, 6f, flicker: false, shadows: false);
        Powered(PointLight(room, new Vector3(3f, 2.9f, -6.5f), new Color(1f, 0.9f, 0.75f), 1.6f, 9f, flicker: false, shadows: true));

        room = passage.root;
        Box("Crate", room, new Vector3(27f, 0.6f, 7.8f), Vector3.one * 1.2f, crate);
        Box("Crate", room, new Vector3(27.1f, 0.5f, 6.6f), Vector3.one * 1f, crate);
        Box("Crate Stack", room, new Vector3(10f, 0.6f, -29.3f), new Vector3(2.4f, 1.2f, 1.2f), crate);
        Barrel(room, 34.4f, -6f);
        Barrel(room, 21f, -15f);
        Barrel(room, 12.4f, -33.4f);
        Box("Blood", room, new Vector3(21.5f, 0.01f, -10f), new Vector3(0.9f, 0.02f, 1.4f), blood, collider: false);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(6f, 0.1f, -33.4f), 10);

        Floodlight(room, new Vector3(27.5f, 0f, -6f), flicker: false);
        Floodlight(room, new Vector3(22.6f, 0f, -16f), flicker: true);
        Floodlight(room, new Vector3(14f, 0f, -26.9f), flicker: false);
        Floodlight(room, new Vector3(7.2f, 0f, -18f), flicker: true);
        Floodlight(room, new Vector3(20.5f, 0f, 4f), flicker: false);

        Door(6.25f, -9f);
    }

    // 1-10: the safe room. No raptor gets in; it has the console that releases the laser shutters
    // and the entrance key.
    static void BuildManagementOffice()
    {
        Transform room = managementOffice.root;
        Box("Desk", room, new Vector3(-7f, 0.45f, 23.5f), new Vector3(3f, 0.9f, 1.2f), metal);
        Box("Sofa", room, new Vector3(-10.9f, 0.35f, 26f), new Vector3(1f, 0.7f, 2.4f), suit);
        BuildItem(room, "EntranceKey", "entrance key", "It opens the front doors of the Main Entrance.", new Vector3(-8f, 0.93f, 23.5f), brassKey);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-6f, 0.99f, 23.5f), 50);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-10.8f, 0.1f, 20.2f), 10);

        GameObject console = Box("Security Console", room, new Vector3(-11.1f, 0.6f, 22f), new Vector3(0.6f, 1.2f, 1.4f), screenGlow);
        laserConsole = console.AddComponent<PowerSwitch>();
        laserConsole.prompt = "Release the laser shutters";
        laserConsole.message = "The laser shutters on this floor switch off.";
        laserConsole.noiseRadius = 0f;

        GameObject terminal = Box("Terminal", room, new Vector3(-0.45f, 0.6f, 25.5f), new Vector3(0.6f, 1.2f, 0.8f), screenGlow);
        Examinable(terminal, "SECURE ROOM: the doors here only open for staff. Nothing else gets in.");
        PointLight(room, new Vector3(-5.75f, 2.9f, 23.5f), new Color(1f, 0.85f, 0.6f), 1.3f, 8f, flicker: false, shadows: false);

        Door(-5.5f, 27.5f).keepsRaptorsOut = true;
        Door(-1.5f, 19.5f).keepsRaptorsOut = true;
    }

    // 1-1, 1-2 and 1-3: the yards where the demo starts, and the storage room with the key to the
    // generator passage.
    static void BuildBackyard()
    {
        Transform room = backyardFront.root;
        Barrel(room, 36f, -6f);
        Barrel(room, 36.7f, -6.1f);
        Box("Crate", room, new Vector3(36f, 0.6f, -0.9f), Vector3.one * 1.2f, crate);
        Floodlight(room, new Vector3(39.5f, 0f, -6.2f), flicker: false);
        Floodlight(room, new Vector3(43.5f, 0f, -2f), flicker: true);

        room = backyard.root;
        Box("Crate", room, new Vector3(31.8f, 0.6f, 13.4f), Vector3.one * 1.2f, crate);
        Box("Crate", room, new Vector3(30.6f, 0.5f, 13.6f), Vector3.one * 1f, crate);
        Box("Crate Stack", room, new Vector3(31f, 0.6f, 1f), new Vector3(2.4f, 1.2f, 1.2f), crate);
        Barrel(room, 43.3f, 7f);
        Barrel(room, 43.3f, 7.7f);
        Box("Blood Trail", room, new Vector3(26f, 0.01f, 11.5f), new Vector3(4f, 0.02f, 0.3f), blood, collider: false);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(31.8f, 1.29f, 13.4f), 50);
        Floodlight(room, new Vector3(20.6f, 0f, 13.8f), flicker: true);
        Floodlight(room, new Vector3(36f, 0f, 5.4f), flicker: false);
        Floodlight(room, new Vector3(43.4f, 0f, 13.8f), flicker: false);

        // Waits for the way back: it only shows up once the generator is on.
        BuildRaptor(backyard, new Vector3(24f, 0f, 11.75f), 90f, ambush: true,
            new Vector3(24f, 0f, 11.75f), new Vector3(30.5f, 0f, 7.5f), new Vector3(38f, 0f, 3f), new Vector3(42.5f, 0f, 10f));

        room = storage.root;
        Box("Shelving", room, new Vector3(33.6f, 1.2f, 10.5f), new Vector3(0.8f, 2.4f, 5f), metal);
        Box("Shelving", room, new Vector3(40.4f, 1.2f, 10.5f), new Vector3(0.8f, 2.4f, 5f), metal);
        Box("Crate", room, new Vector3(37f, 0.5f, 13.9f), Vector3.one * 1f, crate);
        Box("Table", room, new Vector3(37f, 0.4f, 12.5f), new Vector3(1.4f, 0.8f, 0.8f), crate);
        BuildItem(room, "BackupPowerKey", "Backup Power Area key", "It opens the gate to the generator passage.", new Vector3(37f, 0.84f, 12.5f), brassKey);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(35f, 0.1f, 13.8f), 10);
        PointLight(room, new Vector3(37f, 2.9f, 10.5f), new Color(1f, 0.75f, 0.45f), 1.3f, 7f, flicker: true, shadows: false);

        Door(42f, 0f);
        Locked(Door(35f, -2f, lockSign), "BackupPowerKey", "Locked. A sign reads: BACKUP POWER AREA.");
        Locked(Door(44f, -5f, lockSign), "Outside", "That way leads back out of the facility. Not now.");
        Door(37f, 6f);
        Locked(Door(22f, 9f, lockSign), "B1GeneratorKey", "Locked. The tag on the lock reads: B1 GENERATOR ROOM.");
        Powered(Door(42.5f, 14.5f, lockSign), generator, "The door has no power. The building is dark.");
    }

    // 1-6 and 1-20: the hallway into the building and the office where the first raptor waits. The
    // office door to the hallway only unlocks from inside, and a laser shutter cuts the hallway off
    // from the main entrance; the duct vent in the hallway is the way on.
    static void BuildOfficeWing()
    {
        Transform room = office.root;
        foreach (Vector3 desk in new[] { new Vector3(25f, 0f, 24f), new Vector3(25f, 0f, 20f), new Vector3(30f, 0f, 24f), new Vector3(30f, 0f, 20f), new Vector3(35f, 0f, 24f) })
        {
            Box("Desk", room, desk + Vector3.up * 0.45f, new Vector3(3f, 0.9f, 1.2f), metal);
            Box("Monitor", room, desk + new Vector3(-0.6f, 1.2f, 0.3f), new Vector3(0.8f, 0.55f, 0.08f), screenGlow, collider: false);
        }
        Box("Filing Cabinet", room, new Vector3(40.4f, 0.7f, 17f), new Vector3(0.8f, 1.4f, 2f), metal);
        Box("Filing Cabinet", room, new Vector3(40.4f, 0.7f, 24f), new Vector3(0.8f, 1.4f, 2f), metal);
        Box("Toppled Chair", room, new Vector3(27.5f, 0.25f, 17f), new Vector3(0.6f, 0.5f, 0.6f), gunMetal);
        Box("Blood", room, new Vector3(33f, 0.01f, 17.5f), new Vector3(1.6f, 0.02f, 1.1f), blood, collider: false);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(30f, 0.98f, 20f), 10);
        PointLight(room, new Vector3(26f, 2.9f, 21f), new Color(0.7f, 0.85f, 1f), 1.4f, 10f, flicker: true, shadows: true);
        PointLight(room, new Vector3(35f, 2.9f, 19f), new Color(0.7f, 0.85f, 1f), 1f, 8f, flicker: false, shadows: false);

        BuildRaptor(office, new Vector3(24.5f, 0f, 22f), 90f, ambush: false,
            new Vector3(24.5f, 0f, 22f), new Vector3(37.5f, 0f, 17f), new Vector3(38.5f, 0f, 22f), new Vector3(27.5f, 0f, 26.5f));

        room = officeHallway.root;
        Box("Vending Machine", room, new Vector3(43.4f, 1f, 30.4f), new Vector3(0.9f, 2f, 0.9f), screenGlow);
        Plant(room, 43.4f, 16f);
        Plant(room, 33f, 30.5f);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(42.5f, 0.1f, 25f), 10);
        Powered(PointLight(room, new Vector3(30f, 2.9f, 29.25f), new Color(0.8f, 0.85f, 1f), 1f, 8f, flicker: false, shadows: false));
        Powered(PointLight(room, new Vector3(42.5f, 2.8f, 21f), new Color(1f, 0.12f, 0.08f), 2f, 7f, flicker: true, shadows: false));
        PointLight(officeHallwayEnd.root, new Vector3(21.5f, 2.9f, 29.25f), new Color(0.8f, 0.85f, 1f), 0.6f, 4f, flicker: false, shadows: false);

        Door(20f, 17f);
        Door(37.25f, 27.5f).unlocksFromSide = -1;
        Door(20f, 29.25f);
        Powered(Door(23f, 29.25f, lockSign, laserShutter: true), laserConsole, "A laser shutter blocks the way. It's switched from somewhere else.");
    }

    // 1-13, 1-14 and 1-15: the main entrance with its sealed stairs to 2F, the plaza in front of it
    // where the code disk lies, and the elevator hall behind the code lock, where the prototype ends.
    static void BuildEntrance()
    {
        Transform room = entrance.root;
        foreach (Vector3 pillar in new[] { new Vector3(4f, 0f, 27f), new Vector3(4f, 0f, 32f), new Vector3(17.5f, 0f, 32f) })
        {
            Box("Pillar", room, pillar + Vector3.up * (WallHeight / 2f), new Vector3(0.8f, WallHeight, 0.8f), wall);
        }
        Box("Reception Desk", room, new Vector3(7f, 0.5f, 31.5f), new Vector3(3f, 1f, 1f), crate);
        Box("Bench", room, new Vector3(0.45f, 0.25f, 33.2f), new Vector3(0.6f, 0.5f, 2f), metal);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(0.45f, 0.59f, 33.2f), 50);

        // The stairs up to 2F, rising north between railings behind a shutter.
        for (int i = 0; i < 7; i++)
        {
            float height = 0.3f * (i + 1);
            Box("Step", room, new Vector3(13.8f, height / 2f, 25.3f + i * 0.55f + 0.275f), new Vector3(2.4f, height, 0.55f), concrete);
        }
        Box("Railing", room, new Vector3(12.5f, 0.5f, 27.2f), new Vector3(0.1f, 1f, 4.2f), metal);
        Box("Railing", room, new Vector3(15.1f, 0.5f, 27.2f), new Vector3(0.1f, 1f, 4.2f), metal);
        GameObject shutter = Box("Stair Shutter", room, new Vector3(13.8f, 1.2f, 25.15f), new Vector3(2.6f, 2.4f, 0.1f), metal);
        Examinable(shutter, "The stairs to the second floor are shuttered. There's no way up for now.");
        PointLight(room, new Vector3(6f, 2.9f, 29.5f), new Color(0.75f, 0.85f, 1f), 1.4f, 10f, flicker: false, shadows: true);
        PointLight(room, new Vector3(15f, 2.9f, 32f), new Color(0.75f, 0.85f, 1f), 1.1f, 9f, flicker: true, shadows: false);

        BuildRaptor(entrance, new Vector3(5f, 0f, 33.5f), 90f, ambush: false,
            new Vector3(5f, 0f, 33.5f), new Vector3(18.8f, 0f, 34f), new Vector3(17.2f, 0f, 26f), new Vector3(2f, 0f, 26f));

        room = entrancePlaza.root;
        Box("Utility Box", room, new Vector3(27.8f, 0.45f, 44.8f), new Vector3(1f, 0.9f, 1f), metal);
        BuildItem(room, "DdkN", "code disk \"N\"", "It fits the code lock marked N.", new Vector3(27.8f, 0.93f, 44.8f), codeDisk);
        Plant(room, 5f, 38f);
        Plant(room, 14f, 38f);
        Floodlight(room, new Vector3(1f, 0f, 45.8f), flicker: false);
        Floodlight(room, new Vector3(15f, 0f, 45.8f), flicker: true);
        Floodlight(room, new Vector3(22.4f, 0f, 36f), flicker: false);
        BuildRaptor(entrancePlaza, new Vector3(3f, 0f, 43f), 90f, ambush: false,
            new Vector3(3f, 0f, 43f), new Vector3(20f, 0f, 43.5f), new Vector3(12f, 0f, 37.5f), new Vector3(0.5f, 0f, 37.5f));

        room = elevatorHall.root;
        Box("Bench", room, new Vector3(0.45f, 0.25f, 20.5f), new Vector3(0.6f, 0.5f, 2.4f), metal);
        // The elevator down to B1: a closed car in the alcove, with its doors facing the hall.
        Box("Elevator", room, new Vector3(14f, WallHeight / 2f, 15.7f), new Vector3(2f, WallHeight, 2.4f), metal);
        Box("Elevator Door", room, new Vector3(13.55f, 1.2f, 16.92f), new Vector3(0.88f, 2.4f, 0.04f), door, collider: false);
        Box("Elevator Door", room, new Vector3(14.45f, 1.2f, 16.92f), new Vector3(0.88f, 2.4f, 0.04f), door, collider: false);
        Box("Floor Sign", room, new Vector3(14f, 2.6f, 16.93f), new Vector3(0.6f, 0.15f, 0.02f), lockSign, collider: false);
        GameObject callPanel = Box("Call Panel", room, new Vector3(15.3f, 1.2f, 16.95f), new Vector3(0.2f, 0.3f, 0.05f), screenGlow, collider: false);
        Goal goal = callPanel.AddComponent<Goal>();
        goal.prompt = "Call the elevator to B1";
        goal.message = "The elevator to B1 needs an ID card you don't have yet.\nThis is the end of the 1F prototype: the game doesn't continue from here.";
        PointLight(room, new Vector3(9f, 2.9f, 20.5f), new Color(0.8f, 0.85f, 1f), 1.1f, 9f, flicker: false, shadows: false);
        PointLight(room, new Vector3(14f, 2.9f, 18f), new Color(1f, 0.9f, 0.75f), 0.9f, 5f, flicker: false, shadows: false);
        BuildRaptor(elevatorHall, new Vector3(6f, 0f, 20f), 90f, ambush: false,
            new Vector3(3f, 0f, 20f), new Vector3(16f, 0f, 21f), new Vector3(9f, 0f, 15.75f));

        Door(0f, 29.25f);
        Locked(Door(9.5f, 35f, lockSign), "EntranceKey", "The front doors are locked. They need the entrance key.");
        Locked(Door(29f, 41.5f, lockSign), "CommRoom", "Locked from the other side.");
        Locked(Door(9.5f, 24f, lockSign), "DdkN", "A code lock marked \"N\". It needs the matching code disk.");
        Door(18f, 22.25f).unlocksFromSide = -1;
    }

    // 1-16 and 1-19: the hallway wrapping round the elevator hall, and the lecture room with the
    // key to the backyard gate of the generator passage.
    static void BuildLectureWing()
    {
        Transform room = lectureHallway.root;
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(19.4f, 0.1f, 13f), 10);
        PointLight(room, new Vector3(10f, 2.9f, 13.5f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: false, shadows: false);
        PointLight(room, new Vector3(19f, 2.8f, 19f), new Color(1f, 0.12f, 0.08f), 2f, 7f, flicker: true, shadows: false);
        PointLight(room, new Vector3(1f, 2.9f, 5f), new Color(0.8f, 0.85f, 1f), 0.8f, 6f, flicker: true, shadows: false);

        room = lectureRoom.root;
        // Columns of desks facing the screen on the east wall, with an aisle in line with the door.
        foreach (float columnX in new[] { 7f, 10.5f, 14f })
        {
            Box("Desk Row", room, new Vector3(columnX, 0.4f, 3.6f), new Vector3(0.8f, 0.8f, 2.6f), crate);
            Box("Desk Row", room, new Vector3(columnX, 0.4f, 8.9f), new Vector3(0.8f, 0.8f, 2.6f), crate);
        }
        Box("Lectern", room, new Vector3(17.5f, 0.6f, 6.25f), new Vector3(0.6f, 1.2f, 1f), crate);
        Box("Screen", room, new Vector3(19.82f, 1.8f, 6.25f), new Vector3(0.05f, 1.8f, 5f), screenGlow, collider: false);
        Box("Blood", room, new Vector3(5f, 0.01f, 6f), new Vector3(1.6f, 0.02f, 1.1f), blood, collider: false);
        BuildItem(room, "B1GeneratorKey", "B1 generator room key", "Its tag matches the gate between the backyard and the generator passage.", new Vector3(17.5f, 1.24f, 6.25f), brassKey);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(3f, 0.1f, 11.5f), 10);
        PointLight(room, new Vector3(8f, 2.9f, 6.25f), new Color(0.7f, 0.85f, 1f), 1.2f, 9f, flicker: false, shadows: true);
        PointLight(room, new Vector3(15f, 2.9f, 3f), new Color(0.7f, 0.85f, 1f), 1f, 7f, flicker: true, shadows: false);
        BuildRaptor(lectureRoom, new Vector3(12f, 0f, 6.25f), 270f, ambush: false,
            new Vector3(4.5f, 0f, 1.2f), new Vector3(16f, 0f, 1.2f), new Vector3(16f, 0f, 11.5f), new Vector3(4.5f, 0f, 11.5f));

        Door(2f, 6.25f);
    }

    // 1-8, 1-9, 1-11, 1-12, 1-18 and 1-21: the rest of the west wing.
    static void BuildWestWing()
    {
        Transform room = managementHallway.root;
        Plant(room, -15f, 30.5f);
        PointLight(room, new Vector3(-9f, 2.9f, 29.25f), new Color(0.8f, 0.85f, 1f), 1f, 8f, flicker: false, shadows: false);
        PointLight(room, new Vector3(-13.5f, 2.9f, 25.5f), new Color(0.8f, 0.85f, 1f), 0.8f, 6f, flicker: true, shadows: false);
        PointLight(managementHallwayEnd.root, new Vector3(-1.75f, 2.9f, 29.25f), new Color(0.8f, 0.85f, 1f), 0.6f, 4f, flicker: false, shadows: false);
        PointLight(managementVestibule.root, new Vector3(-13.5f, 2.9f, 21.5f), new Color(1f, 0.12f, 0.08f), 1.6f, 5f, flicker: true, shadows: false);

        room = lockerRoom.root;
        Box("Lockers", room, new Vector3(-21.6f, 1f, 27.25f), new Vector3(0.6f, 2f, 6f), metal);
        Box("Bench", room, new Vector3(-18.75f, 0.25f, 27.25f), new Vector3(0.5f, 0.5f, 3f), crate);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-18.75f, 0.58f, 26.5f), 10);
        PointLight(room, new Vector3(-18.75f, 2.9f, 27.25f), new Color(0.85f, 0.9f, 1f), 1f, 6f, flicker: false, shadows: false);

        room = toilet.root;
        Box("Sinks", room, new Vector3(-19.5f, 0.45f, 23.2f), new Vector3(3f, 0.9f, 0.5f), tile);
        Box("Stall Wall", room, new Vector3(-20.5f, 1f, 19.6f), new Vector3(0.08f, 2f, 2.2f), tile);
        Box("Stall Wall", room, new Vector3(-19f, 1f, 19.6f), new Vector3(0.08f, 2f, 2.2f), tile);
        BuildPickup(room, Pickup.Kind.MedPak, new Vector3(-21.3f, 0.1f, 19.1f), 50);
        PointLight(room, new Vector3(-18.75f, 2.9f, 21f), new Color(0.85f, 1f, 0.9f), 0.9f, 6f, flicker: true, shadows: false);

        room = strategyRoom.root;
        Box("Briefing Table", room, new Vector3(-9.25f, 0.45f, 15.5f), new Vector3(5f, 0.9f, 2.5f), metal);
        Box("Map Screen", room, new Vector3(-15.32f, 1.6f, 15.5f), new Vector3(0.05f, 1.6f, 4f), screenGlow, collider: false);
        PointLight(room, new Vector3(-9.25f, 2.9f, 15.5f), new Color(0.75f, 0.85f, 1f), 1.2f, 9f, flicker: false, shadows: false);

        room = controlHall.root;
        Box("Pipe", room, new Vector3(-7.75f, 2.6f, 9.4f), new Vector3(15.5f, 0.15f, 0.15f), metal, collider: false);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-14.8f, 0.1f, 5f), 10);
        PointLight(room, new Vector3(-8f, 2.9f, 10.25f), new Color(0.8f, 0.85f, 1f), 0.9f, 7f, flicker: false, shadows: false);
        PointLight(room, new Vector3(-1.5f, 2.8f, 15.5f), new Color(1f, 0.12f, 0.08f), 2f, 7f, flicker: true, shadows: false);
        PointLight(room, new Vector3(-14.25f, 2.9f, 6f), new Color(0.8f, 0.85f, 1f), 0.7f, 5f, flicker: true, shadows: false);
        BuildRaptor(controlHall, new Vector3(-14.25f, 0f, 6.5f), 0f, ambush: false,
            new Vector3(-14.25f, 0f, 5f), new Vector3(-8f, 0f, 10.25f), new Vector3(-1.5f, 0f, 16f), new Vector3(-8f, 0f, 10.25f));

        room = controlHallEnd.root;
        Box("Stairwell", room, new Vector3(-9f, 0.01f, -2.6f), new Vector3(5f, 0.02f, 2.4f), gunMetal, collider: false);
        Box("Railing", room, new Vector3(-9f, 0.5f, -1.35f), new Vector3(5f, 1f, 0.1f), metal);
        PointLight(room, new Vector3(-9.5f, 2.9f, -2f), new Color(0.8f, 0.85f, 1f), 0.5f, 5f, flicker: true, shadows: false);

        room = controlRoom.root;
        Box("Console", room, new Vector3(-0.6f, 0.5f, 4.5f), new Vector3(0.8f, 1f, 6f), metal);
        Box("Console Screens", room, new Vector3(-0.18f, 1.45f, 4.5f), new Vector3(0.05f, 0.7f, 5.6f), screenGlow, collider: false);
        Box("Desk", room, new Vector3(-6f, 0.45f, 5f), new Vector3(1.2f, 0.9f, 3f), metal);
        GameObject lift = Box("Lift Door", room, new Vector3(-4.75f, 1.2f, 0.17f), new Vector3(1.6f, 2.4f, 0.04f), door);
        Examinable(lift, "A small lift. It needs a communications ID card.");
        Box("Lift Sign", room, new Vector3(-4.75f, 2.6f, 0.17f), new Vector3(0.5f, 0.12f, 0.03f), lockSign, collider: false);
        BuildPickup(room, Pickup.Kind.Ammo, new Vector3(-10.8f, 0.1f, 8.4f), 10);
        PointLight(room, new Vector3(-6f, 2.9f, 4.5f), new Color(0.7f, 1f, 0.8f), 1.2f, 9f, flicker: true, shadows: false);

        const string laserMessage = "A laser shutter blocks the way. It's switched from somewhere else.";
        Powered(Door(-3.5f, 29.25f, lockSign, laserShutter: true), laserConsole, laserMessage);
        Powered(Door(-13.5f, 24f, lockSign, laserShutter: true), laserConsole, laserMessage);
        Door(-15.5f, 29.25f);
        Door(-15.5f, 21.5f);
        Locked(Door(-13.5f, 19.5f, lockSign), "MilitaryID", "Locked. It needs a military ID card.");
        Door(-7.75f, 9f);
        Locked(Door(-14.25f, 3.8f, lockSign), "StairShutter", "A heavy metal shutter blocks the stairs down. It won't move.");
    }

    // 1-7 and 1-17: the piping ducts. Each vent in a room is linked to one at the end of a duct.
    static void BuildDucts()
    {
        foreach ((Room room, Vector3 light) in new[] { (ductA, new Vector3(-47f, 2.6f, -30f)), (ductA, new Vector3(-36f, 2.6f, -35f)), (ductA, new Vector3(-25f, 2.6f, -30f)),
                     (ductB, new Vector3(-47f, 2.6f, -16f)), (ductB, new Vector3(-38f, 2.6f, -21f)), (ductB, new Vector3(-29f, 2.6f, -16f)) })
        {
            PointLight(room.root, light, new Color(0.5f, 1f, 0.6f), 0.7f, 6f, flicker: false, shadows: false);
        }
        foreach (Room room in new[] { ductA, ductB })
        {
            foreach (Rect rect in room.rects)
            {
                Box("Duct Lining", room.root, new Vector3(rect.center.x, 0.02f, rect.center.y), new Vector3(rect.width - 0.2f, 0.02f, rect.height - 0.2f), duct, collider: false);
            }
        }

        // Yaw 180 faces south out of a north wall, 0 north, 90 east.
        LinkVents(BuildVent(officeHallway, new Vector3(25f, 0f, 30.82f), 180f), BuildVent(ductA, new Vector3(-25f, 0f, -26.18f), 180f, inDuct: true));
        LinkVents(BuildVent(controlHall, new Vector3(-2.82f, 0f, 17f), 90f), BuildVent(ductA, new Vector3(-37f, 0f, -37.82f), 0f, inDuct: true));
        LinkVents(BuildVent(managementHallwayEnd, new Vector3(-1.75f, 0f, 30.82f), 180f), BuildVent(ductA, new Vector3(-47f, 0f, -26.18f), 180f, inDuct: true));
        LinkVents(BuildVent(toilet, new Vector3(-21.82f, 0f, 22.5f), 90f), BuildVent(ductB, new Vector3(-47f, 0f, -12.18f), 180f, inDuct: true));
        LinkVents(BuildVent(lectureHallway, new Vector3(2.75f, 0f, 16.82f), 180f), BuildVent(ductB, new Vector3(-29f, 0f, -12.18f), 180f, inDuct: true));
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

    static void Open(Room a, Room b) => openings.Add((a, b));

    static bool IsOpen(Room a, Room b) => openings.Exists(pair => (pair.Item1 == a && pair.Item2 == b) || (pair.Item1 == b && pair.Item2 == a));

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
                if (one == other || IsOpen(one, other)) continue;
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

    // A double sliding door at (x, z), which must lie on a room's edge. Its local +Z faces north or
    // east into the front room; a door in an outer wall leads nowhere and is meant to stay locked.
    // A laser shutter is the same door with beams for halves.
    static SlidingDoor Door(float x, float z, Material signMaterial = null, bool laserShutter = false)
    {
        bool alongX = rooms.Exists(room => room.rects.Exists(rect =>
            (Mathf.Approximately(z, rect.yMin) || Mathf.Approximately(z, rect.yMax)) && x > rect.xMin && x < rect.xMax));
        Vector3 point = new(x, 0f, z);
        Vector3 normal = alongX ? Vector3.forward : Vector3.right;
        Room front = RoomAt(point + normal * 0.5f);
        Room back = RoomAt(point - normal * 0.5f);
        doorways.Add((alongX, alongX ? z : x, alongX ? x : z));

        string frontName = front != null ? front.name : "Outside";
        string backName = back != null ? back.name : "Outside";
        Transform root = new GameObject($"Door {frontName} / {backName}").transform;
        root.SetParent((front ?? back).root, false);
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
        slidingDoor.frontRoomName = front != null ? front.name.ToUpperInvariant() : "";
        slidingDoor.backRoomName = back != null ? back.name.ToUpperInvariant() : "";

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

    static void Powered(SlidingDoor door, PowerSwitch source, string message)
    {
        door.powerSource = source;
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

    // A grate low on a wall; `yaw` turns its front out of the wall into the room.
    static Vent BuildVent(Room room, Vector3 position, float yaw, bool inDuct = false)
    {
        GameObject grate = Box("Vent", room.root, position + Vector3.up * 0.45f, new Vector3(1f, 0.6f, 0.06f), gunMetal, collider: false);
        grate.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Vent vent = grate.AddComponent<Vent>();
        vent.prompt = inDuct ? "Climb out of the duct" : "Crawl into the duct";
        return vent;
    }

    static void LinkVents(Vent a, Vent b)
    {
        a.exit = b;
        b.exit = a;
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

        GameObject gun = Part(PrimitiveType.Cube, "Handgun", root.transform, new Vector3(0.24f, 0.95f, 0.12f), Vector3.zero, new Vector3(0.07f, 0.1f, 0.28f), gunMetal);
        Animator animator = AttachEve(root.transform, gun.transform);

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
        player.animator = animator;
        ExcludeFromNavMesh(root);
        return player;
    }

    // Eve replaces the capsule body. The handgun sits on the Mixamo right-hand bone, so it follows
    // the pistol clips. Without the model, the old capsule body is built instead.
    static Animator AttachEve(Transform player, Transform gun)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAnimatorBuilder.ModelPath);
        if (prefab == null)
        {
            Part(PrimitiveType.Capsule, "Body", player, new Vector3(0f, 0.75f, 0f), Vector3.zero, new Vector3(0.5f, 0.75f, 0.35f), suit);
            Part(PrimitiveType.Sphere, "Head", player, new Vector3(0f, 1.58f, 0f), Vector3.zero, Vector3.one * 0.27f, skin);
            Part(PrimitiveType.Sphere, "Hair", player, new Vector3(0f, 1.64f, -0.03f), Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f), hair);
            return null;
        }

        PlayerAnimatorBuilder.Build();
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.name = "Eve";
        model.transform.SetParent(player, false);
        Animator animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
        animator.applyRootMotion = false;

        Transform hand = FindBone(model.transform, "RightHand");
        if (hand != null)
        {
            gun.SetParent(hand, false);
            gun.localPosition = new Vector3(0f, 0.04f, 0.02f);
            gun.localRotation = Quaternion.Euler(0f, 90f, 90f);
            gun.localScale = Vector3.one * 0.4f;
        }
        return animator;
    }

    static Transform FindBone(Transform root, string bone)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            if (child.name.EndsWith(bone)) return child;
        }
        return null;
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
        duct = Mat("Duct", new Color(0.35f, 0.5f, 0.4f), smoothness: 0.5f);
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
