// Last Change: 9/19/2026 2:40pm
using MalbersAnimations.Events;
using MalbersAnimations.HAP;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.InputSystem
{
    //MWC — ships inside the Rider folder but compiles into MalbersAnimations.InputSystem through the .asmref next to
    //it: MRider lives in the MalbersAnimations assembly, which cannot reference the Input System one, so the MRider
    //context item that authors MInputLink rows cannot be in MRider.cs (same reason 'MInputLink AutoConnections' wires
    //MAnimal from the Input System side).
    public partial class MInputLink
    {
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
        /// <summary>Right click on the MRider: creates the Mount, Dismount and CallMount rows on its MInputLink</summary>
        [MenuItem("CONTEXT/MRider/Create Mount Inputs")]
        private static void CreateRiderMountInputs(MenuCommand command)
        {
            if (command.context is not MRider rider) return;

            var source = rider.FindInterface<IInputSource>();

            if (source != null && source is not MInputLink)
            {
                Debug.LogWarning($"[{rider.name}] uses the legacy <B>{source.GetType().Name}</B>. Use <B>[Create Mount Inputs (Legacy MInput)]</B> instead", rider);
                return;
            }

            var link = source as MInputLink;

            if (link == null) link = Undo.AddComponent<MInputLink>(rider.gameObject);

            link.CreateRiderMountInputs(rider);
        }

        [ContextMenu("Connect/'Interact' -> MRider Mount Inputs")]
        private void Connect_RiderMountInputs()
        {
            var rider = this.FindComponent<MRider>();

            if (rider != null) CreateRiderMountInputs(rider);
            else Debug.Log("No MRider found");
        }

        /// <summary>Creates Mount (Press), Dismount (LongPress) and CallMount (LongPress) on the 'Interact' action and wires them to the Rider</summary>
        //MWC — mirrors the shipped Rider setup: all three rows share the one Interact action (see
        //MInputAction.Active, which must never toggle that shared action), and the Rider's MountInputs pairs do the
        //enable/disable dance instead of OnEndMounting/OnStartDismounting events.
        internal void CreateRiderMountInputs(MRider rider)
        {
            if (InputActions == null) InputActions = MTools.GetInstance<InputActionAsset>("Malbers Inputs");

            if (InputActions == null)
            {
                Debug.LogWarning($"[{name}] has no Input Actions asset. Assign one first", this);
                return;
            }

            if (m_MapButtons == null || m_MapButtons.Count == 0) CheckActionMaps();

            var mapIndex = Mathf.Clamp(ActiveActionMapIndex - 1, 0, m_MapButtons.Count - 1);
            var map = m_MapButtons[mapIndex];
            var actionMap = InputActions.actionMaps[mapIndex];

            map.buttons ??= new List<MInputAction>();

            var interact = GetActionReferenceFromAssets(GetAllActionsFromAsset(InputActions), actionMap, "Interact");

            if (interact == null)
            {
                Debug.LogWarning($"No <B>Interact</B> action found on the [{actionMap.name}] map of {InputActions.name}", this);
                return;
            }

            var dismountUI = MTools.GetInstance<MEvent>("Rider Dismount UI");

            //Mount: Press -> MRider.MountAnimal. Disabled by the Rider while mounted
            if (!HasButton(map, "Mount"))
            {
                var mount = new MInputAction("Mount", interact, MInputInteraction.Press);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(mount.OnInputDown, rider.MountAnimal);
                map.buttons.Add(mount);

                Debug.Log("<B>Mount</B> Input created and connected to Rider.MountAnimal");
            }

            //Dismount: LongPress -> MRider.DismountAnimal. Starts disabled, the Rider enables it while mounted
            if (!HasButton(map, "Dismount"))
            {
                var dismount = new MInputAction("Dismount", interact, MInputInteraction.LongPress) { LongPressDelay = new(0.15f) };
                dismount.active.Value = false;

                UnityEditor.Events.UnityEventTools.AddPersistentListener(dismount.OnLongPress, rider.DismountAnimal);

                if (dismountUI != null)
                {
                    UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(dismount.OnInputDown, dismountUI.Invoke, true);
                    UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(dismount.OnInputUp, dismountUI.Invoke, false);
                    UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<Transform>(dismount.OnLongPress, dismountUI.SetSender, rider.transform);
                    UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(dismount.OnLongPress, dismountUI.Invoke, false);
                    UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<Transform>(dismount.OnInputFloatValue, dismountUI.SetSender, rider.transform);
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(dismount.OnInputFloatValue, dismountUI.Invoke);
                }

                map.buttons.Add(dismount);

                Debug.Log("<B>Dismount</B> Input created and connected to Rider.DismountAnimal");
            }

            //CallMount: LongPress -> MRider.CallAnimalToggle. Disabled by the Rider while mounted
            if (!HasButton(map, "CallMount"))
            {
                var callMount = new MInputAction("CallMount", interact, MInputInteraction.LongPress) { LongPressDelay = new(0.15f) };
                UnityEditor.Events.UnityEventTools.AddPersistentListener(callMount.OnLongPress, rider.CallAnimalToggle);
                map.buttons.Add(callMount);

                Debug.Log("<B>CallMount</B> Input created and connected to Rider.CallAnimalToggle");
            }

            map.InvalidateButtonLookup();

            AddMountInput(rider, "Mount", false);
            AddMountInput(rider, "CallMount", false);
            AddMountInput(rider, "Dismount", true);

            MTools.SetDirty(this);
            MTools.SetDirty(rider);
        }

        /// <summary>True when the map already has a row with that name</summary>
        private static bool HasButton(MInputActionMap map, string name) => map.buttons.Exists(b => b != null && b.name == name);

        /// <summary>Adds a Mount Inputs pair to the Rider unless the input is already listed (pairs accept comma lists)</summary>
        private static void AddMountInput(MRider rider, string input, bool value)
        {
            rider.MountInputs ??= new IDPair<string, bool>[0];

            foreach (var pair in rider.MountInputs)
            {
                if (pair == null || string.IsNullOrEmpty(pair.ID)) continue;

                foreach (var n in pair.ID.Split(','))
                    if (n.Trim() == input) return;
            }

            rider.MountInputs = new List<IDPair<string, bool>>(rider.MountInputs) { new(input, value) }.ToArray();
        }
#endif
    }
}
