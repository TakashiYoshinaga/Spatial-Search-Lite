using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 3つのクラスに同じ DataSource を渡してつなぎ、UIの操作もここで受け持つ。
//   SearchCapture    : Capture(query) で撮影 → DataSource.SetCapture
//   SearchClient     : 撮影データをサーバーへ送る → 結果を DataSource.SetResult
//   SearchResultView : 結果の点群を表示
//
// UIの流れ
//   起動 → サーバーIPダイアログ（前回のIPを入れておく）→ Connect → 接続できたら閉じる
//   コントローラの Search → 検索ウィンドウの表示/非表示（ウィンドウは頭に追従）
//   検索ウィンドウの Search → 撮影して検索し、ウィンドウを閉じる
//
// デモなので、接続の入り口は起動直後のダイアログだけにしている。
// 接続できなかった場合はダイアログが開いたまま残るので、IPを直して Connect し直せる。
public class MainCoordinator : MonoBehaviour
{
    const string ServerIpKey = "SpatialSearchLite.ServerIp";

    [Header("Logic")]
    [SerializeField] SearchCapture _capture;
    [SerializeField] SearchClient _client;
    [SerializeField] SearchResultView _resultView;

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

    SpatialSearchDataSource _dataSource = new SpatialSearchDataSource();

    void Awake()
    {
        // ResultView は Client より先に初期化する。
        // 未接続のとき Client は撮影を受け取った直後にエラー結果を流すので、
        // ResultView の「検索中」表示がエラー表示を上書きしないようにするため。
        _capture.Initialize(_dataSource);
        _resultView.Initialize(_dataSource);
        _client.Initialize(_dataSource);

        if (_serverDialog == null || _serverIpInput == null || _connectButton == null ||
            _searchWindow == null || _queryInput == null || _searchButton == null ||
            _toggleSearchWindowButton == null)
        {
            Debug.LogError("[MainCoordinator] UIの参照が設定されていません。Tools > Spatial Search Lite > Build UI を実行してシーンを保存してください", this);
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
}
