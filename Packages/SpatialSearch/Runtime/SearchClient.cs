using System;
using R3;
using UnityEngine;

namespace SpatialSearch
{
    // サーバーとやり取りするクラス。
    // 撮影データが流れてきたら画像と検索語（点で探すときは画像上の点）をJSONにして通信路で送り、返ってきたマスクを DataSource へ流す。
    // 通信そのもの（WebSocketなど）は、SearchTransportBase を継承したクラスが受け持つ。
    public class SearchClient : MonoBehaviour
    {
        [Tooltip("サーバーとの通信路。空なら同じ GameObject から探す")]
        [SerializeField] SearchTransportBase _transport;

        SpatialSearchDataSource _dataSource;

        // 結果待ちの撮影データ。null でなければ送信中なので、次の撮影は無視する。
        CaptureData _pending;

        // 送信の失敗は別スレッドから知らされることがあるので、メインスレッドへ戻すために一度ここを通す。
        readonly Subject<string> _sendFailedSubject = new Subject<string>();

        public void Initialize(SpatialSearchDataSource dataSource)
        {
            _dataSource = dataSource;
            if (_transport == null && !TryGetComponent(out _transport))
            {
                Debug.LogError("[SearchClient] 通信路がありません。同じ GameObject に SearchTransportBase を継承したコンポーネントを付けるか、Inspector の Transport に設定してください", this);
                return;
            }

            // 通信路のイベントは別スレッドから来ることがあるので、メインスレッドへ戻してから扱う。
            _transport.MessageObservable
                .ObserveOnMainThread()
                .Subscribe(OnMessage)
                .AddTo(this);

            // 接続に失敗したときも false が来る。
            _transport.ConnectedObservable
                .ObserveOnMainThread()
                .Subscribe(connected =>
                {
                    _dataSource.SetConnected(connected);
                    if (!connected) Fail("Disconnected from the server");
                })
                .AddTo(this);

            _sendFailedSubject
                .ObserveOnMainThread()
                .Subscribe(Fail)
                .AddTo(this);

            _dataSource.CaptureObservable
                .Subscribe(Send)
                .AddTo(this);
        }

        // 指定したIPのサーバーへ接続する。接続中なら切断してから繋ぎ直す。
        public void Connect(string serverIp)
        {
            if (_transport == null) return; // 通信路が無いことは Initialize で知らせてある
            _transport.Connect(serverIp);
        }

        void Send(CaptureData capture)
        {
            if (_pending != null)
            {
                Debug.LogWarning("[SearchClient] 前の検索の結果待ちなので無視します");
                Destroy(capture.Image); // 結果にならない撮影の画像は、誰も使わないのでここで捨てる
                return;
            }
            if (!_transport.IsConnected)
            {
                _dataSource.SetResult(new SearchResult { Capture = capture, Error = "Not connected to the server" });
                _transport.Reconnect();
                return;
            }

            _pending = capture;
            _transport.Send(RequestJson(capture), completed =>
            {
                if (!completed) _sendFailedSubject.OnNext("Failed to send the request");
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
}
