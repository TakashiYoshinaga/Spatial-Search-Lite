using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
namespace WebSocketSharpUtl{
    public class WebSocketServerManager: MonoBehaviour
    {
        [SerializeField] private int _port = 8080;//デフォルトのポート番号。必要に応じて変更してください
        [SerializeField] private string _roomName = "gps_transfer";
        private WebSocketServer _wss;
        private bool _isServerReady = false;
        public bool IsServerReady{
            get{
                return _isServerReady;
            }
        }
        private string _url;
        public string ServerURL{
            get{
                return _url;
            }
        }

        void Start()
        {
            // このPCのローカルIPアドレスを取得（他のデバイスから接続するために必要。正しく取得できない場合は直打ちでIPを指定してみてください）
            string ipAddress = GetLocalIPAddress();
            // WebSocketサーバーのURLを作成（例: ws://192.168.1.10:8080）
            string serverAddress = $"ws://{ipAddress}:{_port}";
            // WebSocketサーバーのインスタンスを作成して起動準備
            _wss = new WebSocketServer(serverAddress);
            // 指定したルーム名（パス）でWebSocketサービスを登録
            _wss.AddWebSocketService<SimpleWebSocketServer>($"/{_roomName}");
            // サーバーを起動し、クライアントからの接続を待ち受ける
            _wss.Start();
            Debug.Log($"WebSocket server started. Please connect to: {serverAddress}/{_roomName}");
            _url = $"{serverAddress}/{_roomName}";
            _isServerReady = true;
        }
        // このPCのローカルIPアドレス（例: 192.168.1.10）を取得するメソッド
        // 同じネットワーク内の他のデバイスから接続する際に必要
        private string GetLocalIPAddress()
        {
            string localIP = "127.0.0.1"; // デフォルト値（取得失敗時に使用）
            try
            {
                // ダミーの外部接続を行うことで、このPCが使用しているネットワークインターフェースのIPアドレスを取得
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    // GoogleのDNSサーバー（8.8.8.8）への接続を試みることで、ローカルIPを特定
                    socket.Connect("8.8.8.8", 65530); // GoogleのDNSを使用してローカルIPを取得
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIP = endPoint.Address.ToString();
                }
            }
            catch
            {
                Debug.LogError("Failed to get local IP address.");
            }
            Debug.Log($"Local IP address: {localIP}");
            return localIP;
        }
    
        // アプリケーション終了時に自動的に呼ばれるメソッド
        void OnApplicationQuit()
        {
            // サーバーが起動している場合は、正常に停止処理を行う
            if (_wss != null)
            {
                _wss.Stop();
                Debug.Log("WebSocket server stopped.");
            }
        }
    }

    // WebSocketの動作を定義するクラス（クライアントからの接続やメッセージを処理）
    public class SimpleWebSocketServer : WebSocketBehavior
    {
        // 現在接続している全クライアントのリスト（static = 全インスタンスで共有）
        private static List<SimpleWebSocketServer> _clients = new List<SimpleWebSocketServer>();

        // クライアントが接続してきた時に自動的に呼ばれるメソッド
        protected override void OnOpen()
        {
            // 複数のクライアントが同時に接続する可能性があるため、リストへの追加をロック
            lock (_clients)
            {
                _clients.Add(this);
            }
            Debug.Log($"Client connected: {ID}");
        }

        // クライアントからメッセージを受信した時に自動的に呼ばれるメソッド
        protected override void OnMessage(MessageEventArgs e)
        {
            // バイナリデータ（画像、音声など）の場合
            if(e.IsBinary){
                Debug.Log($"Received binary data: {e.RawData.Length} bytes");
                // 受信したバイナリデータを、接続している他の全クライアントに転送
                lock (_clients)
                {
                    foreach (var client in _clients)
                    {
                        // 送信元のクライアントには送り返さない（エコーバック防止）
                        if (client != this)
                        {
                            client.Send(e.RawData);
                        }
                    }
                }

            }else{
                // テキストデータ（文字列）の場合
                Debug.Log($"Received data: {e.Data}");

                // 受信したテキストデータを、接続している他の全クライアントに転送（ブロードキャスト）
                lock (_clients)
                {
                    foreach (var client in _clients)
                    {
                        // 送信元のクライアントには送り返さない（エコーバック防止）
                        if (client != this)
                        {
                            client.Send(e.Data);
                        }
                    }
                }
            }
        }

        // クライアントが切断した時に自動的に呼ばれるメソッド
        protected override void OnClose(CloseEventArgs e)
        {
            // 接続リストから、切断したクライアントを削除
            lock (_clients)
            {
                _clients.Remove(this);
            }
            Debug.Log($"Client disconnected: {ID}");
        }
    }
}
