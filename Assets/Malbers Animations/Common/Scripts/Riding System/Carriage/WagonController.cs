// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.HAP
{
    /// <summary>This Script controls the behavior of the wagons </summary>
    [AddComponentMenu("Malbers/Riding/Wagon Controller")]
    public class WagonController : MonoBehaviour
    {
        /// <summary>Rigidbody of the Animal that pulls the Wagon</summary>
        public Rigidbody HorseRigidBody;

        /// <summary>Joint that hitches the Wagon to the Animal that pulls it</summary>
        public ConfigurableJoint HorseJoint;

        /// <summary>How much the Animal can turn Regarding the Wagons Front Wheels</summary>
        public float MaxTurnAngle = 45f;

        /// <summary>Pulling Horses component found on, or above, the Horse Rigidbody</summary>
        protected PullingHorses DHorses;

        /// <summary>Transform used to override the Center of Mass of the Wagon</summary>
        public Transform CenterOfMass;

        /// <summary>Reference Transform for the direction the Wagon Body is facing</summary>
        public Transform BodyCollider;

        /// <summary>Reference Transform for the direction the Wagon Stear is facing</summary>
        public Transform StearCollider;

        /// <summary>All the Wheel Colliders of the Wagon</summary>
        public WheelCollider[] WheelColliders;

        /// <summary>Mesh of the Wagon Body. It gets parented to the Body Collider on Start</summary>
        public Transform Body;

        /// <summary>Mesh of the Wagon Stear. It gets parented to the Stear Collider on Start</summary>
        public Transform StearMesh;

        /// <summary>Meshes driven by the Wheel Colliders. Must match the Wheel Colliders order</summary>
        public Transform[] WheelMeshes;

        /// <summary>Index of the selected Inspector tab</summary>
        [HideInInspector] public int Editor_Tabs1;

        /// <summary>Solver Position Iterations applied to every Rigidbody of the pulling chain</summary>
        [Tooltip("Solver Position Iterations for the pulling chain. The locked Joints cannot converge on the Project default of 1")]
        [Range(1, 40)]
        public int SolverIterations = 12;

        /// <summary>Solver Velocity Iterations applied to every Rigidbody of the pulling chain</summary>
        [Tooltip("Solver Velocity Iterations for the pulling chain")]
        [Range(1, 20)]
        public int SolverVelocityIterations = 4;

        /// <summary>Parking brake torque applied to every wheel once the Wagon has come to rest</summary>
        [Tooltip("Parking brake torque (Nm), applied only once the Wagon has actually stopped. It has to hold the weight of the Wagon on a slope")]
        public float BrakeTorque = 500f;

        /// <summary>Speed under which the Wagon counts as stopped and the parking brake engages</summary>
        [Tooltip("Speed (m/s) above which the Wagon counts as moving. It counts as stopped again under half of it, so a rebound does not flicker the brake, the sounds or the On Moving Changed event. Above it the Wagon rolls freely so it can coast instead of locking its wheels")]
        public float BrakeSpeed = 0.3f;

        /// <summary>How many times heavier the hitch Joint treats the Animal compared to its real mass</summary>
        [Tooltip("How many times heavier the hitch Joint treats the Animal. MAnimal assigns the Animal velocity directly every physics step, so the Animal is effectively kinematic. Telling the solver that lets the Wagon be dragged along instead of stalling the Animal. 1 = off. Applied as connectedMassScale = 1/value, because that property scales INVERSE mass")]
        [Range(1f, 100f)]
        public float HorseMassScale = 10f;

        /// <summary>Ignores the collisions between the Wagon colliders and the Animals pulling it</summary>
        [Tooltip("Ignores the collisions between the Wagon and the Animals pulling it. They are not directly jointed, so [Enable Collision] on the Joints does not cover that pair")]
        public bool IgnorePullingAnimals = true;

        /// <summary>Plays the wheel rolling and the wood creak loops</summary>
        [Tooltip("Plays the wheel rolling and the wood creak loops while the Wagon moves")]
        public bool PlaySounds = true;

        /// <summary>Rolling loop per ground surface (entry Sound). The asset Default Sound is the fallback for unmatched ground</summary>
        [Tooltip("Rolling loop per ground surface. Each entry [Sound] is the loop for that surface, the asset [Default Sound] covers unmatched ground. An Audio Random Container used here must be authored with Trigger [Automatic] and Loop [Infinite]")]
        public MStepSurfaces WheelSurfaces;

        /// <summary>Rolling loop used when no Wheel Surfaces asset is assigned or it has no Default Sound</summary>
        [Tooltip("Rolling loop used when no [Wheel Surfaces] asset is assigned, or it has no Default Sound")]
        public AudioClipReference RollingSound;

        /// <summary>Wood creak and rattle loop</summary>
        [Tooltip("Wood creak and rattle loop. Its volume follows the speed and spikes on bumps")]
        public AudioClipReference CreakSound;

        /// <summary>Speed (m/s) that maps to 1 on every volume and pitch curve</summary>
        [Tooltip("Speed (m/s) that maps to 1 on every volume and pitch curve. Set it to the top speed of the Animal pulling the Wagon")]
        public float MaxSpeed = 13f;

        /// <summary>Rolling volume over normalised speed</summary>
        [Tooltip("Rolling volume over the normalised speed (0 = stopped, 1 = Max Speed)")]
        public AnimationCurve RollingVolume = AnimationCurve.EaseInOut(0, 0.3f, 1, 1);

        /// <summary>Rolling pitch over normalised speed</summary>
        [Tooltip("Rolling pitch over the normalised speed (0 = stopped, 1 = Max Speed)")]
        public AnimationCurve RollingPitch = AnimationCurve.Linear(0, 0.85f, 1, 1.15f);

        /// <summary>Creak volume over normalised speed</summary>
        [Tooltip("Creak volume over the normalised speed (0 = stopped, 1 = Max Speed)")]
        public AnimationCurve CreakVolume = AnimationCurve.EaseInOut(0, 0.2f, 1, 0.8f);

        /// <summary>Creak pitch over normalised speed</summary>
        [Tooltip("Creak pitch over the normalised speed (0 = stopped, 1 = Max Speed)")]
        public AnimationCurve CreakPitch = AnimationCurve.Linear(0, 0.95f, 1, 1.05f);

        /// <summary>Extra creak volume per rad/s of body pitch and roll, so bumps make the Wagon rattle</summary>
        [Tooltip("Extra creak volume per rad/s of body pitch and roll, so bumps make the Wagon rattle. 0 = off")]
        public float BumpSensitivity = 0.5f;

        /// <summary>How fast the bump boost fades, per second</summary>
        [Tooltip("How fast the bump boost fades, per second")]
        public float BumpDecay = 4f;

        /// <summary>Seconds to fade the loops in and out, and to crossfade between surfaces</summary>
        [Tooltip("Seconds to fade the loops in and out, and to crossfade between surfaces")]
        public float FadeTime = 0.35f;

        /// <summary>2D to 3D blend of the Wagon sounds</summary>
        [Tooltip("2D to 3D blend of the Wagon sounds")]
        [Range(0, 1)] public float SpatialBlend = 1f;

        /// <summary>Distance under which the Wagon sounds play at full volume</summary>
        [Tooltip("Distance under which the Wagon sounds play at full volume")]
        public float MinDistance = 2f;

        /// <summary>Distance beyond which the Wagon sounds are inaudible</summary>
        [Tooltip("Distance beyond which the Wagon sounds are inaudible")]
        public float MaxDistance = 30f;

        /// <summary>Optional Mixer Group for the Wagon sounds</summary>
        [Tooltip("Optional Mixer Group for the Wagon sounds")]
        public AudioMixerGroup MixerGroup;

        /// <summary>Physics steps between ground surface checks. Raise it on mobile</summary>
        [Tooltip("Physics steps between ground surface checks under the wheels. Raise it on mobile")]
        [Range(1, 30)] public int SurfaceCheckInterval = 5;

        /// <summary>Invoked when the Wagon starts or stops moving (crosses Brake Speed)</summary>
        public BoolEvent OnMovingChanged = new();

        /// <summary>Speed of the Wagon in m/s, updated every physics step</summary>
        public float Speed { get; private set; }

        /// <summary>True while the Wagon moves faster than Brake Speed</summary>
        public bool IsMoving { get; private set; }

        /// <summary>Surface entry the wheels are rolling on. Null on unmatched ground or when Wheel Surfaces is empty</summary>
        public StepSurface CurrentSurface => currentSurface;

        /// <summary>Ground collider under the first grounded wheel at the last surface check. Null before the first check</summary>
        public Collider CurrentGround { get; private set; }

        /// <summary>Rigidbody of the Wagon</summary>
        protected Rigidbody RB;

        /// <summary>Signed angle between the Wagon Body and the Wagon Stear</summary>
        protected float currentAngle;

        /// <summary>Authored rotation of every Wheel Mesh, relative to its own Wheel Collider</summary>
        protected Quaternion[] WheelMeshOffset;

        /// <summary>Child GameObject that holds the Wagon Audio Sources</summary>
        protected Transform soundsRoot;

        /// <summary>First rolling Audio Source (the pair crossfades on surface changes)</summary>
        protected AudioSource rollA;

        /// <summary>Second rolling Audio Source (the pair crossfades on surface changes)</summary>
        protected AudioSource rollB;

        /// <summary>Creak Audio Source</summary>
        protected AudioSource creak;

        /// <summary>Rolling source currently fading in or playing</summary>
        protected AudioSource rollActive;

        /// <summary>Rolling source currently fading out. Null when no crossfade is running</summary>
        protected AudioSource rollFading;

        /// <summary>Rolling resource playing on the active source</summary>
        protected AudioResource currentRolling;

        /// <summary>Rolling resource used on unmatched ground, resolved once on Start</summary>
        protected AudioResource fallbackRolling;

        /// <summary>Creak resource, resolved once on Start</summary>
        protected AudioResource creakResource;

        /// <summary>Smoothed rolling volume before the crossfade weight</summary>
        protected float rollVol;

        /// <summary>Smoothed creak volume before the bump boost</summary>
        protected float creakVol;

        /// <summary>Crossfade weight of the active rolling source (0 → 1)</summary>
        protected float crossfade = 1f;

        /// <summary>Peak-held bump boost added to the creak volume</summary>
        protected float bump;

        /// <summary>Physics steps since the last ground surface check</summary>
        protected int surfaceStep;

        /// <summary>Surface entry the wheels are rolling on</summary>
        protected StepSurface currentSurface;

        /// <summary>Ground collider classification cache for the surface lookup</summary>
        protected Dictionary<Collider, StepGroundInfo> groundCache;

        /// <summary>Draws the Wagon colliders and wheels on the Scene View</summary>
        public bool debug;

        /// <summary>Color used by the debug gizmos</summary>
        public Color DebugColor;

        // Use this for initialization
        void Start()
        {
            RB = GetComponent<Rigidbody>();

            //MWC: RB was used unguarded by GetStearAngle() and by the Center of Mass logic
            if (!RB) Debug.LogWarning($"[{name}] The Wagon Controller requires a Rigidbody", this);

            if (HorseRigidBody)
            {
                //MWC: the Rigidbody and the Pulling Horses component are not always on the same GameObject. The Double
                //Wagon rig keeps Pulling Horses on the parent, so GetComponent() returned null there and the Stear
                //angle, the wheel brakes and the turn limit were all silently disabled. Search upwards instead.
                DHorses = HorseRigidBody.GetComponentInParent<PullingHorses>();

                //MWC: fail loudly instead of running with half of the component disabled
                if (!DHorses)
                    Debug.LogWarning($"[{name}] No [Pulling Horses] found on [{HorseRigidBody.name}] or its parents. The Wagon steering is disabled", this);
            }

            if (Body && BodyCollider) Body.parent = BodyCollider;               //Parent Body to the Collider
            if (StearMesh && StearCollider) StearMesh.parent = StearCollider;   //Parent Body to the Collider

            if (HorseRigidBody && HorseJoint)
            {
                HorseJoint.connectedBody = HorseRigidBody;

                //MWC: MAnimal drives the Animal by writing RB.linearVelocity every physics step, so in translation
                //the Animal behaves as if it were kinematic. The Joint did not know that: it saw a 300kg dynamic
                //body locked on all three linear axes to a 200kg Wagon and split every correction between the two
                //by mass, dragging the Animal back toward the Wagon on every step. MAnimal then re-assigned the
                //velocity, the Joint corrected again, and the pair fought each other at tens of kN. At speed the
                //momentum carried through it, at walking pace it did not and the Animal ran on the spot.
                //connectedMassScale scales the INVERSE mass, so it has to be the RECIPROCAL: a value below 1
                //makes the Animal effectively heavier and the constraint gets satisfied by moving the WAGON.
                //(Passing the multiplier directly makes the Animal that much LIGHTER, which pins it in place.)
                HorseJoint.connectedMassScale = 1f / Mathf.Max(HorseMassScale, 0.0001f);
            }

            CacheWheelMeshOffsets();    //MWC: the Wheel Meshes have their own authored orientation. Keep it
            UpdateCenterOfMass();       //MWC: it was reassigned every FixedUpdate, and in the wrong space
            ApplyStabilitySettings();   //MWC: the locked Joint chain needs more solver iterations than the Project default
            IgnoreAnimalCollisions();   //MWC: the Wagon Body and the Animals share the Animal layer and DO collide
            SetupSounds();
        }

        /// <summary>Ignores the collisions between the Wagon colliders and the Animals pulling it</summary>
        protected virtual void IgnoreAnimalCollisions()
        {
            //MWC: [Enable Collision] on a Joint only covers the two bodies that Joint connects. The Animal is
            //jointed to the Wagon root, never to the Wagon Body, so the Animal capsules and the Body boxes
            //(both on the Animal layer, which collides with itself) were free to jam against each other.
            if (!IgnorePullingAnimals || !DHorses) return;

            var wagon = GetComponentsInChildren<Collider>(true);

            IgnoreAnimal(DHorses.MainAnimal, wagon);
            if (DHorses.HasSecondAnimal) IgnoreAnimal(DHorses.SecondAnimal, wagon);
        }

        /// <summary>Ignores the collisions between one Animal and every solid collider of the Wagon</summary>
        protected virtual void IgnoreAnimal(MalbersAnimations.Controller.MAnimal animal, Collider[] wagon)
        {
            if (!animal) return;

            foreach (var ac in animal.GetComponentsInChildren<Collider>(true))
            {
                //MWC: triggers are skipped so Mount zones and damage volumes keep working, and Wheel Colliders
                //are skipped because Physics.IgnoreCollision does not support them
                if (!ac || ac.isTrigger || ac is WheelCollider) continue;

                foreach (var wc in wagon)
                {
                    if (!wc || wc.isTrigger || wc is WheelCollider) continue;

                    Physics.IgnoreCollision(ac, wc, true);
                }
            }
        }

        /// <summary>Stores the rotation of every Wheel Mesh relative to its Wheel Collider</summary>
        protected virtual void CacheWheelMeshOffsets()
        {
            var count = WheelMeshes != null ? WheelMeshes.Length : 0;
            WheelMeshOffset = new Quaternion[count];

            for (int i = 0; i < count; i++)
            {
                //MWC: relative to the Wheel Collider frame, so it holds no matter how the Meshes are parented
                WheelMeshOffset[i] =
                    (WheelMeshes[i] && WheelColliders != null && i < WheelColliders.Length && WheelColliders[i]) ?
                    Quaternion.Inverse(WheelColliders[i].transform.rotation) * WheelMeshes[i].rotation :
                    Quaternion.identity;
            }
        }

        /// <summary>Applies the Center of Mass Transform to the Wagon Rigidbody</summary>
        public virtual void UpdateCenterOfMass()
        {
            //MWC: localPosition was only correct when the Center of Mass was a direct, unscaled child of the Wagon
            if (RB && CenterOfMass) RB.centerOfMass = RB.transform.InverseTransformPoint(CenterOfMass.position);
        }

        /// <summary>Applies the physics settings that the jointed pulling chain needs to stay stable</summary>
        public virtual void ApplyStabilitySettings()
        {
            //MWC: Horse -> Wagon -> Body are chained by Joints with every linear axis Locked. That chain cannot
            //converge on the Project's global solver iteration count, so raise it per body instead of globally.
            //Interpolation is deliberately NOT touched here: the project rule is a Fixed Update camera Brain with
            //Interpolation [None], so the camera and every body are sampled on the same physics step.
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) ApplyStability(rb);

            if (HorseRigidBody) ApplyStability(HorseRigidBody);
        }

        /// <summary>Raises the solver iterations of one Rigidbody of the pulling chain</summary>
        protected virtual void ApplyStability(Rigidbody rb)
        {
            if (!rb) return;

            rb.solverIterations = SolverIterations;
            rb.solverVelocityIterations = SolverVelocityIterations;
        }

        /// <summary>Updates the Meshes of the Wheel Colliders</summary>
        void UpdateWheelMeshes()
        {
            if (WheelColliders == null) return;

            //MWC: PARKING brake. This used to engage the instant the input stopped, which slammed the full torque
            //on while the Wagon still had all its momentum and locked the four wheels dead instead of letting it
            //roll to a halt. A Wagon has to coast: no brake at all while it is still moving, full torque only once
            //it has actually come to rest, which is when the brake is genuinely needed (holding it on a slope).
            //MainAnimal was also dereferenced without a null check.
            var noInput = !DHorses || !DHorses.MainAnimal || !DHorses.MainAnimal.MovementDetected;
            var stop = noInput && !IsMoving;

            for (int i = 0; i < WheelColliders.Length; i++)
            {
                if (!WheelColliders[i]) continue;

                if (DHorses) StopWheels(stop, i);

                //MWC: WheelMeshes was indexed with the WheelColliders index without any bounds or null check
                if (WheelMeshes == null || i >= WheelMeshes.Length || !WheelMeshes[i]) continue;

                WheelColliders[i].GetWorldPose(out var pos, out var rot);

                //MWC: GetWorldPose returns the COLLIDER pose. Assigning it raw threw away the orientation the Mesh
                //was authored with (the wheels sit at -90 on X) and rendered every wheel on the wrong axis.
                WheelMeshes[i].SetPositionAndRotation(pos, rot * WheelMeshOffset[i]);
            }
        }

        /// <summary>Applies the brake torque to one Wheel Collider</summary>
        void StopWheels(bool stop, int Index)
        {
            //MWC: was hardcoded to 0.9 Nm. On 0.56m wheels that is roughly 1.6 N of grip against a 200kg wagon,
            //so the Wagon never actually stopped and kept rolling into the Animal pulling it.
            WheelColliders[Index].brakeTorque = stop ? BrakeTorque : 0;
        }

        /// <summary>Drives the Wheel Meshes, the Stear angle and the sound state on the physics step</summary>
        void FixedUpdate()
        {
            //MWC: the Center of Mass is applied once on Start by UpdateCenterOfMass() instead of every physics step
            UpdateSpeed();
            UpdateWheelMeshes();
            GetStearAngle();

            if (PlaySounds && rollA)
            {
                UpdateBump();
                UpdateSurface();
            }
        }

        /// <summary>Fades and modulates the loops every rendered frame</summary>
        void Update()
        {
            if (PlaySounds && rollA) UpdateSounds();
        }

        /// <summary>Silences the loops and releases the ground cache</summary>
        protected virtual void OnDisable()
        {
            if (rollA) rollA.Stop();
            if (rollB) rollB.Stop();
            if (creak) creak.Stop();

            rollFading = null;
            rollVol = creakVol = bump = 0;
            crossfade = 1f;
            groundCache?.Clear();
        }

        /// <summary>Reads the Wagon speed and raises the moving event when it crosses Brake Speed</summary>
        protected virtual void UpdateSpeed()
        {
            Speed = RB ? RB.linearVelocity.magnitude : 0;

            //Hysteresis: starts moving above Brake Speed, counts as stopped only under half of it, so the
            //rebound of a hard stop does not flicker the event and the loops
            var moving = IsMoving ? Speed > BrakeSpeed * 0.5f : Speed > BrakeSpeed;
            if (moving == IsMoving) return;

            IsMoving = moving;
            OnMovingChanged.Invoke(moving);
        }

        #region Sounds
        /// <summary>Creates the Audio Sources and resolves the loop resources once</summary>
        protected virtual void SetupSounds()
        {
            if (!PlaySounds || !RB) return;

            var root = new GameObject("Wagon Sounds");
            soundsRoot = root.transform;
            soundsRoot.SetParent(transform, false);
            soundsRoot.position = CenterOfMass ? CenterOfMass.position : transform.position;

            rollA = CreateSource("Rolling A");
            rollB = CreateSource("Rolling B");
            creak = CreateSource("Creak");

            //Resolve the resources ONCE. AudioClipReference.Value picks a random item when it is backed by a List Var
            fallbackRolling =
                WheelSurfaces && !WheelSurfaces.DefaultSound.NullOrEmpty() ? WheelSurfaces.DefaultSound.Value :
                !RollingSound.NullOrEmpty() ? RollingSound.Value : null;

            creakResource = !CreakSound.NullOrEmpty() ? CreakSound.Value : null;
            creak.resource = creakResource;

            groundCache = new Dictionary<Collider, StepGroundInfo>();

            CrossfadeRolling(fallbackRolling);
        }

        /// <summary>Creates one looping 3D Audio Source under the Wagon Sounds child</summary>
        protected virtual AudioSource CreateSource(string sourceName)
        {
            var source = new GameObject(sourceName).AddComponent<AudioSource>();
            source.transform.SetParent(soundsRoot, false);
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0;
            source.spatialBlend = SpatialBlend;
            source.minDistance = MinDistance;
            source.maxDistance = MaxDistance;
            source.outputAudioMixerGroup = MixerGroup;
            return source;
        }

        /// <summary>Peak-holds the body pitch and roll rate so bumps add creak</summary>
        protected virtual void UpdateBump()
        {
            if (BumpSensitivity <= 0) { bump = 0; return; }

            var w = RB.angularVelocity;
            w.y = 0; //yaw from steering is not a bump

            bump = Mathf.Max(bump - BumpDecay * Time.fixedDeltaTime, Mathf.Clamp01(w.magnitude * BumpSensitivity));
        }

        /// <summary>Resolves the ground under the wheels every few steps and swaps the rolling loop on a surface change</summary>
        protected virtual void UpdateSurface()
        {
            if (!IsMoving || WheelSurfaces == null || WheelColliders == null) return;
            if (++surfaceStep < SurfaceCheckInterval) return;

            surfaceStep = 0;

            for (int i = 0; i < WheelColliders.Length; i++)
            {
                var wheel = WheelColliders[i];
                if (!wheel || !wheel.isGrounded || !wheel.GetGroundHit(out var hit) || !hit.collider) continue;

                CurrentGround = hit.collider;

                var entry = SurfaceResolver.Resolve(WheelSurfaces, hit.collider, hit.point, groundCache);
                var next = entry != null && entry.sound ? entry.sound : fallbackRolling;

                if (next != currentRolling)
                {
                    currentSurface = entry;
                    CrossfadeRolling(next);
                }
                return; //the first grounded wheel decides
            }
        }

        /// <summary>Starts the next rolling loop on the idle source and fades the current one out</summary>
        protected virtual void CrossfadeRolling(AudioResource next)
        {
            currentRolling = next;

            if (rollActive)
            {
                //A crossfade already in flight loses its outgoing source; the new outgoing one takes over from its current volume
                if (rollFading) rollFading.Stop();
                rollFading = rollActive;
                rollActive = rollActive == rollA ? rollB : rollA;
            }
            else
            {
                rollActive = rollA;
            }

            crossfade = 0;
            rollActive.Stop();
            rollActive.resource = next;
            rollActive.volume = 0;

            if (next == null) return;

            if (IsMoving) rollActive.Play();

            //Two Wagons in the same scene must not phase-lock on the same loop
            if (next is AudioClip clip && clip.length > 0) rollActive.time = Random.Range(0f, clip.length * 0.9f);
        }

        /// <summary>Fades the loops in and out and follows the speed curves</summary>
        protected virtual void UpdateSounds()
        {
            float step = FadeTime > 0 ? Time.deltaTime / FadeTime : 1f;
            float n = MaxSpeed > 0 ? Mathf.Clamp01(Speed / MaxSpeed) : 0;

            rollVol = Mathf.MoveTowards(rollVol, IsMoving && currentRolling ? RollingVolume.Evaluate(n) : 0, step);
            crossfade = Mathf.MoveTowards(crossfade, 1, step);

            float rollPitch = RollingPitch.Evaluate(n);

            ApplyLoop(rollActive, rollVol * crossfade, rollPitch);

            if (rollFading)
            {
                ApplyLoop(rollFading, rollVol * (1 - crossfade), rollPitch);

                if (crossfade >= 1)
                {
                    rollFading.Stop();
                    rollFading = null;
                }
            }

            creakVol = Mathf.MoveTowards(creakVol, IsMoving && creakResource ? CreakVolume.Evaluate(n) : 0, step);
            ApplyLoop(creak, Mathf.Clamp01(creakVol + (IsMoving ? bump : 0)), CreakPitch.Evaluate(n));
        }

        /// <summary>Applies volume and pitch, and starts or stops the source so a silent loop holds no voice</summary>
        protected static void ApplyLoop(AudioSource source, float volume, float pitch)
        {
            if (!source || !source.resource) return;

            source.volume = volume;
            source.pitch = pitch;

            if (volume > 0 && !source.isPlaying) source.Play();
            else if (volume <= 0 && source.isPlaying) source.Stop();
        }
        #endregion

        /// <summary>Measures the angle between the Wagon Body and the Wagon Stear</summary>
        protected virtual void GetStearAngle()
        {
            if (!DHorses) return;
            if (!StearCollider || !BodyCollider) return;

            Vector3 BodyDirection = BodyCollider.forward;
            Vector3 StearDirection = StearCollider.forward;

            BodyDirection.y = StearDirection.y = 0;

            //MWC: the side was read as Dot(BodyDirection, StearCollider.right), which is wrong twice over.
            //It dotted a FLATTENED vector against an UNFLATTENED axis, so the sign flipped on any slope or
            //roll, and it measured the Body in the Stear's frame rather than the other way round, which
            //INVERTED the result: a Stear turned right reported CurrentAngleSide = false.
            //SignedAngle around world up is flattened on both sides and positive to the right.
            currentAngle = Vector3.SignedAngle(BodyDirection, StearDirection, Vector3.up);

            bool isRight = currentAngle > 0;

            DHorses.CurrentAngleSide = isRight;

            if (DHorses.MainAnimal)                                                                 //MWC: was dereferenced unguarded
            {
                //MWC: the x comparisons are swapped relative to the original. They had been written against the
                //INVERTED angle above, so the two bugs cancelled out and the limit read correctly. With the sign
                //corrected the pairing has to flip too, otherwise the limit blocks the direction that unwinds the
                //Stear and locks the Wagon at full lock.
                //MovementAxis.x > 0 is a turn to the right (same input that drives the positive RotateAround in
                //PullingHorses), so a Stear already at +MaxTurnAngle must block further RIGHT input.
                var turningRight = DHorses.MainAnimal.MovementAxis.x >= 0;

                DHorses.CanRotateInPlace =
                    !((currentAngle >= MaxTurnAngle && turningRight) ||
                      (currentAngle <= -MaxTurnAngle && !turningRight));
            }

            //MWC: removed [RB.linearVelocity = DHorses.PullingDirection]. PullingDirection is never assigned
            //anywhere, so that line zeroed the Wagon velocity on every physics step below 0.01 m/s instead of
            //giving it the head start the comment claimed. It stayed invisible only because DHorses was null on
            //the Double Wagon rig. Now that DHorses resolves there, it would have actively braked that Wagon.
        }


#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!debug) return;

            BoxCollider[] allColliders = GetComponentsInChildren<BoxCollider>();

            foreach (BoxCollider box in allColliders)
            {
                if (box.GetComponent<MonoBehaviour>())
                {
                    continue;
                }
                var sizeX = transform.lossyScale.x * box.size.x;
                var sizeY = transform.lossyScale.y * box.size.y;
                var sizeZ = transform.lossyScale.z * box.size.z;
                Matrix4x4 rotationMatrix = Matrix4x4.TRS(box.bounds.center, box.transform.rotation, new Vector3(sizeX, sizeY, sizeZ));

                Gizmos.matrix = rotationMatrix;
                Gizmos.color = DebugColor;
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
                Gizmos.color = new Color(DebugColor.r, DebugColor.g, DebugColor.b, 1);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }

            //MWC: the box loop above leaves its last rotationMatrix on Gizmos, which offset everything drawn after it
            Gizmos.matrix = Matrix4x4.identity;

            if (WheelColliders == null) return;

            foreach (var wheel in WheelColliders)
            {
                if (!wheel) continue;

                wheel.GetWorldPose(out var pos, out _);

                //MWC: this drew the wheels with UnityEditor.Handles.DrawSolidDisc / DrawWireDisc. Handles is an
                //editor GUI API and throws a NullReference when it is called from OnDrawGizmos during the gizmo
                //pass of a Scriptable Render Pipeline (this project is URP), which spammed the console on every
                //Scene View repaint while [debug] was on. Gizmos line segments draw the same circle safely.
                Gizmos.color = DebugColor;
                DrawGizmoCircle(pos, wheel.transform.right, wheel.radius);
            }
        }

        /// <summary>Draws a wire circle with Gizmos lines, perpendicular to the given axis</summary>
        private static void DrawGizmoCircle(Vector3 center, Vector3 axis, float radius)
        {
            const int segments = 24;

            var rotation = Quaternion.LookRotation(axis);
            var prev = center + rotation * new Vector3(radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var next = center + rotation * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);

                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>Inspector for the Wagon Controller. Validates the physics of the whole pulling rig</summary>
    [CanEditMultipleObjects, CustomEditor(typeof(WagonController))]
    public class WagonControllerEd : Editor
    {
        /// <summary>One row of the Setup Check</summary>
        private class Check
        {
            public MessageType Level;
            public string Message;
            public string FixLabel;
            public System.Action Fix;
        }

        /// <summary>Gravity used to size the suspension springs</summary>
        private const float Gravity = 9.81f;

        private bool showSetup = true;

        private SerializedProperty
            Editor_Tabs1, HorseRigidBody, HorseJoint, MaxTurnAngle, CenterOfMass, BodyCollider, StearCollider,
            WheelColliders, Body, StearMesh, WheelMeshes, SolverIterations, SolverVelocityIterations,
            BrakeTorque, BrakeSpeed, HorseMassScale, IgnorePullingAnimals, debug, DebugColor,
            PlaySounds, WheelSurfaces, RollingSound, CreakSound, MaxSpeed, RollingVolume, RollingPitch, CreakVolume, CreakPitch,
            BumpSensitivity, BumpDecay, FadeTime, SpatialBlend, MinDistance, MaxDistance, MixerGroup, SurfaceCheckInterval,
            OnMovingChanged;

        /// <summary>Caches every serialized property drawn by this inspector</summary>
        protected virtual void OnEnable()
        {
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            HorseRigidBody = serializedObject.FindProperty("HorseRigidBody");
            HorseJoint = serializedObject.FindProperty("HorseJoint");
            MaxTurnAngle = serializedObject.FindProperty("MaxTurnAngle");
            CenterOfMass = serializedObject.FindProperty("CenterOfMass");
            BodyCollider = serializedObject.FindProperty("BodyCollider");
            StearCollider = serializedObject.FindProperty("StearCollider");
            WheelColliders = serializedObject.FindProperty("WheelColliders");
            Body = serializedObject.FindProperty("Body");
            StearMesh = serializedObject.FindProperty("StearMesh");
            WheelMeshes = serializedObject.FindProperty("WheelMeshes");
            SolverIterations = serializedObject.FindProperty("SolverIterations");
            SolverVelocityIterations = serializedObject.FindProperty("SolverVelocityIterations");
            BrakeTorque = serializedObject.FindProperty("BrakeTorque");
            BrakeSpeed = serializedObject.FindProperty("BrakeSpeed");
            HorseMassScale = serializedObject.FindProperty("HorseMassScale");
            IgnorePullingAnimals = serializedObject.FindProperty("IgnorePullingAnimals");
            debug = serializedObject.FindProperty("debug");
            DebugColor = serializedObject.FindProperty("DebugColor");

            PlaySounds = serializedObject.FindProperty("PlaySounds");
            WheelSurfaces = serializedObject.FindProperty("WheelSurfaces");
            RollingSound = serializedObject.FindProperty("RollingSound");
            CreakSound = serializedObject.FindProperty("CreakSound");
            MaxSpeed = serializedObject.FindProperty("MaxSpeed");
            RollingVolume = serializedObject.FindProperty("RollingVolume");
            RollingPitch = serializedObject.FindProperty("RollingPitch");
            CreakVolume = serializedObject.FindProperty("CreakVolume");
            CreakPitch = serializedObject.FindProperty("CreakPitch");
            BumpSensitivity = serializedObject.FindProperty("BumpSensitivity");
            BumpDecay = serializedObject.FindProperty("BumpDecay");
            FadeTime = serializedObject.FindProperty("FadeTime");
            SpatialBlend = serializedObject.FindProperty("SpatialBlend");
            MinDistance = serializedObject.FindProperty("MinDistance");
            MaxDistance = serializedObject.FindProperty("MaxDistance");
            MixerGroup = serializedObject.FindProperty("MixerGroup");
            SurfaceCheckInterval = serializedObject.FindProperty("SurfaceCheckInterval");
            OnMovingChanged = serializedObject.FindProperty("OnMovingChanged");
        }

        /// <summary>Repaints every frame in Play Mode while the Audio tab shows its live readout</summary>
        public override bool RequiresConstantRepaint() => Application.isPlaying && Editor_Tabs1 != null && Editor_Tabs1.intValue == 3;

        /// <summary>Draws the tabbed inspector followed by the Setup Check</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription
                ("Drives a Wagon pulled by an Animal.\nUse [Setup Check] to validate the Horse, the Wheels and the Wagon physics");

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, new string[] { "Horse", "Wagon", "Physics", "Audio", "Debug" });

                int Selection = Editor_Tabs1.intValue;

                if (Selection == 0) ShowHorse();
                else if (Selection == 1) ShowWagon();
                else if (Selection == 2) ShowPhysics();
                else if (Selection == 3) ShowAudio();
                else if (Selection == 4) ShowDebug();

                if (cc.changed) Undo.RecordObject(target, "Wagon Controller Changed");
            }

            if (targets.Length == 1)            //the check inspects one rig at a time
            {
                EditorGUILayout.Space();
                DrawSetupCheck((WagonController)target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws the Animal and hitch references</summary>
        private void ShowHorse()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                HorseRigidBody.isExpanded = MalbersEditor.Foldout(HorseRigidBody.isExpanded, "Hitch");

                if (HorseRigidBody.isExpanded)
                {
                    EditorGUILayout.PropertyField(HorseRigidBody);
                    EditorGUILayout.PropertyField(HorseJoint);
                    EditorGUILayout.PropertyField(MaxTurnAngle);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                BodyCollider.isExpanded = MalbersEditor.Foldout(BodyCollider.isExpanded, "Steering References");

                if (BodyCollider.isExpanded)
                {
                    EditorGUILayout.PropertyField(BodyCollider);
                    EditorGUILayout.PropertyField(StearCollider);
                }
            }
        }

        /// <summary>Draws the wheels and the meshes they drive</summary>
        private void ShowWagon()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                WheelColliders.isExpanded = MalbersEditor.Foldout(WheelColliders.isExpanded, "Wheels");

                if (WheelColliders.isExpanded)
                {
                    EditorGUILayout.PropertyField(WheelColliders, true);
                    EditorGUILayout.PropertyField(WheelMeshes, true);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Body.isExpanded = MalbersEditor.Foldout(Body.isExpanded, "Meshes");

                if (Body.isExpanded)
                {
                    EditorGUILayout.PropertyField(Body);
                    EditorGUILayout.PropertyField(StearMesh);
                    EditorGUILayout.PropertyField(CenterOfMass);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                OnMovingChanged.isExpanded = MalbersEditor.Foldout(OnMovingChanged.isExpanded, "Events");

                if (OnMovingChanged.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(OnMovingChanged);
                    EditorGUI.indentLevel--;
                }
            }
        }

        /// <summary>Draws the rolling and creak loops and their mixing</summary>
        private void ShowAudio()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(PlaySounds);
            }

            using (new EditorGUI.DisabledGroupScope(!PlaySounds.boolValue))
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    WheelSurfaces.isExpanded = MalbersEditor.Foldout(WheelSurfaces.isExpanded, "Rolling");

                    if (WheelSurfaces.isExpanded)
                    {
                        EditorGUILayout.PropertyField(WheelSurfaces);
                        EditorGUILayout.PropertyField(RollingSound);
                        EditorGUILayout.PropertyField(RollingVolume);
                        EditorGUILayout.PropertyField(RollingPitch);
                    }
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    CreakSound.isExpanded = MalbersEditor.Foldout(CreakSound.isExpanded, "Creak");

                    if (CreakSound.isExpanded)
                    {
                        EditorGUILayout.PropertyField(CreakSound);
                        EditorGUILayout.PropertyField(CreakVolume);
                        EditorGUILayout.PropertyField(CreakPitch);
                        EditorGUILayout.PropertyField(BumpSensitivity);
                        EditorGUILayout.PropertyField(BumpDecay);
                    }
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    MaxSpeed.isExpanded = MalbersEditor.Foldout(MaxSpeed.isExpanded, "Mixing");

                    if (MaxSpeed.isExpanded)
                    {
                        EditorGUILayout.PropertyField(MaxSpeed);
                        EditorGUILayout.PropertyField(FadeTime);
                        EditorGUILayout.PropertyField(SpatialBlend);
                        EditorGUILayout.PropertyField(MinDistance);
                        EditorGUILayout.PropertyField(MaxDistance);
                        EditorGUILayout.PropertyField(MixerGroup);
                        EditorGUILayout.PropertyField(SurfaceCheckInterval);
                    }
                }
            }

            if (Application.isPlaying && targets.Length == 1)
            {
                var wc = (WagonController)target;

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Speed", $"{wc.Speed:F2} m/s   {(wc.IsMoving ? "moving" : "stopped")}");
                    EditorGUILayout.LabelField("Surface", wc.CurrentSurface != null ? wc.CurrentSurface.name : "Default");
                    EditorGUILayout.LabelField("Ground", wc.CurrentGround ? $"{wc.CurrentGround.name}  [{LayerMask.LayerToName(wc.CurrentGround.gameObject.layer)}]" : "-");
                }
            }
        }

        /// <summary>Draws the stability and brake values</summary>
        private void ShowPhysics()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SolverIterations.isExpanded = MalbersEditor.Foldout(SolverIterations.isExpanded, "Stability");

                if (SolverIterations.isExpanded)
                {
                    EditorGUILayout.PropertyField(SolverIterations);
                    EditorGUILayout.PropertyField(SolverVelocityIterations);
                    EditorGUILayout.PropertyField(HorseMassScale);
                    EditorGUILayout.PropertyField(IgnorePullingAnimals);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                BrakeTorque.isExpanded = MalbersEditor.Foldout(BrakeTorque.isExpanded, "Brakes");

                if (BrakeTorque.isExpanded)
                {
                    EditorGUILayout.PropertyField(BrakeTorque);
                    EditorGUILayout.PropertyField(BrakeSpeed);
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
                    EditorGUILayout.LabelField("Draw Colliders and Wheels", GUILayout.MinWidth(60));
                    MalbersEditor.DrawDebugIcon(debug);
                }

                EditorGUILayout.PropertyField(DebugColor);
            }
        }

        /// <summary>Runs every validation and draws the results</summary>
        private void DrawSetupCheck(WagonController wc)
        {
            var checks = Validate(wc);

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
                    EditorGUILayout.HelpBox
                        ("The rig checks out. The Animal should pull the Wagon at every speed, and the Wagon should coast when the Animal stops.",
                         MessageType.Info);
                }

                foreach (var c in checks)
                {
                    EditorGUILayout.HelpBox(c.Message, c.Level);

                    if (c.Fix == null) continue;

                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(c.FixLabel, GUILayout.Width(170))) c.Fix();
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

        /// <summary>Builds the list of problems found on the rig</summary>
        private List<Check> Validate(WagonController wc)
        {
            var list = new List<Check>();
            var wagonRB = wc.GetComponent<Rigidbody>();

            if (!wagonRB)
            {
                list.Add(new Check
                {
                    Level = MessageType.Error,
                    Message = "The Wagon has no Rigidbody. Add one to the same GameObject as the Wagon Controller."
                });
                return list;                    //nothing else is measurable without it
            }

            ValidateAnimal(wc, list);
            ValidateJoint(wc, list);
            ValidateWheels(wc, list);
            ValidateBodies(wc, wagonRB, list);
            ValidateAudio(wc, list);

            return list;
        }

        /// <summary>Checks the rolling and creak loops</summary>
        private void ValidateAudio(WagonController wc, List<Check> list)
        {
            if (!wc.PlaySounds) return;

            var asset = wc.WheelSurfaces;
            var assetHasSound = asset && (!asset.DefaultSound.NullOrEmpty() || asset.Surfaces.Exists(s => s != null && s.sound));

            if (!assetHasSound && wc.RollingSound.NullOrEmpty())
            {
                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = "[Play Sounds] is on but there is no rolling loop. Assign a [Rolling Sound], or a [Wheel Surfaces] asset " +
                              "with a Default Sound or per-surface sounds. Loops must be authored to loop: an Audio Random Container needs " +
                              "Trigger [Automatic] and Loop [Infinite]."
                });
            }

            if (asset && asset.Surfaces.Count > 0 && !asset.Surfaces.Exists(s => s != null && (s.surface || (s.textures != null && s.textures.Exists(t => t)))))
            {
                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"No entry on [{asset.name}] has a Surface ID or a Texture, so none of them can ever match the ground. " +
                              "Only the asset Default Sound will play."
                });
            }

            if (wc.CreakSound.NullOrEmpty())
            {
                list.Add(new Check
                {
                    Level = MessageType.Info,
                    Message = "[Creak Sound] is empty. The Wagon will roll without the wood creak and rattle loop."
                });
            }

            if (wc.MaxSpeed <= wc.BrakeSpeed)
            {
                var target = wc;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"[Max Speed] is {wc.MaxSpeed:F2} m/s, which is not above [Brake Speed] ({wc.BrakeSpeed:F2}). Every volume and " +
                              "pitch curve would sit at its end value as soon as the Wagon moves.",
                    FixLabel = "Set to 13",
                    Fix = () => { Record(target, "Set Max Speed"); target.MaxSpeed = 13f; }
                });
            }
        }

        /// <summary>Checks the Animal that pulls the Wagon</summary>
        private void ValidateAnimal(WagonController wc, List<Check> list)
        {
            if (!wc.HorseRigidBody)
            {
                list.Add(new Check
                {
                    Level = MessageType.Error,
                    Message = "[Horse Rigid Body] is empty. Set it to the Rigidbody of the Animal that pulls the Wagon."
                });
                return;
            }

            //the component is often on a parent that groups a pair of Animals, so search upwards
            var ph = wc.HorseRigidBody.GetComponentInParent<PullingHorses>();

            if (!ph)
            {
                list.Add(new Check
                {
                    Level = MessageType.Error,
                    Message = $"No [Pulling Horses] found on [{wc.HorseRigidBody.name}] or any of its parents.\n" +
                              "Without it the steer angle, the wheel brakes and the turn limit are all disabled."
                });
                return;
            }

            var animator = wc.HorseRigidBody.GetComponent<Animator>();

            if (animator && animator.updateMode != AnimatorUpdateMode.Fixed)
            {
                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"The Animal's Animator Update Mode is [{animator.updateMode}]. The Wagon is simulated by physics, " +
                              "so the Animal has to run in [Fixed] for the two to stay in step. MAnimal forces this at runtime."
                });
            }

            if (wc.HorseRigidBody.isKinematic)
            {
                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = "The Animal's Rigidbody is Kinematic. A kinematic body cannot drive a jointed Wagon."
                });
            }
        }

        /// <summary>Checks the hitch joint between the Animal and the Wagon</summary>
        private void ValidateJoint(WagonController wc, List<Check> list)
        {
            if (!wc.HorseJoint)
            {
                list.Add(new Check
                {
                    Level = MessageType.Error,
                    Message = "[Horse Joint] is empty. Set it to the Configurable Joint that hitches the Wagon to the Animal."
                });
                return;
            }

            if (!wc.HorseJoint.connectedBody && wc.HorseRigidBody)
            {
                var joint = wc.HorseJoint;
                var body = wc.HorseRigidBody;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = "The hitch Joint has no Connected Body saved. It is assigned on Start, but leaving it empty " +
                              "means the Joint is welded to the world for the first frames.",
                    FixLabel = "Assign Connected Body",
                    Fix = () => { Record(joint, "Assign Connected Body"); joint.connectedBody = body; }
                });
            }

            if (Mathf.Approximately(wc.HorseMassScale, 1f))
            {
                var target = wc;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = "[Horse Mass Scale] is 1. MAnimal writes the Animal's velocity directly every physics step, so the " +
                              "Animal behaves as if it were kinematic. With a scale of 1 the Joint splits every correction between " +
                              "Animal and Wagon by mass and drags the Animal backwards, which makes it run on the spot at low speeds.\n" +
                              "A value around 10 tells the solver to move the Wagon instead.",
                    FixLabel = "Set to 10",
                    Fix = () => { Record(target, "Set Horse Mass Scale"); target.HorseMassScale = 10f; }
                });
            }
        }

        /// <summary>Checks the wheel colliders, their suspension and their meshes</summary>
        private void ValidateWheels(WagonController wc, List<Check> list)
        {
            if (wc.WheelColliders == null || wc.WheelColliders.Length == 0)
            {
                list.Add(new Check { Level = MessageType.Error, Message = "[Wheel Colliders] is empty. Add every Wheel Collider of the Wagon." });
                return;
            }

            var meshCount = wc.WheelMeshes != null ? wc.WheelMeshes.Length : 0;

            if (meshCount != wc.WheelColliders.Length)
            {
                list.Add(new Check
                {
                    Level = MessageType.Error,
                    Message = $"[Wheel Meshes] has {meshCount} entries but [Wheel Colliders] has {wc.WheelColliders.Length}. " +
                              "They are matched by index, so both arrays must be the same length and in the same order."
                });
            }

            for (int i = 0; i < wc.WheelColliders.Length; i++)
            {
                var w = wc.WheelColliders[i];

                if (!w)
                {
                    list.Add(new Check { Level = MessageType.Error, Message = $"[Wheel Colliders] element {i} is empty." });
                    continue;
                }

                var body = w.attachedRigidbody;
                if (!body) continue;

                //each wheel carries an equal share of the Rigidbody it belongs to
                var sprung = body.mass / Mathf.Max(WheelsOn(wc, body), 1);
                var travel = w.suspensionDistance * w.suspensionSpring.targetPosition;

                if (travel <= 0.0001f) continue;

                var required = sprung * Gravity / travel;
                var ratio = w.suspensionSpring.spring / required;

                if (ratio < 0.7f)
                {
                    var wheel = w;
                    var target = Mathf.Round(required / 100f) * 100f;

                    list.Add(new Check
                    {
                        Level = ratio < 0.4f ? MessageType.Error : MessageType.Warning,
                        Message = $"[{w.name}] suspension is too soft. Spring is {w.suspensionSpring.spring:F0} but this wheel carries " +
                                  $"{sprung:F0} kg, which needs about {required:F0} to hold it at the target position.\n" +
                                  (ratio < 0.4f ? "At this value the suspension is permanently bottomed out and the Wagon scrapes and hops."
                                                : "The Wagon will sit lower than intended."),
                        FixLabel = $"Set spring to {target:F0}",
                        Fix = () =>
                        {
                            Record(wheel, "Fix Suspension Spring");
                            var s = wheel.suspensionSpring; s.spring = target; wheel.suspensionSpring = s;
                        }
                    });
                }

                //a spring with no damping oscillates; critical damping is 2*sqrt(k*m)
                var critical = 2f * Mathf.Sqrt(Mathf.Max(w.suspensionSpring.spring, 1f) * Mathf.Max(sprung, 0.01f));
                var damping = w.suspensionSpring.damper / critical;

                if (damping < 0.2f || damping > 1.2f)
                {
                    var wheel = w;
                    var target = Mathf.Round(critical * 0.4f / 10f) * 10f;

                    list.Add(new Check
                    {
                        Level = MessageType.Warning,
                        Message = $"[{w.name}] damper is {w.suspensionSpring.damper:F0}, which is {damping:P0} of critical damping " +
                                  $"({critical:F0}). " + (damping < 0.2f ? "The Wagon will bounce and never settle." : "The suspension is over-damped and will feel rigid.") +
                                  "\nAim for 20% to 80% of critical.",
                        FixLabel = $"Set damper to {target:F0}",
                        Fix = () =>
                        {
                            Record(wheel, "Fix Suspension Damper");
                            var s = wheel.suspensionSpring; s.damper = target; wheel.suspensionSpring = s;
                        }
                    });
                }

                if (w.mass > sprung * 0.6f)
                {
                    var wheel = w;
                    var target = Mathf.Round(sprung * 0.3f);

                    list.Add(new Check
                    {
                        Level = MessageType.Warning,
                        Message = $"[{w.name}] wheel mass is {w.mass:F0} against the {sprung:F0} kg it carries. A wheel that heavy " +
                                  "dominates the suspension response and makes the Wagon feel unstable.",
                        FixLabel = $"Set mass to {target:F0}",
                        Fix = () => { Record(wheel, "Fix Wheel Mass"); wheel.mass = target; }
                    });
                }
            }
        }

        /// <summary>Checks the Rigidbodies that make up the Wagon</summary>
        private void ValidateBodies(WagonController wc, Rigidbody wagonRB, List<Check> list)
        {
            //MWC: interpolation is deliberately not validated. The project rule is a Fixed Update camera Brain
            //with Rigidbody Interpolation [None], so every body and the camera are sampled on the same step.

            if (Physics.defaultSolverIterations < 6 && wc.SolverIterations < 6)
            {
                var target = wc;

                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = $"The project's Default Solver Iterations is {Physics.defaultSolverIterations} and [Solver Iterations] is " +
                              $"{wc.SolverIterations}. The Animal, the Wagon and its Body are chained by Joints with every linear axis " +
                              "Locked, and that chain cannot converge on so few iterations. The Joints will visibly stretch.",
                    FixLabel = "Set to 12",
                    Fix = () => { Record(target, "Set Solver Iterations"); target.SolverIterations = 12; }
                });
            }

            if (!wc.IgnorePullingAnimals)
            {
                list.Add(new Check
                {
                    Level = MessageType.Warning,
                    Message = "[Ignore Pulling Animals] is off. [Enable Collision] on a Joint only covers the two bodies that Joint " +
                              "connects, and the Animal is never jointed directly to the Wagon Body, so their colliders can jam " +
                              "against each other on tight turns."
                });
            }

            if (!wc.CenterOfMass)
            {
                list.Add(new Check
                {
                    Level = MessageType.Info,
                    Message = "[Center Of Mass] is empty, so the Wagon uses the centre implied by its colliders. On a Wagon whose only " +
                              "colliders sit at the axles that is usually too high and too far forward. Assign a low, central Transform " +
                              "if the Wagon feels tippy."
                });
            }
        }

        /// <summary>Number of the Wagon's wheels that share the given Rigidbody</summary>
        private int WheelsOn(WagonController wc, Rigidbody body)
        {
            int n = 0;

            foreach (var w in wc.WheelColliders)
                if (w && w.attachedRigidbody == body) n++;

            return n;
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
