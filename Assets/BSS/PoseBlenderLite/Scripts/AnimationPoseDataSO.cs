using System.Collections.Generic;
using UnityEngine;

namespace BSS.PoseBlender
{
    [CreateAssetMenu(fileName = "NewAnimationPoseData", menuName = "Pose Blender Lite/New Animation Pose Data")]
    public class AnimationPoseDataSO : ScriptableObject
    {
        [System.Serializable]
        public class BonePoseData
        {
            public string boneName;
            public string bonePath;        // Relative path from the recording root.
            public Quaternion localRotation;

            public bool isPose = false;
            [Range(0f, 1f)] public float resetBlendWeight = 1f;
        }

        public List<BonePoseData> boneTransforms = new List<BonePoseData>();
    }
}