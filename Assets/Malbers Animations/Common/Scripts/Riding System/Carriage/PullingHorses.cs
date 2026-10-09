// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Controller;
using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace MalbersAnimations.HAP
{
    /// <summary>Turns the Animals that pull a Wagon, and keeps a paired second Animal locked to the first</summary>
    //MWC: NO [DefaultExecutionOrder] here on purpose. This script's .meta pins executionOrder to 7, and an
    //explicit Script Execution Order ALWAYS overrides the attribute, so adding one would be silently ignored.
    //The effective order is PullingHorses (7) -> WagonController (650) -> MAnimal (680), which is already what
    //CopyInput() needs: the input is mirrored before MAnimal reads it on the same frame.
    [AddComponentMenu("Malbers/Riding/Pulling Horses")]
    public class PullingHorses : MonoBehaviour
    {
        /// <summary>Animal that steers the Wagon</summary>
        public MAnimal MainAnimal;

        /// <summary>Optional second Animal yoked alongside the Main Animal</summary>
        public MAnimal SecondAnimal;

        /// <summary>Local point the Animals turn around. Usually the Wagon front axle</summary>
        public Vector3 RotationOffset;

        /// <summary>Index of the selected Inspector tab</summary>
        [HideInInspector] public int Editor_Tabs1;

        /// <summary>Stops the Animals turning further once the Wagon reaches its Max Turn Angle</summary>
        [Tooltip("Stops the Animals turning further once the Wagon Controller reports its Stear is at the Max Turn Angle")]
        public bool UseWagonTurnLimit = true;

        /// <summary>Draws the rotation pivot on the Scene View</summary>
        public bool debug = true;

        /// <summary>True while the Wagon Stear is turned to the Right. Written by the Wagon Controller</summary>
        public bool CurrentAngleSide { get; set; }

        //MWC: defaults to TRUE. It used to default to false, which was harmless only because nothing ever read
        //it. Now that the turn honours it, a rig with no Wagon Controller has to start out able to turn.
        /// <summary>False while the Wagon Stear sits at its Max Turn Angle. Written by the Wagon Controller</summary>
        public bool CanRotateInPlace { get; set; } = true;

        /// <summary>True when a second, different Animal is yoked to the Main Animal</summary>
        public bool HasSecondAnimal => SecondAnimal != null && SecondAnimal != MainAnimal;

        /// <summary>Position of the Second Animal in the Main Animal local space</summary>
        private Vector3 SecondAnimalOffset;

        /// <summary>Rotation of the Second Animal relative to the Main Animal</summary>
        private Quaternion SecondAnimalRotOffset = Quaternion.identity;

        /// <summary>Caches the pair offsets and takes movement control away from the Animals</summary>
        void Start()
        {
            if (!MainAnimal)
            {
                //MWC: it used to only return, leaving FixedUpdate and LateUpdate to throw a NullReference every frame
                Debug.LogWarning($"[{name}] [Pulling Horses] has no Main Animal. Disabling the component", this);
                enabled = false;
                return;
            }

            if (!SecondAnimal) SecondAnimal = MainAnimal;

            //MWC: set here as well as on the property. A non-serialized auto-property initializer is NOT reliable
            //across a Unity domain reload, and a false value here with UseWagonTurnLimit on means the Animals can
            //never turn at all. A Wagon Controller overwrites this every physics step once it is running.
            CanRotateInPlace = true;

            MainAnimal.DisableRotation = true;      //This component drives the turn instead
            MainAnimal.RootMotion = false;

            if (HasSecondAnimal)
            {
                SecondAnimal.DisableRotation = true;
                SecondAnimal.DisablePosition = true;
                SecondAnimal.RootMotion = false;

                //MWC: the rotation offset is stored too, so the follow is one exact expression instead of a
                //RotateAround that had to be kept in sync with the Main Animal's own rotation by hand
                SecondAnimalOffset = MainAnimal.transform.InverseTransformPoint(SecondAnimal.transform.position);
                SecondAnimalRotOffset = Quaternion.Inverse(MainAnimal.transform.rotation) * SecondAnimal.transform.rotation;
            }
        }

        /// <summary>Turns the Main Animal around the Wagon pivot</summary>
        void FixedUpdate()
        {
            var time = Time.fixedDeltaTime;

            if (time <= 0) return;

            //MWC: the Wagon Controller reports when its Stear has hit the Max Turn Angle. That flag was written
            //and never read, so the Animals could keep turning past the articulation limit and fight the Joint.
            if (UseWagonTurnLimit && !CanRotateInPlace) return;

            var RotationPoint = MainAnimal.transform.TransformPoint(RotationOffset);
            var turn = MainAnimal.HorizontalSmooth * time * MainAnimal.CurrentSpeedModifier.rotation;

            MainAnimal.transform.RotateAround(RotationPoint, MainAnimal.UpVector, turn);

            //MWC: the Second Animal is deliberately NOT rotated here any more. It is placed from the Main
            //Animal's rendered pose in LateUpdate instead. See PlaceSecondAnimal().
        }

        /// <summary>Mirrors the Main Animal input onto the Second Animal</summary>
        void Update()
        {
            //MWC: this ran in LateUpdate, a full frame after MAnimal had already consumed the input, so the
            //Second Animal's animation always played one frame behind its partner. Update runs ahead of
            //MAnimal (execution order 7 vs 680), so now both Animals read the same input on the same frame.
            if (HasSecondAnimal) CopyInput();
        }

        /// <summary>Copies the movement state of the Main Animal onto the Second Animal</summary>
        protected virtual void CopyInput()
        {
            SecondAnimal.RawInputAxis = MainAnimal.RawInputAxis;
            SecondAnimal.UseRawInput = MainAnimal.UseRawInput;
            SecondAnimal.Sprint = MainAnimal.sprint;
            SecondAnimal.Rotate_at_Direction = MainAnimal.Rotate_at_Direction;
            SecondAnimal.CurrentSpeedIndex = MainAnimal.CurrentSpeedIndex;
            SecondAnimal.MovementDetected = MainAnimal.MovementDetected;
            SecondAnimal.MovementAxis = MainAnimal.MovementAxis;
        }

        /// <summary>Places the Second Animal on the Main Animal rendered pose</summary>
        void LateUpdate()
        {
            if (HasSecondAnimal) PlaceSecondAnimal();
        }

        /// <summary>Locks the Second Animal to the stored offset from the Main Animal</summary>
        protected virtual void PlaceSecondAnimal()
        {
            //MWC: THE JITTER FIX. The Main Animal's Rigidbody is interpolated, so its VISIBLE pose is only final
            //in LateUpdate. Placing the Second Animal from the FixedUpdate (physics) pose made it step at the
            //50hz physics rate next to a smoothly interpolated partner. Reading the same pose the camera reads
            //keeps the pair welded together at any frame rate.
            //This also replaces the old Grounded / airborne split, whose airborne branch did
            //[position = MainAnimal.position + offset] where offset was ALREADY a world point from
            //TransformPoint, so the Second Animal was flung to roughly double the distance whenever it left
            //the ground. One path now, no teleport.
            SecondAnimal.transform.SetPositionAndRotation(
                MainAnimal.transform.TransformPoint(SecondAnimalOffset),
                MainAnimal.transform.rotation * SecondAnimalRotOffset);
        }

#if UNITY_EDITOR
        /// <summary>Draws the point the Animals turn around</summary>
        void OnDrawGizmos()
        {
            if (!debug) return;

            //MWC: was drawn from this.transform while FixedUpdate turns around MainAnimal.transform. Those only
            //agreed because the component happens to sit on the Main Animal. Also drew a wire and a solid
            //sphere at the same point.
            var pivot = MainAnimal ? MainAnimal.transform : transform;

            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(pivot.TransformPoint(RotationOffset), 0.05f);
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>Inspector for Pulling Horses. Validates the Animals that pull the Wagon</summary>
    [CanEditMultipleObjects, CustomEditor(typeof(PullingHorses))]
    public class PullingHorsesEd : Editor
    {
        /// <summary>One row of the Setup Check</summary>
        private class Check
        {
            public MessageType Level;
            public string Message;
            public string FixLabel;
            public System.Action Fix;
        }

        private bool showSetup = true;

        private SerializedProperty
            Editor_Tabs1, MainAnimal, SecondAnimal, RotationOffset, UseWagonTurnLimit, debug;

        /// <summary>Caches every serialized property drawn by this inspector</summary>
        protected virtual void OnEnable()
        {
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            MainAnimal = serializedObject.FindProperty("MainAnimal");
            SecondAnimal = serializedObject.FindProperty("SecondAnimal");
            RotationOffset = serializedObject.FindProperty("RotationOffset");
            UseWagonTurnLimit = serializedObject.FindProperty("UseWagonTurnLimit");
            debug = serializedObject.FindProperty("debug");
        }

        /// <summary>Draws the tabbed inspector followed by the Setup Check</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription
                ("Turns the Animals that pull a Wagon, and keeps a paired second Animal locked to the first.\nUse [Setup Check] to validate the pair");

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, new string[] { "Animals", "Turning", "Debug" });

                int Selection = Editor_Tabs1.intValue;

                if (Selection == 0) ShowAnimals();
                else if (Selection == 1) ShowTurning();
                else if (Selection == 2) ShowDebug();

                if (cc.changed) Undo.RecordObject(target, "Pulling Horses Changed");
            }

            if (targets.Length == 1)            //the check inspects one rig at a time
            {
                EditorGUILayout.Space();
                DrawSetupCheck((PullingHorses)target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws the Animals of the pair</summary>
        private void ShowAnimals()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MainAnimal.isExpanded = MalbersEditor.Foldout(MainAnimal.isExpanded, "Animals");

                if (MainAnimal.isExpanded)
                {
                    EditorGUILayout.PropertyField(MainAnimal);
                    EditorGUILayout.PropertyField(SecondAnimal);
                }
            }
        }

        /// <summary>Draws the turn pivot and the Wagon turn limit</summary>
        private void ShowTurning()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                RotationOffset.isExpanded = MalbersEditor.Foldout(RotationOffset.isExpanded, "Turning");

                if (RotationOffset.isExpanded)
                {
                    EditorGUILayout.PropertyField(RotationOffset);
                    EditorGUILayout.PropertyField(UseWagonTurnLimit);
                }
            }
        }

        /// <summary>Draws the gizmo options</summary>
        private void ShowDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Draw the rotation pivot", GUILayout.MinWidth(60));
                    MalbersEditor.DrawDebugIcon(debug);
                }
            }
        }

        /// <summary>Runs every validation and draws the results</summary>
        private void DrawSetupCheck(PullingHorses ph)
        {
            var checks = Validate(ph);

            int errors = 0, warnings = 0;

            foreach (var c in checks)
            {
                if (c.Level == MessageType.Error) errors++;
                else if (c.Level == MessageType.Warning) warnings++;
            }

            var title =
                errors > 0 ? $"Setup Check   [{errors} error(s), {warnings} warning(s)]" :
                warnings > 0 ? $"Setup Check   [{warnings} warning(s)]" :
                               "Setup Check   [all good]";

            showSetup = MalbersEditor.Foldout(showSetup, title);

            if (!showSetup) return;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;

                if (checks.Count == 0)
                {
                    EditorGUILayout.HelpBox("The pair checks out.", MessageType.Info);
                }

                foreach (var c in checks)
                {
                    EditorGUILayout.HelpBox(c.Message, c.Level);

                    if (c.Fix == null) continue;

                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(c.FixLabel, GUILayout.Width(190))) c.Fix();
                    }
                }

                var fixable = checks.FindAll(x => x.Fix != null);

                if (fixable.Count > 1)
                {
                    EditorGUILayout.Space();

                    if (GUILayout.Button($"Fix All ({fixable.Count})"))
                        foreach (var c in fixable) c.Fix();
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>Builds the list of problems found on the pair</summary>
        private List<Check> Validate(PullingHorses ph)
        {
            var list = new List<Check>();

            if (!ph.MainAnimal)
            {
                list.Add(new Check
                {
                    Level = MessageType.Error,
                    Message = "[Main Animal] is empty. Set it to the Animal that steers the Wagon.\n" +
                              "The component disables itself on Start without it."
                });
                return list;                    //every other check depends on it
            }

            ValidateMain(ph, list);
            ValidateSecond(ph, list);
            ValidatePivot(ph, list);
            ValidateWagon(ph, list);

            return list;
        }

        /// <summary>Checks the Animal that steers</summary>
        private void ValidateMain(PullingHorses ph, List<Check> list)
        {
            var rb = ph.MainAnimal.GetComponent<Rigidbody>();

            if (!rb)
            {
                list.Add(new Check { Level = MessageType.Error, Message = $"[{ph.MainAnimal.name}] has no Rigidbody." });
                return;
            }

            if (rb.isKinematic)
            {
                var body = rb;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"[{ph.MainAnimal.name}] has a Kinematic Rigidbody. MAnimal moves the Main Animal by writing " +
                              "linearVelocity, which a kinematic body ignores, so it cannot pull the Wagon.",
                    FixLabel = "Make it Dynamic",
                    Fix = () => { Record(body, "Make Main Animal Dynamic"); body.isKinematic = false; }
                });
            }

            var anim = ph.MainAnimal.GetComponent<Animator>();

            if (anim && anim.updateMode != AnimatorUpdateMode.Fixed)
            {
                var a = anim;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"[{ph.MainAnimal.name}] Animator Update Mode is [{anim.updateMode}]. The Wagon is simulated by " +
                              "physics, so the Animal has to run in [Fixed] to stay in step with it.",
                    FixLabel = "Set to Fixed",
                    Fix = () => { Record(a, "Set Animator Update Mode"); a.updateMode = AnimatorUpdateMode.Fixed; }
                });
            }
        }

        /// <summary>Checks the optional second Animal of the pair</summary>
        private void ValidateSecond(PullingHorses ph, List<Check> list)
        {
            if (!ph.SecondAnimal)
            {
                list.Add(new Check
                {
                    Level = MessageType.Info,
                    Message = "[Second Animal] is empty, so this is a single Animal rig. It is set to the Main Animal on Start."
                });
                return;
            }

            if (!ph.HasSecondAnimal) return;    //Second is the same as Main, a valid single Animal setup

            var srb = ph.SecondAnimal.GetComponent<Rigidbody>();

            if (!srb)
            {
                list.Add(new Check { Level = MessageType.Error, Message = $"[{ph.SecondAnimal.name}] has no Rigidbody." });
                return;
            }

            //the Second Animal is placed by transform every LateUpdate, so physics must not also drive it
            if (!srb.isKinematic)
            {
                var body = srb;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"[{ph.SecondAnimal.name}] has a Dynamic Rigidbody. The Second Animal is positioned by transform " +
                              "every LateUpdate, and MAnimal turns Interpolation ON for dynamic Fixed rigs, which fights those " +
                              "transform writes and makes it rubber-band.\nA paired Animal should be Kinematic.",
                    FixLabel = "Make it Kinematic",
                    Fix = () => { Record(body, "Make Second Animal Kinematic"); body.isKinematic = true; }
                });
            }

            var gap = Vector3.Distance(ph.MainAnimal.transform.position, ph.SecondAnimal.transform.position);

            if (gap < 0.1f)
            {
                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"The two Animals are {gap:F2}m apart. The side by side offset is captured on Start from the " +
                              "positions you author here, so place them where they should stand as a pair before entering Play."
                });
            }
        }

        /// <summary>Checks the point the Animals turn around</summary>
        private void ValidatePivot(PullingHorses ph, List<Check> list)
        {
            if (ph.RotationOffset != Vector3.zero) return;

            var check = new Check
            {
                Level = MessageType.Warning,
                Message = "[Rotation Offset] is zero, so the Animals turn around their own root instead of the Wagon's front " +
                          "axle. The Wagon will swing wide and fight the hitch on every turn.\n" +
                          "Set it to the axle position, in the Main Animal's local space (usually a negative Z)."
            };

            //offer the Wagon's steer transform when there is one to measure against
            var wagon = FindWagon(ph);

            if (wagon && wagon.StearCollider)
            {
                var animal = ph.MainAnimal;
                var stear = wagon.StearCollider;
                var target = ph;
                var value = animal.transform.InverseTransformPoint(stear.position);
                value.y = 0;                    //the turn is around the vertical axis, height is irrelevant

                check.FixLabel = $"Set from front axle (z {value.z:F2})";
                check.Fix = () => { Record(target, "Set Rotation Offset"); target.RotationOffset = value; };
            }

            list.Add(check);
        }

        /// <summary>Checks whether a Wagon Controller actually drives this component</summary>
        private void ValidateWagon(PullingHorses ph, List<Check> list)
        {
            if (!ph.UseWagonTurnLimit) return;

            if (FindWagon(ph) == null)
            {
                list.Add(new Check
                {
                    Level = MessageType.Info,
                    Message = "[Use Wagon Turn Limit] is on but no Wagon Controller in this hierarchy points at these Animals, " +
                              "so nothing writes [Can Rotate In Place] and the limit has no effect. Turning still works normally."
                });
            }
        }

        /// <summary>Finds the Wagon Controller whose Animal resolves back to this component</summary>
        private WagonController FindWagon(PullingHorses ph)
        {
            foreach (var wc in ph.transform.root.GetComponentsInChildren<WagonController>(true))
            {
                if (!wc.HorseRigidBody) continue;
                if (wc.HorseRigidBody.GetComponentInParent<PullingHorses>() == ph) return wc;
            }
            return null;
        }

        /// <summary>Registers an Undo step and flags the object dirty, prefab instances included</summary>
        private static void Record(Object o, string label)
        {
            Undo.RecordObject(o, label);
            EditorUtility.SetDirty(o);

            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }
    }
#endif
}
