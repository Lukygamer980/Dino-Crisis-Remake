using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The project renders with URP (the Universal Render Pipeline). This keeps its settings in
// Assets/Settings, where nothing depends on a demo folder, and upgrades the built-in Standard
// materials of the packs the game uses, which URP would otherwise draw pink.
//
// "Dino Demo > Set Up URP" is safe to run again: it only creates what is missing and only touches
// materials that are still on a built-in shader.
public static class RenderSetup
{
    const string Folder = "Assets/Settings";
    public const string PipelinePath = Folder + "/DinoDemo URP.asset";
    const string RendererPath = Folder + "/DinoDemo URP Renderer.asset";
    const string GlobalSettingsPath = Folder + "/UniversalRenderPipelineGlobalSettings.asset";
    // The Particle Pack's "Beautiful" quality, which the project was switched to when the pack came in.
    const string PackSettings = "Assets/UnityTechnologies/ParticlePack/Settings/";
    const string SourcePipeline = PackSettings + "Beautiful_PipelineAsset.asset";
    const string SourceRenderer = PackSettings + "Beautiful_PipelineAsset_DeferredRenderer.asset";
    const string SourceGlobalSettings = PackSettings + "UniversalRenderPipelineGlobalSettings.asset";

    // Packs whose models the game places. Other packs' demo materials are left as they came.
    static readonly string[] UsedPacks =
    {
        "Assets/Characters", "Assets/Animations", "Assets/PBRVelociraptor", "Assets/FreakGames", "Assets/GeeKay3D", "Assets/Scenes/Pistol 92",
        // The muzzle flash and the bullet impacts.
        "Assets/UnityTechnologies/ParticlePack/EffectExamples/Weapon Effects", "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Materials",
    };

    [MenuItem("Dino Demo/Set Up URP")]
    public static void SetUp()
    {
        EnsurePipeline();
        UpgradeMaterials(UsedPacks);
        // Models with their materials inside the file only get URP materials when imported again.
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Characters" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Any(m => !IsUrp(m)))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.SaveAssets();
    }

    static void EnsurePipeline()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Settings");
        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(GlobalSettingsPath) == null && AssetDatabase.LoadAssetAtPath<ScriptableObject>(SourceGlobalSettings) != null)
            AssetDatabase.MoveAsset(SourceGlobalSettings, GlobalSettingsPath);

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            AssetDatabase.CopyAsset(SourceRenderer, RendererPath);
            AssetDatabase.CopyAsset(SourcePipeline, PipelinePath);
            pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            var serialized = new SerializedObject(pipeline);
            SerializedProperty list = serialized.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        // Every quality level uses the default pipeline, so switching quality never changes the look.
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = null;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    internal static bool IsUrp(Material material) =>
        material != null && material.shader != null && (material.shader.name.StartsWith("Universal Render Pipeline") || material.shader.name.StartsWith("Shader Graphs"));

    // Upgrades every material under `folders` that is still on a built-in shader URP knows how to replace.
    public static void UpgradeMaterials(string[] folders)
    {
        List<MaterialUpgrader> upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
        var failed = new List<string>();
        int upgraded = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", folders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat")) continue;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || IsUrp(material) || material.shader.name.StartsWith("Hidden")) continue;
            string message = "";
            if (UpgradeLegacyParticle(material))
            {
                EditorUtility.SetDirty(material);
                upgraded++;
            }
            else if (MaterialUpgrader.Upgrade(material, upgraders, MaterialUpgrader.UpgradeFlags.None, ref message))
            {
                EditorUtility.SetDirty(material);
                upgraded++;
            }
            else failed.Add(path + " (" + material.shader.name + ")");
        }
        Debug.Log($"URP: upgraded {upgraded} materials." + (failed.Count > 0 ? " Not upgraded:\n" + string.Join("\n", failed) : ""));
    }

    // URP has no upgrader for the legacy particle shaders, which the Particle Pack's effects still
    // use: they become URP's unlit particles, transparent, blended the same way, with the same
    // texture and tint (the legacy tint counts double).
    static bool UpgradeLegacyParticle(Material material)
    {
        string name = material.shader.name;
        if (!name.StartsWith("Legacy Shaders/Particles/") && !name.StartsWith("Particles/")) return false;
        bool additive = name.Contains("Additive");
        Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        Vector2 tiling = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
        Color tint = material.HasProperty("_TintColor") ? material.GetColor("_TintColor") * 2f : material.HasProperty("_Color") ? material.color : Color.white;
        material.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Surface", 1f);
        // URP's blend modes: 0 alpha, 1 premultiply, 2 additive, 3 multiply.
        material.SetFloat("_Blend", additive ? 2f : 0f);
        BaseShaderGUI.SetMaterialKeywords(material, null, UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI.SetMaterialKeywords);
        return true;
    }
}
