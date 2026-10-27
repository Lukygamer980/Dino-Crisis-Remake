using System.IO;
using UnityEditor;
using UnityEngine;

// Every pickup is a prefab in Assets/Prefabs/Pickups: a root with the Pickup component, and a child
// "Model" that is only the look. To change how ammo, a Med Pak, a key or a card looks, open its
// prefab and replace or move the Model child (or its material); every pickup in both scenes follows.
// The scene builders only set what each placed pickup gives (amounts, item ids and names).
//
// Like the character prefabs, these are only built when missing, or again from code by
// "Dino Demo > Rebuild Character Prefabs".
public static class PickupPrefabs
{
    public const string Folder = "Assets/Prefabs/Pickups";
    public enum Look { Ammo, MedPak, BrassKey, CodeDisk, KeyCard, MilitaryCard }

    const string AmmoModel = "Assets/FreakGames/9MM Ammo Pack/Ammo Box/Prefabs/Ammo Box.prefab";
    const string MedPakModel = "Assets/GeeKay3D/First-Aid-Set/Assets/Prefabs/FirstAidKit_Red.prefab";

    public static string PathOf(Look look) => $"{Folder}/{Name(look)}.prefab";

    public static GameObject Prefab(Look look) => AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(look));

    static string Name(Look look) => look switch
    {
        Look.MedPak => "Med Pak",
        Look.BrassKey => "Brass Key",
        Look.CodeDisk => "Code Disk",
        Look.KeyCard => "Key Card",
        Look.MilitaryCard => "Military Card",
        _ => look.ToString(),
    };

    internal static void Prepare(bool rebuild)
    {
        Directory.CreateDirectory(Folder);
        foreach (Look look in System.Enum.GetValues(typeof(Look)))
        {
            if (!rebuild && Prefab(look) != null) continue;
            GameObject root = Build(look);
            try { PrefabUtility.SaveAsPrefabAsset(root, PathOf(look)); }
            finally { Object.DestroyImmediate(root); }
        }
    }

    // The root sits where the old placeholder box's center was, so the bottom of each model lands
    // where the bottom of that box did, on a shelf or the floor.
    static GameObject Build(Look look)
    {
        GameObject root = new(Name(look));
        Pickup pickup = root.AddComponent<Pickup>();
        switch (look)
        {
            case Look.Ammo:
                pickup.kind = Pickup.Kind.Ammo;
                pickup.amount = 10;
                Model(root, AmmoModel, Vector3.zero, 0.24f, 0.08f, SceneBuilder.ammoBox, new Vector3(0.28f, 0.16f, 0.2f));
                break;
            case Look.MedPak:
                pickup.kind = Pickup.Kind.MedPak;
                pickup.amount = 50;
                // The kit stands upright in its pack; it lies flat here.
                Model(root, MedPakModel, new Vector3(90f, 0f, 0f), 0.32f, 0.09f, SceneBuilder.medPak, new Vector3(0.3f, 0.18f, 0.3f));
                break;
            default:
                pickup.kind = Pickup.Kind.Item;
                BuildItemLook(root, look);
                break;
        }
        return root;
    }

    // A downloaded model, scaled so its longest side is `size` meters, centered, with its bottom
    // `halfHeight` below the root. Without the model, the old placeholder box.
    static void Model(GameObject root, string path, Vector3 euler, float size, float halfHeight, Material fallback, Vector3 fallbackScale)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null)
        {
            SceneBuilder.Part(PrimitiveType.Cube, "Model", root.transform, Vector3.zero, Vector3.zero, fallbackScale, fallback);
            return;
        }
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localRotation = Quaternion.Euler(euler);
        CharacterPrefabs.StripPhysics(model);
        Bounds bounds = Bounds(model);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest > 0f) model.transform.localScale = Vector3.one * (size / longest);
        bounds = Bounds(model);
        model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y - halfHeight, -bounds.center.z);
    }

    static Bounds Bounds(GameObject model)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (first) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            first = false;
        }
        return bounds;
    }

    // Keys, the code disk and the cards have no model in the downloaded packs yet: small shapes in
    // the old colours, lying flat, 3 cm thick at most.
    static void BuildItemLook(GameObject root, Look look)
    {
        Transform model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        switch (look)
        {
            case Look.BrassKey:
                SceneBuilder.Part(PrimitiveType.Cylinder, "Bow", model, new Vector3(-0.06f, 0f, 0f), Vector3.zero, new Vector3(0.06f, 0.008f, 0.06f), SceneBuilder.brassKey);
                SceneBuilder.Part(PrimitiveType.Cube, "Shank", model, new Vector3(0.02f, 0f, 0f), Vector3.zero, new Vector3(0.12f, 0.012f, 0.014f), SceneBuilder.brassKey);
                SceneBuilder.Part(PrimitiveType.Cube, "Bit", model, new Vector3(0.07f, 0f, -0.015f), Vector3.zero, new Vector3(0.02f, 0.012f, 0.02f), SceneBuilder.brassKey);
                break;
            case Look.CodeDisk:
                SceneBuilder.Part(PrimitiveType.Cylinder, "Disk", model, Vector3.zero, Vector3.zero, new Vector3(0.12f, 0.004f, 0.12f), SceneBuilder.codeDisk);
                break;
            case Look.KeyCard:
                SceneBuilder.Part(PrimitiveType.Cube, "Card", model, Vector3.zero, Vector3.zero, new Vector3(0.086f, 0.004f, 0.054f), SceneBuilder.keyCard);
                break;
            case Look.MilitaryCard:
                SceneBuilder.Part(PrimitiveType.Cube, "Card", model, Vector3.zero, Vector3.zero, new Vector3(0.086f, 0.004f, 0.054f), SceneBuilder.militaryCard);
                break;
        }
    }
}
