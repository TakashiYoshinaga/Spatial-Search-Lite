using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WebSocketSharpUtl;

// [MainCoordinator] に付いているロジックのコンポーネントを Logic の子へ引っ越す一度きりのメニュー。
//   SearchCapture                      → Logic/Search
//   SearchClient + WebSocketClientManager → Logic/WebSocket
// 設定値はそのままコピーし、MainCoordinator の参照も付け替える。Undoで戻せる。
public static class LogicComponentMover
{
    [MenuItem("Tools/Spatial Search Lite/Move Logic Components")]
    static void Move()
    {
        var coordinator = Object.FindAnyObjectByType<MainCoordinator>();
        var searchObject = FindChild("Logic", "Search");
        var webSocketObject = FindChild("Logic", "WebSocket");
        if (coordinator == null || searchObject == null || webSocketObject == null)
        {
            Debug.LogError("[LogicComponentMover] MainCoordinator / Logic/Search / Logic/WebSocket が見つかりません");
            return;
        }

        Undo.SetCurrentGroupName("Move Logic Components");
        var source = coordinator.gameObject;

        // ---- SearchCapture ----
        var capture = source.GetComponent<SearchCapture>();
        if (capture != null)
        {
            var moved = Undo.AddComponent<SearchCapture>(searchObject);
            EditorUtility.CopySerialized(capture, moved);
            Undo.DestroyObjectImmediate(capture);
        }

        // ---- SearchClient + WebSocketClientManager ----
        // SearchClient を足すと RequireComponent で WebSocketClientManager も付くので、そちらへ値を写す。
        var client = source.GetComponent<SearchClient>();
        var webSocket = source.GetComponent<WebSocketClientManager>();
        if (client != null)
        {
            var movedClient = Undo.AddComponent<SearchClient>(webSocketObject);
            EditorUtility.CopySerialized(client, movedClient);

            var movedWebSocket = webSocketObject.GetComponent<WebSocketClientManager>();
            if (webSocket != null && movedWebSocket != null) EditorUtility.CopySerialized(webSocket, movedWebSocket);

            // SearchClient が WebSocketClientManager を必要とするので、SearchClient から先に消す。
            Undo.DestroyObjectImmediate(client);
            if (webSocket != null) Undo.DestroyObjectImmediate(webSocket);
        }

        // ---- MainCoordinator の参照を付け替える ----
        var so = new SerializedObject(coordinator);
        so.FindProperty("_capture").objectReferenceValue = searchObject.GetComponent<SearchCapture>();
        so.FindProperty("_client").objectReferenceValue = webSocketObject.GetComponent<SearchClient>();
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(coordinator.gameObject.scene);
        Debug.Log("[LogicComponentMover] 引っ越しました。Inspectorを確認してからシーンを保存してください");
    }

    static GameObject FindChild(string parentName, string childName)
    {
        var parent = GameObject.Find(parentName);
        if (parent == null) return null;
        var child = parent.transform.Find(childName);
        return child != null ? child.gameObject : null;
    }
}
