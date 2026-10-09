using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.IK
{
    [CreateAssetMenu(menuName = "Malbers Animations/IK/IK Processor Profile", fileName = "New IK Profile")]
    public class IKSetProfile : ScriptableObject
    {
        public IKSet set;

        public int EditorTabs;

        internal void Initialize(IKManager iKManager) => set.Initialize(iKManager);

        internal void ValidateIK(IKManager manager) => set.OnValidate(manager);
    }


    //Draw the IK Set in the Inspector
#if UNITY_EDITOR

    [CustomEditor(typeof(IKSetProfile))]
    public class IKProcessorProfileEditor : Editor
    {
        //MWC — the whole IK Set UI now comes from the shared IKSetDrawer (also used by IKManager & IKSetLibrary)
        private IKSetDrawer drawer;

        private SerializedProperty set, EditorTabs;

        private IKSetProfile m;

        private void OnEnable()
        {
            m = (IKSetProfile)target;

            set = serializedObject.FindProperty("set");
            EditorTabs = serializedObject.FindProperty("EditorTabs");

            drawer = new IKSetDrawer(m); //No IKManager: this Set lives inside an asset
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Foldout_Bold(true, m.name, MTools.MBlue * 2, true);

                // ── Paste from IKManager clipboard ────────────────────────────────
                DrawPasteFromClipboardButton();
                // ─────────────────────────────────────────────────────────────────

                drawer.DrawSet(set, m.set, EditorTabs);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws a button that pastes IKProcessors and WeightProcessors from the
        /// IKSetCopyPaste clipboard into this profile's IKSet, leaving Tags untouched.
        /// </summary>
        private void DrawPasteFromClipboardButton()
        {
            if (!IKSetCopyPaste.HasClipboard) return;

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);

            if (GUILayout.Button(new GUIContent(
                $"Paste IK Set from Clipboard  [{IKSetCopyPaste.ClipboardSetName}]",
                "Pastes the IKProcessors and WeightProcessors copied from an IKManager.\nTargets (Tags) are kept as-is.")))
            {
                Undo.RecordObject(m, "Paste IKSet Processors");
                var sourceName = IKSetCopyPaste.ClipboardSetName; // capture before paste clears it
                IKSetCopyPaste.PasteProcessorsInto(m.set);
                EditorUtility.SetDirty(m);
                serializedObject.Update();
                drawer.ClearCache(); //MWC — the processor lists were replaced
                Debug.Log($"[IKSetProfile] Pasted processors from \"{sourceName}\" into \"{m.name}\".");
            }

            GUI.backgroundColor = prevColor;
        }
    }
#endif
}
