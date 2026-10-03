using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// SpatialSearchLite / SpatialSearchLiteGrab シーンのUIを組み立てるメニュー。足りないものだけを作るので、何度実行してもよい。
//
//   ControllerPanel : 検索ウィンドウの開閉ボタン(Search) + 状態表示（左コントローラ）
//   SearchWindow    : 検索語の入力欄 + Search ボタン（頭部追従）
//   ServerDialog    : サーバーIPの入力欄 + Connect ボタン（頭部追従）
//
// レイで操作できるCanvas（RayInteractable / PointableCanvas / Surface）は設定が多いので、
// 左コントローラのCanvasを複製して使い回す。最後に MainCoordinator と SearchResultView へ参照を設定する。
public static class SearchUiBuilder
{
    const string OldInputName = "SerchWordInputField (TMP)";
    const float CanvasScale = 0.0007f; // 1px = 0.7mm
    const int UiLayer = 5;

    [MenuItem("Tools/Spatial Search Lite/Build UI")]
    static void Build()
    {
        var coordinator = Object.FindAnyObjectByType<MainCoordinator>();
        var resultView = Object.FindAnyObjectByType<SearchResultViewBase>();
        var controllerPanel = GetOrCreateControllerPanel();
        if (coordinator == null || resultView == null || controllerPanel == null)
        {
            Debug.LogError("[SearchUiBuilder] MainCoordinator / SearchResultView / 左コントローラのCanvas が見つかりません");
            return;
        }

        // ---- コントローラのパネル ----
        SetSize(controllerPanel, new Vector2(260, 130));
        GetOrAddBackground(controllerPanel);
        DestroyChild(controllerPanel.transform, "ServerButton"); // 接続の入り口は起動時のダイアログだけ
        var toggleButton = GetOrCreateButton(controllerPanel.transform, "ToggleSearchWindowButton", "Search", new Vector2(0, 30), new Vector2(220, 56));
        var statusText = GetOrCreateLabel(controllerPanel.transform, "StatusText", "", new Vector2(0, -35), new Vector2(250, 50), 18);

        // ---- 検索ウィンドウ ----
        var searchWindow = GetOrCreateWindow("SearchWindow", controllerPanel);
        DestroyChild(searchWindow.transform, "ServerButton");
        GetOrCreateLabel(searchWindow.transform, "Title", "Search", new Vector2(0, 80), new Vector2(440, 40), 28);
        var queryInput = GetOrCreateInputField(searchWindow.transform, "SearchWordInputField (TMP)", "Search word (e.g. monitor)", 10);
        var searchButton = GetOrCreateButton(searchWindow.transform, "SearchButton", "Search", new Vector2(0, -65), new Vector2(220, 56));

        // ---- サーバーIPダイアログ ----
        var serverDialog = GetOrCreateWindow("ServerDialog", controllerPanel);
        GetOrCreateLabel(serverDialog.transform, "Title", "Server IP address", new Vector2(0, 80), new Vector2(440, 40), 28);
        var serverIpInput = GetOrCreateInputField(serverDialog.transform, "ServerIpInputField (TMP)", "e.g. 192.168.0.10", 10);
        var connectButton = GetOrCreateButton(serverDialog.transform, "ConnectButton", "Connect", new Vector2(0, -65), new Vector2(220, 56));

        foreach (var root in new[] { searchWindow, serverDialog, controllerPanel })
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;
        }

        // ---- 参照を設定 ----
        var so = new SerializedObject(coordinator);
        so.FindProperty("_serverDialog").objectReferenceValue = serverDialog;
        so.FindProperty("_serverIpInput").objectReferenceValue = serverIpInput;
        so.FindProperty("_connectButton").objectReferenceValue = connectButton;
        so.FindProperty("_searchWindow").objectReferenceValue = searchWindow;
        so.FindProperty("_queryInput").objectReferenceValue = queryInput;
        so.FindProperty("_searchButton").objectReferenceValue = searchButton;
        so.FindProperty("_toggleSearchWindowButton").objectReferenceValue = toggleButton;
        so.ApplyModifiedProperties();

        var viewSo = new SerializedObject(resultView);
        viewSo.FindProperty("_statusText").objectReferenceValue = statusText;
        viewSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(coordinator.gameObject.scene);
        Selection.activeGameObject = searchWindow;
        Debug.Log("[SearchUiBuilder] UIを作成しました。確認してからシーンを保存してください");
    }

    // 左コントローラのCanvasを ControllerPanel として使う。
    // 初回は古い入力欄のCanvasを改名し、入力欄は検索ウィンドウ側で作り直すので消す。
    static GameObject GetOrCreateControllerPanel()
    {
        var panel = FindInScene("ControllerPanel");
        if (panel != null) return panel;

        var oldInput = FindInScene(OldInputName);
        if (oldInput == null) return null;

        panel = oldInput.GetComponentInParent<Canvas>().gameObject;
        panel.name = "ControllerPanel";
        Object.DestroyImmediate(oldInput);
        return panel;
    }

    // ControllerPanel を複製し、レイの当たり面（Surface）以外の中身を消してウィンドウにする。
    static GameObject GetOrCreateWindow(string name, GameObject template)
    {
        var window = FindInScene(name);
        if (window == null)
        {
            window = Object.Instantiate(template);
            window.name = name;
            for (var i = window.transform.childCount - 1; i >= 0; i--)
            {
                var child = window.transform.GetChild(i);
                if (child.name != "Surface") Object.DestroyImmediate(child.gameObject);
            }
            window.transform.SetPositionAndRotation(new Vector3(0, 1.3f, 0.6f), Quaternion.identity);
            window.transform.localScale = Vector3.one * CanvasScale;
        }

        if (window.GetComponent<HeadFollow>() == null) window.AddComponent<HeadFollow>();
        SetSize(window, new Vector2(480, 220));
        GetOrAddBackground(window);
        return window;
    }

    // Canvasと、レイが当たる面（Surface）の大きさを揃える。
    static void SetSize(GameObject canvas, Vector2 size)
    {
        ((RectTransform)canvas.transform).sizeDelta = size;
        var surface = canvas.transform.Find("Surface");
        if (surface != null) surface.localScale = new Vector3(size.x, size.y, 20);
    }

    static void GetOrAddBackground(GameObject canvas)
    {
        if (canvas.transform.Find("Background") != null) return;

        var go = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(canvas.transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = new Color(0.1f, 0.1f, 0.12f, 0.9f);
    }

    static TMP_InputField GetOrCreateInputField(Transform parent, string name, string placeholder, float y)
    {
        var existing = parent.Find(name);
        var field = existing != null ? existing.GetComponent<TMP_InputField>() : null;
        if (field == null)
        {
            var go = TMP_DefaultControls.CreateInputField(DefaultResources());
            go.name = name;
            go.transform.SetParent(parent, false);
            field = go.GetComponent<TMP_InputField>();
            field.pointSize = 32;
        }

        var rect = (RectTransform)field.transform;
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(1, 0.5f);
        rect.sizeDelta = new Vector2(-40, 52);
        rect.anchoredPosition = new Vector2(0, y);
        if (field.placeholder is TMP_Text text) text.text = placeholder;
        field.caretWidth = 5; // 既定の1pxだとVRでは細すぎて見えない
        field.customCaretColor = true;
        field.caretColor = Color.black;
        return field;
    }

    static Button GetOrCreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size)
    {
        var existing = parent.Find(name);
        var button = existing != null ? existing.GetComponent<Button>() : null;
        if (button == null)
        {
            var go = TMP_DefaultControls.CreateButton(DefaultResources());
            go.name = name;
            go.transform.SetParent(parent, false);
            button = go.GetComponent<Button>();
        }

        var rect = (RectTransform)button.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var text = button.GetComponentInChildren<TMP_Text>();
        text.text = label;
        text.fontSize = 24;
        return button;
    }

    static TMP_Text GetOrCreateLabel(Transform parent, string name, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var existing = parent.Find(name);
        var text = existing != null ? existing.GetComponent<TMP_Text>() : null;
        if (text == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            text = go.GetComponent<TMP_Text>();
            text.text = value;
        }

        var rect = (RectTransform)text.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    static void DestroyChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    static GameObject FindInScene(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == name) return t.gameObject;
        }
        return null;
    }

    static TMP_DefaultControls.Resources DefaultResources()
    {
        return new TMP_DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
        };
    }
}
