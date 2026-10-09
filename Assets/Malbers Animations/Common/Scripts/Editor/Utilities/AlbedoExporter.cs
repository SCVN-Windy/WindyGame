// ============================================================
//  AlbedoExporter.cs
//  Malbers Animations — Unity 6 / C# 9
//
//  Bakes the EXACT Base Color fragment output of any material
//  by invoking its Meta pass — the same mechanism Unity's
//  lightmapper uses to extract albedo — so the result is
//  pixel-perfect regardless of what shader calculations feed
//  into the Base Color node.
//
//  Usage:
//    • Tools / Malbers Animations / Editor / Albedo Exporter
//    • Right-click a Material asset → Malbers Animations / Export Albedo PNG
// ============================================================

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalbersAnimations
{
    public class AlbedoExporter : EditorWindow
    {
        // ── Resolution enum ───────────────────────────────────────────────────────

        public enum ExportResolution
        {
            _64 = 64,
            _128 = 128,
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
        }

        // ── State ─────────────────────────────────────────────────────────────────

        private Material _material;
        private ExportResolution _resolution = ExportResolution._512;
        private Texture2D _preview;
        private string _statusMessage;
        private MessageType _statusType;

        // MWC: batch export state
        private readonly List<Material> _batchList = new();
        private Vector2 _batchScroll;

        // ── Menu items ────────────────────────────────────────────────────────────

        [MenuItem("Tools/Malbers Animations/Editor/Albedo Exporter")]
        public static void Open()
        {
            var w = GetWindow<AlbedoExporter>("Albedo Exporter");
            w.minSize = new Vector2(320f, 500f);
            w.Show();
        }

        [MenuItem("Assets/Malbers Animations/Export Albedo PNG", validate = true)]
        static bool ValidateAssetMenu() => Selection.activeObject is Material;

        [MenuItem("Assets/Malbers Animations/Export Albedo PNG")]
        static void OpenFromAsset()
        {
            var w = GetWindow<AlbedoExporter>("Albedo Exporter");
            w.minSize = new Vector2(320f, 500f);
            w.Assign(Selection.activeObject as Material);
            w.Show();
        }

        // ── EditorWindow GUI ──────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("  Base Color PNG Exporter", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  Bakes the exact Base Color Fragment output via Meta pass.",
                EditorStyles.miniLabel);
            Separator();

            DrawSingleSection();

            EditorGUILayout.Space(6f);
            Separator();

            DrawBatchSection();
        }

        // ── Single material section ───────────────────────────────────────────────

        private void DrawSingleSection()
        {
            EditorGUILayout.LabelField("Single Export", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _material = (Material)EditorGUILayout.ObjectField(
                "Material", _material, typeof(Material), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck()) Assign(_material);

            _resolution = (ExportResolution)EditorGUILayout.EnumPopup("Resolution", _resolution);

            EditorGUILayout.Space(4f);

            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusType);

            if (_preview != null)
            {
                EditorGUILayout.Space(4f);
                float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 24f, 240f);
                Rect rect = GUILayoutUtility.GetRect(size, size);
                rect.width = size;
                EditorGUI.DrawPreviewTexture(rect, _preview, mat: null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(_material == null || _preview == null))
            {
                if (GUILayout.Button("Export PNG", GUILayout.Height(28f)))
                    DoExport();
            }
        }

        // ── Batch section ─────────────────────────────────────────────────────────

        private void DrawBatchSection()
        {
            EditorGUILayout.LabelField("Batch Export", EditorStyles.boldLabel);

            // Toolbar row
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected", EditorStyles.miniButtonLeft))
                    AddSelectedMaterials();

                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50f)))
                    _batchList.Clear();
            }

            // Material list
            float listHeight = Mathf.Clamp(_batchList.Count * 22f + 4f, 44f, 176f);
            _batchScroll = EditorGUILayout.BeginScrollView(
                _batchScroll, GUILayout.Height(listHeight));

            if (_batchList.Count == 0)
            {
                EditorGUILayout.LabelField("  Drag materials here or use Add Selected.",
                    EditorStyles.centeredGreyMiniLabel, GUILayout.Height(36f));
            }
            else
            {
                for (int i = _batchList.Count - 1; i >= 0; i--)
                {
                    using var row = new EditorGUILayout.HorizontalScope();

                    _batchList[i] = (Material)EditorGUILayout.ObjectField(
                        _batchList[i], typeof(Material), allowSceneObjects: false);

                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f)))
                        _batchList.RemoveAt(i);
                }
            }

            EditorGUILayout.EndScrollView();

            // Handle drag-and-drop onto the list area
            AcceptDragDrop(GUILayoutUtility.GetLastRect());

            EditorGUILayout.Space(4f);
            _resolution = (ExportResolution)EditorGUILayout.EnumPopup("Resolution", _resolution);
            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(_batchList.Count == 0))
            {
                if (GUILayout.Button($"Batch Export ({_batchList.Count}) …", GUILayout.Height(28f)))
                    DoBatchExport();
            }

            EditorGUILayout.Space(4f);
        }

        // ── Drag-and-drop support ─────────────────────────────────────────────────

        // MWC: allow dragging material assets from the Project window into the list.
        private void AcceptDragDrop(Rect dropArea)
        {
            var ev = Event.current;
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            if (!dropArea.Contains(ev.mousePosition)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Material mat && !_batchList.Contains(mat))
                        _batchList.Add(mat);
                }
                ev.Use();
                Repaint();
            }
        }

        // ── Assignment ────────────────────────────────────────────────────────────

        private void AddSelectedMaterials()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Material mat && !_batchList.Contains(mat))
                    _batchList.Add(mat);
            }
        }

        private void Assign(Material mat)
        {
            _material = mat;
            _statusMessage = null;
            ClearPreview();

            if (mat == null) { Repaint(); return; }

            _preview = BakeBaseColor(mat, 128);

            if (_preview == null)
            {
                _statusMessage = "Failed to bake — check the Console for details.";
                _statusType = MessageType.Warning;
            }

            Repaint();
        }

        // ── Core bake ────────────────────────────────────────────────────────────────

        // MWC: takes explicit material so the same path works for single and batch export.
        private Texture2D BakeBaseColor(Material mat, int resolution)
        {
            if (mat == null) return null;

            int metaPass = mat.FindPass("Meta");
            if (metaPass >= 0)
                return BakeViaMetaPass(mat, resolution, metaPass);

            return BakeViaDirectSample(mat, resolution);
        }

        // MWC: On DX11/DX12/Vulkan, unity_MetaFragmentControl lives inside the
        //      "UnityMetaPass" CBUFFER — Shader.SetGlobalVector can't reach it and the
        //      branch `if (unity_MetaFragmentControl.x)` is never taken → black output.
        //      Fix: bind a ComputeBuffer that matches the CBUFFER layout exactly.
        //      We clone the material so we never dirty the original project asset.
        //
        //  UnityMetaPass CBUFFER layout (HLSL std packing, 48 bytes total):
        //    bool4  unity_MetaVertexControl   offset  0, 16 B  → use UV1 for vertex pos
        //    bool4  unity_MetaFragmentControl offset 16, 16 B  → output albedo when .x ≠ 0
        //    float  unity_OneOverOutputBoost  offset 32,  4 B  → exponent (1 = identity)
        //    float  unity_MaxOutputValue      offset 36,  4 B  → clamp ceiling (1 = LDR)
        //    float  unity_UseLinearSpace      offset 40,  4 B
        //    float  unity_SpecularBoost       offset 44,  4 B
        private Texture2D BakeViaMetaPass(Material mat, int resolution, int metaPass)
        {
            var mesh = BuildUVQuad();

            // MWC: explicit element assignment avoids the Mono RuntimeHelpers.InitializeArray
            //      bug that fires for constant float[] initializers of this size.
            // float nonzero == true for HLSL bool in a CBUFFER (0x3F800000 ≠ 0)
            var cbufData = new float[12];
            cbufData[0] = 1f; cbufData[1] = 0f; cbufData[2] = 0f; cbufData[3] = 0f; // MetaVertexControl
            cbufData[4] = 1f; cbufData[5] = 0f; cbufData[6] = 0f; cbufData[7] = 0f; // MetaFragmentControl
            cbufData[8] = 1f; // OneOverOutputBoost — pow(albedo,1) = identity
            cbufData[9] = 1f; // MaxOutputValue     — clamp to 1 (LDR)
            cbufData[10] = 0f; // UseLinearSpace
            cbufData[11] = 1f; // SpecularBoost
            var metaCbuf = new ComputeBuffer(
                cbufData.Length, sizeof(float), ComputeBufferType.Constant);
            metaCbuf.SetData(cbufData);

            // Clone the material so SetConstantBuffer never dirties the project asset
            var bakeMat = new Material(mat) { hideFlags = HideFlags.HideAndDontSave };
            bakeMat.SetConstantBuffer(
                "UnityMetaPass", metaCbuf, 0, cbufData.Length * sizeof(float));

            // GLES fallback: same names are plain uniforms there, not CBUFFER-bound
            Shader.SetGlobalVector("unity_MetaFragmentControl", new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalVector("unity_MetaVertexControl", new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalFloat("unity_OneOverOutputBoost", 1f);
            Shader.SetGlobalFloat("unity_MaxOutputValue", 1f);
            Shader.SetGlobalVector("unity_LightmapST", new Vector4(1f, 1f, 0f, 0f));
            Shader.SetGlobalVector("unity_DynamicLightmapST", new Vector4(1f, 1f, 0f, 0f));

            var rt = RenderTexture.GetTemporary(resolution, resolution, depthBuffer: 0,
                format: RenderTextureFormat.ARGB32, readWrite: RenderTextureReadWrite.sRGB);

            // Ortho VP: maps object-space x,y in 0..1 → clip-space −1..1
            var gpuProj = GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), renderIntoTexture: true);

            var cb = new CommandBuffer { name = "AlbedoExporter_MetaBake" };
            cb.SetViewProjectionMatrices(Matrix4x4.identity, gpuProj);
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.DrawMesh(mesh, Matrix4x4.identity, bakeMat, 0, metaPass);
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();

            DestroyImmediate(bakeMat);
            DestroyImmediate(mesh);
            metaCbuf.Release();

            Shader.SetGlobalVector("unity_MetaFragmentControl", Vector4.zero);
            Shader.SetGlobalVector("unity_MetaVertexControl", Vector4.zero);

            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false);
            result.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
            result.Apply();
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);

            // MWC: on DX the render-texture Y-flip from GetGPUProjectionMatrix(renderIntoTexture:true)
            //      inverts the image — flip pixel rows to correct it.
            FlipVertical(result);

            return result;
        }

        // MWC: fallback for shaders without a Meta pass — uses HasProperty to avoid
        //      Unity errors that GetTexture throws when a property doesn't exist.
        private static Texture2D BakeViaDirectSample(Material mat, int resolution)
        {
            Texture srcTex = null;
            foreach (var name in new[] { "_BaseMap", "_BaseColorMap", "_MainTex", "_Albedo", "_AlbedoMap" })
            {
                if (!mat.HasProperty(name)) continue;
                srcTex = mat.GetTexture(name);
                if (srcTex != null) break;
            }

            if (srcTex == null)
            {
                var shader = mat.shader;
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count && srcTex == null; i++)
                {
                    if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    string n = shader.GetPropertyName(i).ToLower();
                    if (n.Contains("albedo") || n.Contains("base") || n.Contains("diffuse"))
                        srcTex = mat.GetTexture(shader.GetPropertyName(i));
                }
            }

            Color tint = Color.white;
            if (mat.HasProperty("_BaseColor")) tint = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color")) tint = mat.GetColor("_Color");

            var rt = RenderTexture.GetTemporary(resolution, resolution, depthBuffer: 0,
                             format: RenderTextureFormat.ARGB32, readWrite: RenderTextureReadWrite.sRGB);
            var prevRT = RenderTexture.active;

            if (srcTex != null)
            {
                Graphics.Blit(srcTex, rt);
            }
            else
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, tint.linear);
                tint = Color.white;
            }

            RenderTexture.active = rt;
            var result = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false);
            result.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0);
            result.Apply();
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);

            if (tint != Color.white)
            {
                Color[] pixels = result.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] *= tint;
                result.SetPixels(pixels);
                result.Apply();
            }

            return result;
        }

        // UV quad: UV0=UV1=0..1 so the Meta pass can sample textures (UV0) and
        // MetaVertexPosition can reposition vertices via UV1.
        private static Mesh BuildUVQuad()
        {
            var m = new Mesh();
            m.vertices = new[] {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(1f, 0f, 0f)
            };
            var uv = new[] {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f)
            };
            m.uv = uv;
            m.uv2 = uv;
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return m;
        }

        // ── Export ────────────────────────────────────────────────────────────────

        private void DoExport()
        {
            if (_material == null) return;

            var tex = BakeBaseColor(_material, (int)_resolution);
            if (tex == null)
            {
                EditorUtility.DisplayDialog("Albedo Exporter",
                    "Bake failed — see the Console for details.", "OK");
                return;
            }

            string matPath = AssetDatabase.GetAssetPath(_material);
            string defaultDir = string.IsNullOrEmpty(matPath)
                ? "Assets"
                : Path.GetDirectoryName(matPath)!.Replace('\\', '/');

            string savePath = EditorUtility.SaveFilePanel(
                "Export Base Color PNG", defaultDir,
                $"{_material.name}_BaseColor", "png");

            if (string.IsNullOrEmpty(savePath)) { DestroyImmediate(tex); return; }

            WriteAndImport(tex, savePath);
            DestroyImmediate(tex);
        }

        // MWC: batch export — picks an output folder then bakes every material in the list.
        private void DoBatchExport()
        {
            if (_batchList.Count == 0) return;

            string outDir = EditorUtility.OpenFolderPanel(
                "Select Output Folder for Batch Export", "Assets", "");
            if (string.IsNullOrEmpty(outDir)) return;

            int res = (int)_resolution;
            int succeeded = 0;

            try
            {
                for (int i = 0; i < _batchList.Count; i++)
                {
                    var mat = _batchList[i];
                    if (mat == null) continue;

                    EditorUtility.DisplayProgressBar(
                        "Albedo Exporter — Batch",
                        $"Baking {mat.name}  ({i + 1}/{_batchList.Count})",
                        (float)i / _batchList.Count);

                    var tex = BakeBaseColor(mat, res);
                    if (tex == null)
                    {
                        Debug.LogWarning($"[Malbers Animations] Batch: bake failed for '{mat.name}'");
                        continue;
                    }

                    string savePath = Path.Combine(outDir, $"{mat.name}_BaseColor.png")
                                         .Replace('\\', '/');
                    WriteAndImport(tex, savePath);
                    DestroyImmediate(tex);
                    succeeded++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Malbers Animations] Batch export complete — {succeeded}/{_batchList.Count} materials exported to {outDir}");
        }

        // Writes PNG bytes and reimports the asset if it's inside the project.
        private static void WriteAndImport(Texture2D tex, string savePath)
        {
            File.WriteAllBytes(savePath, tex.EncodeToPNG());

            if (savePath.StartsWith(Application.dataPath))
            {
                string relative = "Assets" + savePath[Application.dataPath.Length..];
                AssetDatabase.ImportAsset(relative);
            }

            Debug.Log($"[Malbers Animations] Base Color exported → {savePath}");
        }

        // ── Cleanup ───────────────────────────────────────────────────────────────

        private void ClearPreview()
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = null;
        }

        private void OnDestroy() => ClearPreview();

        // ── Helpers ───────────────────────────────────────────────────────────────

        // MWC: flips pixel rows in-place — corrects DX render-texture vertical inversion.
        private static void FlipVertical(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            Color[] pixels = tex.GetPixels();
            var row = new Color[w];
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * w, bot = (h - 1 - y) * w;
                System.Array.Copy(pixels, top, row, 0, w);
                System.Array.Copy(pixels, bot, pixels, top, w);
                System.Array.Copy(row, 0, pixels, bot, w);
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private static void Separator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.3f));
            EditorGUILayout.Space(2f);
        }
    }
}
#endif