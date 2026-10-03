using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// つかめる版のシーン（SpatialSearchLiteGrab）を作るメニュー。足りないものだけを作るので、何度実行してもよい。
//   1. SpatialSearchLite シーンを複製して開く（既にあればそれを開く）
//   2. カメラ画像の色で描く点群のマテリアルと、コントローラでつかめる点群のひな形プレハブを作る
//   3. Logic/SearchResultView を GrabbableSearchResultView に差し替え、MainCoordinator の参照も付け替えて保存する
// 元の SpatialSearchLite シーンは変更しない。
public static class GrabSceneBuilder
{
    const string SourceScenePath = "Assets/Scenes/SpatialSearchLite.unity";
    const string ScenePath = "Assets/Scenes/SpatialSearchLiteGrab.unity";
    const string MaterialPath = "Assets/Materials/SpatialSearchColoredPointsMat.mat";
    const string PrefabPath = "Assets/Prefabs/GrabbableSearchResult.prefab";
    const string ShaderName = "SpatialSearch/URP/Colored Points";
    const string TintProperty = "_TintColor";

    [MenuItem("Tools/Spatial Search Lite/Create Grab Scene")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var material = GetOrCreateMaterial();
        if (material == null) return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
        {
            Debug.LogError($"[GrabSceneBuilder] {SourceScenePath} を複製できませんでした");
            return;
        }
        var scene = EditorSceneManager.OpenScene(ScenePath);

        var template = GetOrCreateTemplate(material);
        var coordinator = Object.FindAnyObjectByType<MainCoordinator>();
        var view = GetOrReplaceView();
        if (coordinator == null || view == null)
        {
            Debug.LogError("[GrabSceneBuilder] MainCoordinator / SearchResultView が見つかりません");
            return;
        }

        var viewSo = new SerializedObject(view);
        viewSo.FindProperty("_objectTemplate").objectReferenceValue = template;
        viewSo.ApplyModifiedPropertiesWithoutUndo();

        var coordinatorSo = new SerializedObject(coordinator);
        coordinatorSo.FindProperty("_resultView").objectReferenceValue = view;
        coordinatorSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeObject = view;
        Debug.Log($"[GrabSceneBuilder] {ScenePath} を作成しました。Build Profiles のシーン一覧に追加してからビルドしてください");
    }

    static Material GetOrCreateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[GrabSceneBuilder] シェーダー {ShaderName} が見つかりません");
            return null;
        }

        material = new Material(shader);
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    // 物体1つぶんの点群のひな形。コントローラを近づけてグリップでつかめる（手はつかめない）。
    //   Rigidbody + BoxCollider : つかめる範囲。箱の大きさは GrabbableSearchResultView が点群に合わせる
    //   Grabbable + GrabFreeTransformer : つかんで動かす・回す（両手なら拡大縮小も）
    //   GrabInteractable : コントローラの ControllerGrabInteractor からつかめるようにする
    //   InteractableColorVisual : 近づけた・つかんだときに点群へ色を重ねる
    static MeshFilter GetOrCreateTemplate(Material material)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing.GetComponent<MeshFilter>();

        var go = new GameObject("GrabbableSearchResult");
        try
        {
            go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var transformer = go.AddComponent<GrabFreeTransformer>();
            var grabbable = go.AddComponent<Grabbable>();
            var grabbableSo = new SerializedObject(grabbable);
            grabbableSo.FindProperty("_oneGrabTransformer").objectReferenceValue = transformer;
            grabbableSo.FindProperty("_twoGrabTransformer").objectReferenceValue = transformer;
            grabbableSo.FindProperty("_rigidbody").objectReferenceValue = body;
            grabbableSo.FindProperty("_throwWhenUnselected").boolValue = false; // 離したらその場に止める
            grabbableSo.ApplyModifiedPropertiesWithoutUndo();

            var interactable = go.AddComponent<GrabInteractable>();
            var interactableSo = new SerializedObject(interactable);
            interactableSo.FindProperty("_pointableElement").objectReferenceValue = grabbable;
            interactableSo.FindProperty("_rigidbody").objectReferenceValue = body;
            interactableSo.ApplyModifiedPropertiesWithoutUndo();

            var blockEditor = go.AddComponent<MaterialPropertyBlockEditor>();
            var blockEditorSo = new SerializedObject(blockEditor);
            var renderers = blockEditorSo.FindProperty("_renderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = meshRenderer;
            blockEditorSo.ApplyModifiedPropertiesWithoutUndo();

            // シェーダーは _TintColor の A を重ねる強さとして使う。普段は A=0 で元の色のまま。
            var colorVisual = go.AddComponent<InteractableColorVisual>();
            var colorVisualSo = new SerializedObject(colorVisual);
            colorVisualSo.FindProperty("_interactableView").objectReferenceValue = interactable;
            colorVisualSo.FindProperty("_editor").objectReferenceValue = blockEditor;
            colorVisualSo.FindProperty("_colorShaderPropertyName").stringValue = TintProperty;
            colorVisualSo.FindProperty("_normalColorState.Color").colorValue = new Color(1f, 1f, 1f, 0f);
            colorVisualSo.FindProperty("_hoverColorState.Color").colorValue = new Color(1f, 1f, 1f, 0.35f);
            colorVisualSo.FindProperty("_selectColorState.Color").colorValue = new Color(1f, 0.9f, 0f, 0.35f);
            colorVisualSo.FindProperty("_disabledColorState.Color").colorValue = new Color(1f, 1f, 1f, 0f);
            colorVisualSo.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            return prefab.GetComponent<MeshFilter>();
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 複製したシーンの SearchResultView を GrabbableSearchResultView に差し替える。状態表示の参照は引き継ぐ。
    static GrabbableSearchResultView GetOrReplaceView()
    {
        var view = Object.FindAnyObjectByType<GrabbableSearchResultView>();
        if (view != null) return view;

        var oldView = Object.FindAnyObjectByType<SearchResultView>();
        if (oldView == null) return null;

        var go = oldView.gameObject;
        var statusText = new SerializedObject(oldView).FindProperty("_statusText").objectReferenceValue;

        // SearchResultView が MeshFilter / MeshRenderer を必要とするので、SearchResultView から先に消す。
        Object.DestroyImmediate(oldView);
        if (go.TryGetComponent<MeshRenderer>(out var meshRenderer)) Object.DestroyImmediate(meshRenderer);
        if (go.TryGetComponent<MeshFilter>(out var meshFilter)) Object.DestroyImmediate(meshFilter);

        view = go.AddComponent<GrabbableSearchResultView>();
        var so = new SerializedObject(view);
        so.FindProperty("_statusText").objectReferenceValue = statusText;
        so.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }
}
