// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using WebSocketSharp;
namespace WebSocketSharpUtl{
    // WebSocketクライアント機能を提供するクラス
    // サーバーに接続してメッセージの送受信を行う
    public class WebSocketClientManager : MonoBehaviour
    {
        private WebSocket _ws; // WebSocketの接続を管理するオブジェクト
        private bool _isConnected = false; // 現在の接続状態を保持するフラグ


        // 外部のスクリプトからイベントハンドラを登録できるようにする（カスタムイベント）
        public event Action OnConnected; // サーバーに接続成功した時に発火
        public event Action<string> OnMessageReceived; // テキストメッセージを受信した時に発火
        public event Action<byte[]> OnBinaryReceived; // バイナリデータを受信した時に発火
        public event Action<string> OnDisconnected; // サーバーから切断された時に発火
        public event Action<string> OnErrorOccurred; // エラーが発生した時に発火
        

        // 現在の接続状態を取得するプロパティ（読み取り専用）
        // true: 接続中、false: 未接続
        public bool IsConnected{
            get{
                // WebSocketオブジェクトが存在し、かつ接続フラグが立っているかを確認
                // 注: _ws.IsAliveは毎回サーバーに確認を送るため処理が重いので使用しない
                return _ws != null && _isConnected;
            }
        }

        // WebSocketサーバーに接続するメソッド
        // url: 接続先のサーバーURL（例: ws://192.168.1.10:8080/ws）
        public void Connect(string url)
        {
            // すでに接続中または接続オブジェクトが存在する場合は、重複接続を防ぐため何もしない
            if(_ws!=null) return;
            // WebSocketオブジェクトを作成し、指定されたURLへの接続準備を行う
            _ws = new WebSocket(url);

            // 接続が確立された時のイベントハンドラを設定
            _ws.OnOpen += (sender, e) =>
            {
                Debug.Log("Connected to WebSocket server.");
                _isConnected = true; // 接続フラグをtrueに設定
                OnConnected?.Invoke(); // 外部で登録されたイベントハンドラを実行（?. は null チェック付き呼び出し）
            };

            // サーバーからメッセージを受信した時のイベントハンドラを設定
            _ws.OnMessage += (sender, e) =>
            {
                if (e.IsText)
                {
                    OnMessageReceived?.Invoke(e.Data); // テキストデータのみ渡す
                    return;
                }

                if (e.IsBinary)
                {
                    OnBinaryReceived?.Invoke(e.RawData); // バイナリデータのみ渡す
                }
            };

            // サーバーから切断された時のイベントハンドラを設定
            _ws.OnClose += (sender, e) =>
            {
                Debug.Log($"Disconnected from WebSocket server. Reason: {e.Reason}");
                _isConnected = false; // 接続フラグをfalseに設定
                OnDisconnected?.Invoke(e.Reason); // 切断理由を外部のイベントハンドラに渡す
            };

            // エラーが発生した時のイベントハンドラを設定
            _ws.OnError += (sender, e) =>
            {
                Debug.LogError($"WebSocket Error: {e.Message}");
                OnErrorOccurred?.Invoke(e.Message); // エラーメッセージを外部のイベントハンドラに渡す
            };
            
            // 非同期でサーバーへの接続を開始（Unityのメインスレッドをブロックしない）
            _ws.ConnectAsync();
        }

        // サーバーに再接続するメソッド
        // 一度切断してから再度接続を試みる
        public void ReConnect(){
            // WebSocketオブジェクトが存在しない場合は何もしない
            if(_ws==null){ 
                return;
            }   
            else
            {
                _ws.Close(); // 既存の接続を切断
                _isConnected = false; // 接続フラグをfalseに設定
                _ws.ConnectAsync(); // 再度接続を試みる
            }
        }

        // テキストメッセージをサーバーに送信するメソッド
        // message: 送信するテキストデータ
        // callback: 送信完了時に呼ばれるコールバック関数（成功: true、失敗: false）
        public void SendWebSocketMessage(string message, Action<bool> callback)
        {
            // 接続中の場合のみ送信処理を行う
            if (_ws != null && _isConnected)
            {
                // 非同期でメッセージを送信し、完了時にコールバックを実行
                _ws.SendAsync(message, (completed) =>
                {
                    callback?.Invoke(completed);
                });
            }
        }

        // バイナリデータをサーバーに送信するメソッド
        // data: 送信するバイナリデータ（画像、音声などのバイト配列）
        // callback: 送信完了時に呼ばれるコールバック関数（成功: true、失敗: false）
        public void SendWebSocketBinary(byte[] data, Action<bool> callback)
        {
            // 接続中の場合のみ送信処理を行う
            if (_ws != null && _isConnected)
            {
                _ws.SendAsync(data, (completed) =>
                {
                    callback?.Invoke(completed);
                });
            }else
            {
                callback?.Invoke(false); // 送信できない場合は失敗を通知
            }
        }

        // サーバーから切断するメソッド
        // 別のURLへ接続し直すときは、これを呼んでからConnectする
        public void Disconnect()
        {
            CloseWebSocket();
        }

        // アプリケーション終了時に自動的に呼ばれるメソッド
        void OnApplicationQuit()
        {
            // WebSocket接続が存在する場合は、正常に切断処理を行う
            CloseWebSocket();
        }

        // 共通のクローズ処理を切り出し
        private void CloseWebSocket()
        {
            if (_ws != null)
            {
                try
                {
                    _ws.Close();
                    Debug.Log("WebSocket connection closed.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error closing WebSocket: {e.Message}");
                }
                _ws = null;
                _isConnected = false;
            }
        }

#if UNITY_EDITOR
        // エディタでPlayモードが停止されたときに呼ばれるハンドラを登録
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            CloseWebSocket();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                CloseWebSocket();
            }
        }
#else
        private void OnDisable()
        {
            CloseWebSocket();
        }
#endif
    }
}
