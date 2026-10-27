using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// A bare, well-lit arena for working the bugs out of the characters before they go into the level.
// It places the same prefabs as SceneBuilder (see CharacterPrefabs), so whatever is tuned here on
// the prefabs (in Prefab Mode, or by applying a scene instance's overrides) shows up in the level.
// Changes left as overrides on the arena's instances stay in this scene and are lost when it is
// rebuilt with "Dino Demo > Build Character Test Arena".
//
// The south half is Eve's: a 36 m sprint lane along the south wall with a marker every 5 m, a 2 m
// wide corridor for the camera, a low wall and a crate to crouch behind, and steps up to a platform
// with a ramp down the far side. A wall across the middle, with a doorway and a wider opening, keeps
// the raptors' half out of sight from the start. There one raptor patrols round the pillars; a
// second stands switched off, to be turned on for testing the pack call.
public static class TestArenaBuilder
{
    public const string ScenePath = "Assets/Scenes/CharacterTest.unity";
    const string NavMeshPath = "Assets/Scenes/CharacterTest NavMesh.asset";
    const float WallHeight = 3.2f;
    const float Half = 20f;

    static Transform arena;
    // The HomebrewIK demo's look: a white checkered floor and walls, and blue obstacles.
    static Material floor, wall, obstacle;

    [MenuItem("Dino Demo/Build Character Test Arena")]
    public static void Build()
    {
        SceneBuilder.CreateMaterials();
        floor = SceneBuilder.Mat("Arena Floor", new Color(0.85f, 0.85f, 0.85f), smoothness: 0.3f, grid: true);
        wall = SceneBuilder.Mat("Arena Wall", new Color(0.7f, 0.7f, 0.72f), smoothness: 0.3f, grid: true);
        obstacle = SceneBuilder.Mat("Arena Obstacle", new Color(0.216f, 0.784f, 1f), smoothness: 0.4f, grid: true);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CharacterPrefabs.Prepare();
        SetUpLighting();

        arena = new GameObject("Arena").transform;
        BuildShell();
        BuildPlayerHalf();
        BuildRaptorHalf();

        PlayerController player = SceneBuilder.BuildPlayer(new Vector3(-17.5f, 0f, -16f), 90f);
        BuildPickups();
        SceneBuilder.BuildCameraAndUI(player, "Character Test Arena");

        SceneBuilder.BakeNavMesh(NavMeshPath);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("Character test arena built: " + ScenePath);
    }

    // Bright and fog-free, so animation and contact problems are easy to see.
    static void SetUpLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.31f, 0.33f);
        RenderSettings.fog = false;

        Light sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        sun.intensity = 0.9f;
        sun.shadows = LightShadows.Soft;
    }

    static void BuildShell()
    {
        SceneBuilder.Box("Floor", arena, new Vector3(0f, -0.05f, 0f), new Vector3(2f * Half, 0.1f, 2f * Half), floor);
        float t = 0.3f;
        Block("Wall South", -Half - t, -Half - t, Half + t, -Half, WallHeight, wall);
        Block("Wall North", -Half - t, Half, Half + t, Half + t, WallHeight, wall);
        Block("Wall West", -Half - t, -Half, -Half, Half, WallHeight, wall);
        Block("Wall East", Half, -Half, Half + t, Half, WallHeight, wall);

        // Across the middle: a doorway of the level's size (1.8 x 2.4 m) at x = -8 and a 4 m opening at x = 8.
        Block("Middle Wall", -Half, -0.15f, -8.9f, 0.15f, WallHeight, wall);
        Block("Middle Wall", -7.1f, -0.15f, 6f, 0.15f, WallHeight, wall);
        Block("Middle Wall", 10f, -0.15f, Half, 0.15f, WallHeight, wall);
        Block("Doorway Lintel", -8.9f, -0.15f, -7.1f, 0.15f, WallHeight - 2.4f, wall, 2.4f);
    }

    static void BuildPlayerHalf()
    {
        // The sprint lane runs east along z = -16; time Eve between markers to check her speeds.
        for (float x = -15f; x <= 15f; x += 5f)
            SceneBuilder.Box("Lane Marker", arena, new Vector3(x, 0.005f, -16f), new Vector3(0.1f, 0.01f, 3f), SceneBuilder.hazard, collider: false);

        // A corridor as narrow as the level's tightest, for the camera's wall collision.
        Block("Corridor Wall", -15.3f, -11f, -15f, -3f, WallHeight, wall);
        Block("Corridor Wall", -13f, -11f, -12.7f, -3f, WallHeight, wall);

        // Cover: a 1.4 m wall and a 1 m crate to crouch behind.
        Block("Cover Wall", -4f, -6.15f, 0f, -5.85f, 1.4f, obstacle);
        Block("Crate", -8f, -9.6f, -6.8f, -8.4f, 1f, obstacle);

        // Eight 15 cm steps up to a 1.2 m platform, and a 13.5 degree ramp down its east side.
        const float stepRise = 0.15f, stepRun = 0.35f, platformHeight = 1.2f;
        for (int i = 0; i < 8; i++)
        {
            float x0 = 4.7f + i * stepRun;
            Block("Step", x0, -10f, x0 + stepRun, -6f, (i + 1) * stepRise, obstacle);
        }
        Block("Platform", 7.5f, -10f, 11f, -6f, platformHeight, obstacle);
        Ramp("Ramp", 11f, 16f, -10f, -6f, platformHeight);
    }

    static void BuildRaptorHalf()
    {
        foreach (float x in new[] { -10f, -4f, 4f, 10f })
            foreach (float z in new[] { 7f, 13f })
                Block("Pillar", x - 0.3f, z - 0.3f, x + 0.3f, z + 0.3f, WallHeight, obstacle);
        Block("Crate", -1f, 9.4f, 1f, 10.6f, 1f, obstacle);

        Transform raptors = new GameObject("Raptors").transform;
        SceneBuilder.PlaceRaptor(raptors, "Velociraptor", new Vector3(0f, 0f, 16.5f), 180f,
            new Vector3(-14f, 0f, 16.5f), new Vector3(14f, 0f, 16.5f), new Vector3(14f, 0f, 3.5f), new Vector3(-14f, 0f, 3.5f));
        // Off at the start: switch it on to test two raptors at once.
        SceneBuilder.PlaceRaptor(raptors, "Velociraptor (Off)", new Vector3(16f, 0f, 10f), 270f,
            new Vector3(16f, 0f, 10f), new Vector3(16f, 0f, 3.5f)).gameObject.SetActive(false);
    }

    // Ammo and a med pak by the start, for testing reloads and healing.
    static void BuildPickups()
    {
        SceneBuilder.BuildPickup(arena, Pickup.Kind.Ammo, new Vector3(-18.5f, 0.1f, -18.5f), 30);
        SceneBuilder.BuildPickup(arena, Pickup.Kind.MedPak, new Vector3(-17f, 0.1f, -18.5f), 50);
    }

    // A box standing on the floor (or at `baseY`) over the rectangle x0..x1, z0..z1.
    static void Block(string name, float x0, float z0, float x1, float z1, float height, Material material, float baseY = 0f)
    {
        var center = new Vector3((x0 + x1) * 0.5f, baseY + height * 0.5f, (z0 + z1) * 0.5f);
        SceneBuilder.Box(name, arena, center, new Vector3(Mathf.Abs(x1 - x0), height, Mathf.Abs(z1 - z0)), material);
    }

    // A ramp running from `height` at x0 down to the floor at x1.
    static void Ramp(string name, float x0, float x1, float z0, float z1, float height)
    {
        const float thickness = 0.2f;
        float run = x1 - x0;
        float angle = Mathf.Atan2(height, run) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, 0f, -angle);
        Vector3 topCenter = new((x0 + x1) * 0.5f, height * 0.5f, (z0 + z1) * 0.5f);
        GameObject ramp = SceneBuilder.Box(name, arena, topCenter - rotation * Vector3.up * (thickness * 0.5f),
            new Vector3(Mathf.Sqrt(run * run + height * height), thickness, Mathf.Abs(z1 - z0)), obstacle);
        ramp.transform.localRotation = rotation;
    }
}
