using MalbersAnimations.Items;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // Super Item AI Decision: state of the Super Item Manager (Self or Target) — aiming (optionally at this AI) or equipping
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Super Item/Manager State", order = 300)]
    public class SIManagerStateDecision : MAIDecision
    {
        public override string DisplayName => "Super Item/Manager State";

        public enum CheckOption { Aiming, Equipping }

        [Tooltip("Check the Decision on the Animal(Self) or the Target(Target)")]
        public Affected CheckOn = Affected.Target;

        [Tooltip("Aiming: the Aim component is active.\nEquipping: an Equip/Unequip animation is in flight")]
        public CheckOption Check = CheckOption.Aiming;

        [Hide(nameof(Check), (int)CheckOption.Aiming)]
        [Tooltip("Target only: true when the Target is aiming AT this AI (its Aim Target or Aim Ray hits me, or its aim direction points at me within the Aim Angle)")]
        public bool AimingAtMe = false;

        [Hide(nameof(AimingAtMe))]
        [Tooltip("Aiming At Me: tolerance angle between the Target's aim direction and the direction to this AI, used when its aim ray/target is not on me")]
        [Range(0f, 90f)]
        public float AimAngle = 10f;

        /// <summary>Caches the Super Item Manager of the Animal or the Target</summary>
        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            switch (CheckOn)
            {
                case Affected.Self:
                    brain.DecisionsVars[Index].mono = brain.Animal.FindComponent<SuperItemManager>(); //Cache the Super Item Manager in the Animal
                    break;
                case Affected.Target:
                    brain.DecisionsVars[Index].mono = brain.Target != null ? brain.Target.FindComponent<SuperItemManager>() : null; //Cache the Super Item Manager in the Target
                    break;
                default:
                    break;
            }
        }

        public override bool Decide(MAnimalBrain brain, int Index)
        {
            var manager = brain.DecisionsVars[Index].mono as SuperItemManager;
            if (manager == null) return false;

            return Check switch
            {
                CheckOption.Aiming => IsAiming(brain, manager),
                CheckOption.Equipping => manager.IsEquipping,
                _ => false,
            };
        }

        /// <summary>True when the Aim component is active, and (Target + Aiming At Me) when it points at this AI</summary>
        private bool IsAiming(MAnimalBrain brain, SuperItemManager manager)
        {
            var aim = manager.Aim;
            if (aim == null || !aim.Active) return false;

            if (!AimingAtMe || CheckOn != Affected.Target) return true; //Self can only aim at others

            var me = brain.Animal.transform;

            //1. The aimer has a forced/assisted Aim Target on me
            if (aim.AimTarget != null && aim.AimTarget.SameHierarchy(me)) return true;

            //2. The aim ray is hitting one of my colliders
            if (aim.AimHit.transform != null && aim.AimHit.transform.SameHierarchy(me)) return true;

            //3. The aim direction points at my center within the tolerance angle
            var origin = aim.AimOrigin != null ? aim.AimOrigin.position : aim.transform.position;
            var direction = aim.AimDirection.sqrMagnitude > 0f ? aim.AimDirection : aim.transform.forward;

            return Vector3.Angle(direction, brain.Animal.Center - origin) <= AimAngle;
        }

        private void Reset() => Description = "Check the state of the Super Item Manager (Self or Target): aiming (optionally aiming AT this AI, e.g. the Player is aiming at me) or equipping an Item";
    }
}
