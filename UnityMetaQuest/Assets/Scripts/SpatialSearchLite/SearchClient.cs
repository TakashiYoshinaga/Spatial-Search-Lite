using System;
using R3;
using UnityEngine;
using WebSocketSharpUtl;

// サーバーとやり取りするクラス。
// 撮影データが流れてきたら画像と検索語をWebSocketで送り、返ってきたマスクを DataSource へ流す。
[RequireComponent(typeof(WebSocketClientManager))]
public class SearchClient : MonoBehaviour
{
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

        Observable.FromEvent(h => _webSocket.OnConnected += h, h => _webSocket.OnConnected -= h)
            .ObserveOnMainThread()
            .Subscribe(_ => _dataSource.SetConnected(true))
            .AddTo(this);

        // 接続に失敗したときも OnDisconnected が来る。
        Observable.FromEvent<string>(h => _webSocket.OnDisconnected += h, h => _webSocket.OnDisconnected -= h)
            .ObserveOnMainThread()
            .Subscribe(_ =>
            {
                _dataSource.SetConnected(false);
                Fail("Disconnected from the server");
            })
            .AddTo(this);

        _dataSource.CaptureObservable
            .Subscribe(Send)
            .AddTo(this);
    }

    // 指定したIPのサーバーへ接続する。接続中なら切断してから繋ぎ直す。
    public void Connect(string serverIp)
    {
        var url = $"ws://{serverIp}:{_serverPort}/";
        Debug.Log($"[SearchClient] connecting to {url}");
        _webSocket.Disconnect();
        _webSocket.Connect(url);
    }

    void Send(CaptureData capture)
    {
        if (_pending != null)
        {
            Debug.LogWarning("[SearchClient] 前の検索の結果待ちなので無視します");
            Destroy(capture.Image); // 結果にならない撮影の画像は、誰も使わないのでここで捨てる
            return;
        }
        if (!_webSocket.IsConnected)
        {
            _dataSource.SetResult(new SearchResult { Capture = capture, Error = "Not connected to the server" });
            _webSocket.ReConnect();
            return;
        }

        _pending = capture;
        _webSocket.SendWebSocketMessage(RequestJson(capture), completed =>
        {
            if (!completed) Debug.LogWarning("[SearchClient] 送信に失敗しました");
        });
    }

    // 点が指定されていれば点で、なければ検索語で探すリクエストにする。
    static string RequestJson(CaptureData capture)
    {
        var image = Convert.ToBase64String(capture.Image.EncodeToPNG());
        if (!capture.HasPoint)
        {
            return JsonUtility.ToJson(new SearchRequestMessage { query = capture.Query, image = image });
        }

        // UVは左下原点、PNGのピクセルは左上原点なので上下を反転する。
        var width = capture.Image.width;
        var height = capture.Image.height;
        return JsonUtility.ToJson(new PointRequestMessage
        {
            x = Mathf.Clamp((int)(capture.PointUv.x * width), 0, width - 1),
            y = Mathf.Clamp((int)((1f - capture.PointUv.y) * height), 0, height - 1),
            image = image,
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
            Fail("Invalid response from the server: " + e.Message);
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
