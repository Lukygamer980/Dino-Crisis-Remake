using System.Linq;
using UnityEditor;
using UnityEngine;

// Sounds and effects from the downloaded packs, wired into the character prefabs when they are built.
// Everything assigned here shows up on the prefab's components, where it can be swapped by hand.
public static class GameFeedback
{
    const string Sounds = "Assets/Weapons of Choice FREE - Komposite Sound/";
    const string Effects = "Assets/UnityTechnologies/ParticlePack/EffectExamples/Weapon Effects/Prefabs/";

    internal static void SetUpPlayer(PlayerController player)
    {
        AudioSource source = player.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        // Mostly in the head of the player: the camera is never far from her.
        source.spatialBlend = 0.3f;
        player.audioSource = source;

        player.fireSounds = Enumerable.Range(1, 5).Select(i => Clip($"GUN/Pistol_01_Fire_0{i}_SFX.wav")).Where(c => c != null).ToArray();
        player.shellSounds = new[] { Clip("BULLETS/Shell/Shell_Short_01_SFX.wav"), Clip("BULLETS/Shell/Shell_Short_02_SFX.wav") }.Where(c => c != null).ToArray();
        player.reloadSound = Clip("GUN/Handling_Gun_01_Reload_Sq_SFX.wav");
        player.drawSound = Clip("GUN/Handling_Gun_01_Arming_SFX.wav");
        // The pack has no dry-fire click; the magazine going in is the nearest short click.
        player.dryFireSound = Clip("GUN/Handling_Gun_01_Clip_In_SFX.wav");

        player.fleshImpact = Prefab("BulletImpactFleshBigEffect.prefab");
        player.metalImpact = Prefab("BulletImpactMetalEffect.prefab");
        player.stoneImpact = Prefab("BulletImpactStoneEffect.prefab");

        GameObject muzzle = Prefab("MuzzleFlashEffect.prefab");
        if (muzzle != null && player.gunPoint != null)
        {
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(muzzle, player.gunPoint);
            effect.name = "Muzzle Flash";
            effect.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // The pack's flash is sized for a rifle.
            effect.transform.localScale = Vector3.one * 0.35f;
            foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.playOnAwake = false;
                main.loop = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            // GetComponentInChildren includes the root itself.
            player.muzzleEffect = effect.GetComponentInChildren<ParticleSystem>(true);
        }
    }

    static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + path);

    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Effects + name);
}
