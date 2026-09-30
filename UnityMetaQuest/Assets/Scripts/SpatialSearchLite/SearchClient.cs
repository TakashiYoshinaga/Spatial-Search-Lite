using System;
using R3;
using UnityEngine;
using WebSocketSharpUtl;

// サーバーとやり取りするクラス。
// 撮影データが流れてきたら画像と検索語をWebSocketで送り、返ってきたマスクを DataSource へ流す。
[RequireComponent(typeof(WebSocketClientManager))]
public class SearchClient : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("Pythonサーバーを動かしているPCのIPアドレス")]
    [SerializeField] string _serverIp = "192.168.0.10";
    [SerializeField] int _serverPort = 8765;

    SpatialSearchDataSource _dataSource;
    WebSocketClientManager _webSocket;

    // 結果待ちの撮影データ。null でなければ送信中なので、次の撮影は無視する。
    CaptureData _pending;

    public void Initialize(SpatialSearchDataSource dataSource)
    {
        _dataSource = dataSource;
        if (!TryGetComponent(out _webSocket)) _webSocket = gameObject.AddComponent<WebSocketClientManager>();

        // WebSocketのイベントは別スレッドから来るので、メインスレッドへ戻してから扱う。
        Observable.FromEvent<string>(h => _webSocket.OnMessageReceived += h, h => _webSocket.OnMessageReceived -= h)
            .ObserveOnMainThread()
            .Subscribe(OnMessage)
            .AddTo(this);

        Observable.FromEvent<string>(h => _webSocket.OnDisconnected += h, h => _webSocket.OnDisconnected -= h)
            .ObserveOnMainThread()
            .Subscribe(_ => Fail("サーバーとの接続が切れました"))
            .AddTo(this);

        _dataSource.CaptureObservable
            .Subscribe(Send)
            .AddTo(this);

        var url = $"ws://{_serverIp}:{_serverPort}/";
        Debug.Log($"[SearchClient] connecting to {url}");
        _webSocket.Connect(url);
    }

    void Send(CaptureData capture)
    {
        if (_pending != null)
        {
            Debug.LogWarning("[SearchClient] 前の検索の結果待ちなので無視します");
            return;
        }
        if (!_webSocket.IsConnected)
        {
            Debug.LogWarning("[SearchClient] サーバーに接続していません。再接続します");
            _webSocket.ReConnect();
            return;
        }

        var request = new SearchRequestMessage
        {
            query = capture.Query,
            image = Convert.ToBase64String(capture.Image.EncodeToPNG()),
        };

        _pending = capture;
        _webSocket.SendWebSocketMessage(JsonUtility.ToJson(request), completed =>
        {
            if (!completed) Debug.LogWarning("[SearchClient] 送信に失敗しました");
        });
    }

    void OnMessage(string json)
    {
        if (_pending == null) return;

        SearchResponseMessage response;
        try
        {
            response = JsonUtility.FromJson<SearchResponseMessage>(json);
        }
        catch (Exception e)
        {
            Fail("サーバーの応答を読めませんでした: " + e.Message);
            return;
        }

        if (response.type != "result")
        {
            Fail(response.message);
            return;
        }

        var result = new SearchResult { Capture = _pending };
        if (response.detections != null)
        {
            foreach (var detection in response.detections)
            {
                var mask = new Texture2D(2, 2);
                mask.LoadImage(Convert.FromBase64String(detection.mask)); // PNGを読むとサイズも合わせて変わる
                result.Detections.Add(new Detection { Score = detection.score, Mask = mask });
            }
        }

        _pending = null;
        _dataSource.SetResult(result);
    }

    void Fail(string message)
    {
        if (_pending == null) return;

        Debug.LogWarning("[SearchClient] " + message);
        var result = new SearchResult { Capture = _pending, Error = message };
        _pending = null;
        _dataSource.SetResult(result);
    }
}
