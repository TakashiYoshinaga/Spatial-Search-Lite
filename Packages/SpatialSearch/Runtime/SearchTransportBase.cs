// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using R3;
using UnityEngine;

namespace SpatialSearch
{
    // サーバーとの通信路の共通の入り口。SearchClient はこれを通して、サーバーとJSONの文字列をやり取りする。
    // 文字列を送受信できれば WebSocket でも HTTP でもよいので、通信の方法は使う側のプロジェクトで継承して実装する。
    //
    // 継承するクラスがすること
    //   IsConnected / Connect / Reconnect / Send を実装する
    //   接続できたら NotifyConnected(true)、切れたら（接続に失敗したときも）NotifyConnected(false) を呼ぶ
    //   サーバーから文字列を受け取ったら NotifyMessage(文字列) を呼ぶ
    // Notify は別スレッドから呼んでもよい。メインスレッドへ戻すのは SearchClient が行う。
    public abstract class SearchTransportBase : MonoBehaviour
    {
        readonly Subject<bool> _connectedSubject = new Subject<bool>();
        readonly Subject<string> _messageSubject = new Subject<string>();

        // 接続の状態が変わったとき（true: 接続できた、false: 切れた・接続に失敗した）
        public Observable<bool> ConnectedObservable => _connectedSubject;

        // サーバーから文字列を受け取ったとき
        public Observable<string> MessageObservable => _messageSubject;

        // 今つながっているか
        public abstract bool IsConnected { get; }

        // 指定したIPのサーバーへ接続する。接続中なら切断してから繋ぎ直す。
        public abstract void Connect(string serverIp);

        // 前回と同じサーバーへ繋ぎ直す。
        public abstract void Reconnect();

        // 文字列を送る。送れたかどうかは completed で知らせる（別スレッドから呼んでもよい）。
        public abstract void Send(string message, Action<bool> completed);

        protected void NotifyConnected(bool connected)
        {
            _connectedSubject.OnNext(connected);
        }

        protected void NotifyMessage(string message)
        {
            _messageSubject.OnNext(message);
        }
    }
}
