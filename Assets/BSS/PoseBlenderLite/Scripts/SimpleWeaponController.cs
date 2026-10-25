// WeaponController.cs
using UnityEngine;

namespace BSS.PoseBlender.SimpleController
{
    public class SimpleWeaponController : MonoBehaviour
    {
        [SerializeField] private Animator weaponAnimator;

        [Header("Input")]
        [SerializeField] KeyCode reloadKeyCode = KeyCode.R;
        [SerializeField] KeyCode inspectKeyCode = KeyCode.I;

        private void Awake()
        {
            if (weaponAnimator == null)
                Debug.LogWarning("No weapon animator found!");
        }

        private void Update()
        {
            if (Input.GetKeyDown(reloadKeyCode) && weaponAnimator != null)
            {
                weaponAnimator.SetTrigger("Reload");
            }
            if (Input.GetKeyDown(inspectKeyCode) && weaponAnimator != null)
            {
                weaponAnimator.SetTrigger("Inspect");
            }
        }
    }
}