using System;
using SpatialSearch;
using UnityEngine;
using WebSocketSharpUtl;

// SearchClient の通信路を WebSocket で実装するクラス。WebSocketSharpUtl の WebSocketClientManager を使う。
// SearchClient と同じ GameObject に付ける（WebSocketClientManager は自動で付く）。
// URL（ws://IP:ポート/）の組み立てもここで行うので、ポートは Inspector のこちらで変える。
[RequireComponent(typeof(WebSocketClientManager))]
public class WebSocketSearchTransport : SearchTransportBase
{
    [SerializeField] int _serverPort = 8765;

    WebSocketClientManager _webSocket;

    public override bool IsConnected => _webSocket.IsConnected;

    void Awake()
    {
        _webSocket = GetComponent<WebSocketClientManager>();
    }

    // WebSocketのイベントは別スレッドから来る。メインスレッドへ戻すのは SearchClient が行う。
    void OnEnable()
    {
        _webSocket.OnConnected += OnSocketConnected;
        _webSocket.OnDisconnected += OnSocketDisconnected;
        _webSocket.OnMessageReceived += NotifyMessage;
    }

    void OnDisable()
    {
        _webSocket.OnConnected -= OnSocketConnected;
        _webSocket.OnDisconnected -= OnSocketDisconnected;
        _webSocket.OnMessageReceived -= NotifyMessage;
    }

    void OnSocketConnected()
    {
        NotifyConnected(true);
    }

    // 接続に失敗したときも OnDisconnected が来る。
    void OnSocketDisconnected(string reason)
    {
        NotifyConnected(false);
    }

    public override void Connect(string serverIp)
    {
        var url = $"ws://{serverIp}:{_serverPort}/";
        Debug.Log($"[WebSocketSearchTransport] connecting to {url}");
        _webSocket.Disconnect();
        _webSocket.Connect(url);
    }

    public override void Reconnect()
    {
        _webSocket.ReConnect();
    }

    public override void Send(string message, Action<bool> completed)
    {
        // WebSocketClientManager は未接続だと何も呼ばないので、結果は必ず返す約束をここで守る。
        if (!_webSocket.IsConnected)
        {
            completed?.Invoke(false);
            return;
        }
        _webSocket.SendWebSocketMessage(message, completed);
    }
}
