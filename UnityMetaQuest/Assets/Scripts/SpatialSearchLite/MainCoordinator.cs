using R3;
using SpatialSearch;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 3つのクラスに同じ DataSource を渡してつなぎ、UIの操作もここで受け持つ。
//   SearchCapture    : Capture(query) で撮影 → DataSource.SetCapture
//   SearchClient     : 撮影データをサーバーへ送る → 結果を DataSource.SetResult
//   SearchResultView : 結果の点群を表示（つかめる版は GrabbableSearchResultView）
//   FrameCopyController : 手の枠で指した物体を CaptureAt で撮影する（SpatialSearchLiteFrame シーンのみ）
//
// UIの流れ
//   起動 → サーバーIPダイアログ（前回のIPを入れておく）→ Connect → 接続できたら閉じる
//   コントローラの Search → 検索ウィンドウの表示/非表示（ウィンドウは頭に追従）
//   検索ウィンドウの Search → 撮影して検索し、ウィンドウを閉じる
//   コントローラの状態表示 → 検索中・見つかった数・エラーを出す（結果を表示するクラスが知らせる数から作る）
//
// デモなので、接続の入り口は起動直後のダイアログだけにしている。
// 接続できなかった場合はダイアログが開いたまま残るので、IPを直して Connect し直せる。
public class MainCoordinator : MonoBehaviour
{
    const string ServerIpKey = "SpatialSearchLite.ServerIp";

    [Header("Logic")]
    [SerializeField] SearchCapture _capture;
    [SerializeField] SearchClient _client;
    [SerializeField] SearchResultViewBase _resultView;
    [Tooltip("手の枠で指して物体を複製する（SpatialSearchLiteFrame シーンのみ。無ければ空のまま）")]
    [SerializeField] FrameCopyController _frameCopy;

    [Header("Server Dialog")]
    [SerializeField] GameObject _serverDialog;
    [SerializeField] TMP_InputField _serverIpInput;
    [SerializeField] Button _connectButton;
    [Tooltip("一度も入力していないときにIP欄へ入れておく値")]
    [SerializeField] string _defaultServerIp = "192.168.0.10";

    [Header("Search Window")]
    [SerializeField] GameObject _searchWindow;
    [SerializeField] TMP_InputField _queryInput;
    [SerializeField] Button _searchButton;

    [Header("Controller")]
    [SerializeField] Button _toggleSearchWindowButton;
    [Tooltip("検索中・結果の文字表示（任意）。日本語フォントが無いので表示文は英語")]
    [SerializeField] TMP_Text _statusText;

    SpatialSearchDataSource _dataSource = new SpatialSearchDataSource();

    void Awake()
    {
        _capture.Initialize(_dataSource);
        _resultView.Initialize(_dataSource);

        // 状態の表示は Client より先に購読する。
        // 未接続のとき Client は撮影を受け取った直後にエラー結果を流すので、
        // 「検索中」の表示がエラーの表示を上書きしないようにするため。
        _dataSource.CaptureObservable
            .Subscribe(capture => SetStatus($"Searching \"{capture.Query}\"..."))
            .AddTo(this);
        _resultView.ShownObservable
            .Subscribe(ShowResultStatus)
            .AddTo(this);

        _client.Initialize(_dataSource);
        if (_frameCopy != null) _frameCopy.Initialize(_dataSource, _capture);

        if (_serverDialog == null || _serverIpInput == null || _connectButton == null ||
            _searchWindow == null || _queryInput == null || _searchButton == null ||
            _toggleSearchWindowButton == null)
        {
            Debug.LogError("[MainCoordinator] UIの参照が設定されていません。Inspector で Server Dialog / Search Window / Controller の項目を設定してください", this);
            return;
        }

        // 入力欄の onSubmit にはつながない。Questではキーボードを閉じただけでも onSubmit が呼ばれ、
        // 入力の途中で勝手に接続・検索が始まってしまうため。進むのはボタンを押したときだけにする。
        _connectButton.onClick.AddListener(OnConnect);
        _searchButton.onClick.AddListener(OnSearch);
        _toggleSearchWindowButton.onClick.AddListener(ToggleSearchWindow);

        // 接続できたらIPダイアログを閉じる。
        _dataSource.ConnectedObservable
            .Where(connected => connected)
            .Subscribe(_ => _serverDialog.SetActive(false))
            .AddTo(this);

        _searchWindow.SetActive(false);
        _serverIpInput.text = PlayerPrefs.GetString(ServerIpKey, _defaultServerIp);
        _serverDialog.SetActive(true);
    }

    void OnConnect()
    {
        var ip = _serverIpInput.text.Trim();
        if (string.IsNullOrEmpty(ip)) return;

        PlayerPrefs.SetString(ServerIpKey, ip);
        PlayerPrefs.Save();

        _client.Connect(ip);
    }

    void ToggleSearchWindow()
    {
        // IPダイアログを出している間は検索ウィンドウを重ねない。
        if (_serverDialog.activeSelf) return;
        _searchWindow.SetActive(!_searchWindow.activeSelf);
    }

    void OnSearch()
    {
        var query = _queryInput.text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        if (_capture.Capture(query))
        {
            _searchWindow.SetActive(false);
        }
    }

    void ShowResultStatus(ShownResult shown)
    {
        var result = shown.Result;
        var query = result.Capture.Query;
        if (!result.IsSuccess)
        {
            SetStatus($"Error: {result.Error}");
            return;
        }
        SetStatus(shown.ObjectCount == 0 ? $"\"{query}\" was not found" : $"Found {shown.ObjectCount} \"{query}\"");
    }

    void SetStatus(string message)
    {
        if (_statusText != null) _statusText.text = message;
    }
}
