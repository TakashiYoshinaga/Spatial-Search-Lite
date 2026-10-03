using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 手の枠で物体を指して複製するシーン（SpatialSearchLiteFrame）を作るメニュー。足りないものだけを作るので、何度実行してもよい。
//   1. SpatialSearchLiteGrab シーンを複製して開く（既にあればそれを開く）
//   2. 点・リング・枠の線に使うマテリアルを作る
//   3. 手でもつかめる複製のひな形プレハブ（GrabbableCopy）を作り、GrabbableSearchResultView に使わせる
//   4. Logic に FrameCopyController を足し、MainCoordinator から使えるようにして保存する
// 元の SpatialSearchLiteGrab シーンと GrabbableSearchResult プレハブは変更しない。
public static class FrameSceneBuilder
{
    const string SourceScenePath = "Assets/Scenes/SpatialSearchLiteGrab.unity";
    const string ScenePath = "Assets/Scenes/SpatialSearchLiteFrame.unity";
    const string MaterialPath = "Assets/Materials/FramePointerMat.mat";
    const string SourcePrefabPath = "Assets/Prefabs/GrabbableSearchResult.prefab";
    const string PrefabPath = "Assets/Prefabs/GrabbableCopy.prefab";
    const string ShaderName = "Sprites/Default"; // LineRenderer の頂点カラーで色を付ける

    [MenuItem("Tools/Spatial Search Lite/Create Frame Copy Scene")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
        {
            Debug.LogError($"[FrameSceneBuilder] {SourceScenePath} がありません。先に Tools > Spatial Search Lite > Create Grab Scene を実行してください");
            return;
        }

        var material = GetOrCreateMaterial();
        var template = GetOrCreateTemplate();
        if (material == null || template == null) return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
        {
            Debug.LogError($"[FrameSceneBuilder] {SourceScenePath} を複製できませんでした");
            return;
        }
        var scene = EditorSceneManager.OpenScene(ScenePath);

        var coordinator = Object.FindAnyObjectByType<MainCoordinator>();
        var capture = Object.FindAnyObjectByType<SearchCapture>();
        var view = Object.FindAnyObjectByType<GrabbableSearchResultView>();
        if (coordinator == null || capture == null || view == null)
        {
            Debug.LogError("[FrameSceneBuilder] MainCoordinator / SearchCapture / GrabbableSearchResultView が見つかりません");
            return;
        }

        var viewSo = new SerializedObject(view);
        viewSo.FindProperty("_objectTemplate").objectReferenceValue = template;
        viewSo.ApplyModifiedPropertiesWithoutUndo();

        var frameCopy = Object.FindAnyObjectByType<FrameCopyController>();
        if (frameCopy == null)
        {
            var go = new GameObject("FrameCopy");
            go.transform.SetParent(capture.transform.parent, false);
            frameCopy = go.AddComponent<FrameCopyController>();
        }
        var frameCopySo = new SerializedObject(frameCopy);
        frameCopySo.FindProperty("_visualMaterial").objectReferenceValue = material;
        frameCopySo.ApplyModifiedPropertiesWithoutUndo();

        var coordinatorSo = new SerializedObject(coordinator);
        coordinatorSo.FindProperty("_frameCopy").objectReferenceValue = frameCopy;
        coordinatorSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeObject = frameCopy;
        Debug.Log($"[FrameSceneBuilder] {ScenePath} を作成しました。Build Profiles のシーン一覧に追加してからビルドしてください");
    }

    static Material GetOrCreateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[FrameSceneBuilder] シェーダー {ShaderName} が見つかりません");
            return null;
        }

        material = new Material(shader);
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    // GrabbableSearchResult を複製し、手でもつかめるよう HandGrabInteractable を足したひな形。
    // 手の枠で操作するときはコントローラを置いているので、手でつかめないと複製を動かせない。
    // HandGrabInteractable には手の形（HandGrabPose）を登録しないので、つまむ・握るのどちらでも、手の形はそのままでつかむ。
    static MeshFilter GetOrCreateTemplate()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null && !AssetDatabase.CopyAsset(SourcePrefabPath, PrefabPath))
        {
            Debug.LogError($"[FrameSceneBuilder] {SourcePrefabPath} を複製できませんでした");
            return null;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!root.TryGetComponent<HandGrabInteractable>(out _))
            {
                var grabbable = root.GetComponent<Grabbable>();
                var body = root.GetComponent<Rigidbody>();

                var interactable = root.AddComponent<HandGrabInteractable>();
                var so = new SerializedObject(interactable);
                so.FindProperty("_pointableElement").objectReferenceValue = grabbable;
                so.FindProperty("_rigidbody").objectReferenceValue = body;
                so.FindProperty("_handAligment").enumValueIndex = (int)HandAlignType.None;
                so.ApplyModifiedPropertiesWithoutUndo();

                root.name = "GrabbableCopy";
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<MeshFilter>();
    }
}
