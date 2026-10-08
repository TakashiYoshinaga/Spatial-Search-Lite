# 設定・しくみ（開発者向け）

[体験手順に戻る](../README.md) · [つかむ・指の枠でコピーする](interactions.md)

体験に必要な準備と起動手順は README にあります。このページは、設定を調整したり、実装を読んだりするための補足です。

## サーバーのオプション

`python server/server.py` に次のオプションを付けられます。コマンドはリポジトリのルートで実行してください。

| オプション | 既定値 | 内容 |
|---|---|---|
| `--host` | `127.0.0.1` | 待ち受けるアドレス。Questから繋ぐときは `0.0.0.0` |
| `--port` | `8765` | 待ち受けるポート |
| `--comfyui` | `http://127.0.0.1:8188` | ComfyUIのURL |
| `--checkpoint` | `sam3.1_multiplex_fp16.safetensors` | SAM 3.1 のチェックポイント名 |
| `--threshold` | `0.5` | これより自信の低い検出は捨てます。見つからないときは下げてみてください |
| `--max-detections` | `8` | 1回の検索で見つける物体の最大数 |

## 設定できる項目

| 場所（Hierarchy） | 項目 | 内容 |
|---|---|---|
| `[MainCoordinator]` の Main Coordinator | Default Server Ip | 一度もIPを入力していないときにダイアログへ入れておく値 |
| `Logic/Search` の Search Capture | Min / Max Depth Meters | この距離の範囲にある点だけを使います |
| `Logic/WebSocket` の Web Socket Search Transport | Server Port | Pythonサーバーのポート |
| `SearchWindow` / `ServerDialog` の Head Follow | Distance / Height Offset / Follow Speed | ウィンドウを出す距離・高さ、頭に追いつく速さ |
| `Logic/SearchResultView`（0-SpatialSearchLite） | Color / Point Size | 結果の点の色（既定は黄色）と大きさ |
| `Logic/SearchResultView`（1-SpatialSearchLiteGrab） | Outlier Ratio / Min Collider Size | つかめる範囲の箱を決めるときに外す端の点の割合と、箱の最小の大きさ |
| `Packages/SpatialSearch/Materials/SpatialSearchColoredPointsMat`（Project ウィンドウでは Packages > SpatialSearch） | Depth Tolerance | 重なった板を同じ面として描く奥行きの幅（板の大きさの何倍か）。ちらつくときは上げ、後ろの板が透けて見えるときは下げます |
| 〃 | Fade Start / Fade End | 物体の正面（点群から求めた面の向き）と今の視点の向きの内積（1=正面から、0=真横、負=裏側）。Fade Start 以上で不透明、Fade End 以下で完全に透明、その間はなめらかに変えます。もっと横から見ても見えるようにするときは Fade End を下げます |
| `Assets/Prefabs/GrabbableSearchResult` の Interactable Color Visual | Hover / Select Color State | 近づけた・つかんだときに重ねる色（Aが重ねる強さ） |
| `Logic/FrameCopy` の Frame Copy Controller（2-SpatialSearchLiteFrame(Experimental)） | Stable Angle / Lock Seconds | 点がこの角度（度）以内に止まっている時間がこの秒数になると、ロックします |
| 〃 | Hands Gone Seconds / Lower Hands Timeout | 両手がカメラ画像から消えてから撮影するまでの時間と、ロックしてから手を下ろすのを待つ時間（秒） |
| 〃 | Result Timeout | サーバーの結果を待つ時間（秒）。過ぎると次の操作を受け付けます |
| 〃 | Dot Angle / Ring Angle / Aim / Edge / Locked Color | 点とリングの見た目の大きさ（度）と、狙っているとき・縁を指しているとき・ロックしたときの色 |

## しくみ

```text
Quest : カメラ画像 + 深度 を撮影 ──(WebSocket)──▶ PC : Pythonサーバー ──▶ ComfyUI (SAM 3.1)
Quest : マスクに写っていた深度の点を表示 ◀──(WebSocket)── PC : 物体ごとのマスク画像
```

Unity側のスクリプトは2か所に分かれています。

| 場所 | 中身 |
|---|---|
| `Packages/SpatialSearch/`（パッケージ `com.yoshinaga.spatialsearch`、名前空間 `SpatialSearch`） | 他のプロジェクトでも使える部分: `SpatialSearchDataSource`、撮影データと検索結果の型（`SearchData`）、`SearchClient`、通信路の共通の入り口（`SearchTransportBase`）、結果の表示（`SearchResultView` / `GrabbableSearchResultView` / `SearchResultMeshBuilder`）、シェーダーとマテリアル |
| `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/` | このプロジェクト（Meta Quest）固有の部分: `MainCoordinator`、`SearchCapture`、`WebSocketSearchTransport`、`FrameCopyController`、`HeadFollow` |

パッケージは `WebSocketSharpUtl` と同じく、`UnityMetaQuest/Packages/manifest.json` から `file:../../Packages/SpatialSearch` で読み込んでいます。使い方は [Packages/SpatialSearch/README.md](../Packages/SpatialSearch/README.md) にあります。

`MainCoordinator` が3つのクラスに同じ `SpatialSearchDataSource` を渡し、各クラスはそこに流れてくるデータを受け取って動きます。ボタンや入力欄などUIの操作は `MainCoordinator` がまとめて受け持ち、3つのクラスはUIを知りません。`2-SpatialSearchLiteFrame(Experimental)` シーンでは、手の枠を扱う `FrameCopyController` も加わります。

| クラス | 役割 |
|---|---|
| `MainCoordinator` | 3つのクラスをつなぎ、UIの操作（IPダイアログ、検索ウィンドウの開閉、検索の開始）と、コントローラのパネルの状態表示（検索中・見つかった数・エラー）を受け持ちます |
| `SearchCapture` | `Capture(検索語)` が呼ばれたらカメラ画像と深度を撮影します。深度の各点が画像のどこに写るか（UV）と、その点での面の向き（法線）も求めて `SetCapture` します。`CaptureAt(位置)` で撮影すると、その位置が画像のどこに写るかも撮影データに入れます（点で探す）。手の枠のために、深度へ向けたレイキャストもします |
| `SearchClient` | 撮影データを受け取ったら、画像と検索語（点で探すときは画像上の点）をJSONにして、通信路でサーバーへ送ります。返ってきたマスクを `SetResult` します。通信そのものは次の通信路に任せていて、`SearchClient` はWebSocketを知りません |
| `WebSocketSearchTransport` | `SearchClient` の通信路（`SearchTransportBase`）をWebSocketで実装したものです。`WebSocketSharpUtl` の `WebSocketClientManager` を包み、`ws://IP:ポート/` への接続と、文字列の送受信、接続の状態の通知を受け持ちます。HTTPなど別の方法にしたいときは、`SearchTransportBase` を継承したクラスに差し替えます |
| `FrameCopyController` | 両手の枠の形を判定し、頭から枠の中心へのレイが深度に当たった位置に点を出します。点がロックされたら、手が下りるのを待って `SearchCapture.CaptureAt` で撮影します（2-SpatialSearchLiteFrame(Experimental) シーン） |
| `SearchResultView` | マスクの白い部分に写っていた点だけを集めて、まとめて単色で表示します（0-SpatialSearchLite シーン） |
| `GrabbableSearchResultView` | 見つかった物体ごとにひな形のプレハブ `GrabbableSearchResult` を複製し、物体の点を、撮影したカメラ画像を貼った四角形で表示します。つかめる範囲の箱も物体に合わせます（1-SpatialSearchLiteGrab シーン。2-SpatialSearchLiteFrame(Experimental) シーンではひな形が `GrabbableCopy`） |
| `SearchResultMeshBuilder` | `GrabbableSearchResultView` が使う部品です。点（深度の1画素）ごとに、その画素が写している範囲の四角形を作り、四隅をカメラ画像へ投影してUVにします |

画像は点ごとに1色を割り当てるのではなく、四角形の中でカメラ画像の画素そのものを引いて貼っています（シェーダーはパッケージの `SpatialSearchColoredPointsURP`）。そのため、近づいても粗いドットにはなりません。

四角形はシェーダーが毎フレーム視点の方へ向けるので（ビルボード）、物体を傾けても板は常にこちらを向きます。四隅と画像の対応は撮影したときのままなので、撮影した向きから見るとぴったり合い、大きく傾けると貼った画像が少しずれて見えます。

そこで、物体の正面から離れて見るほど物体を透明にしています。正面は点群から求めた面の向きで（本や画面なら表面の向き）、それと今の視点から見た向きの内積で決めていて、真横ではほとんど透明に、裏側からは見えなくなります。撮影したカメラの方向ではなく面の向きを基準にしているので、机の上の本のように斜めから撮った物でも、表面を自分に向けた姿勢から上下左右のどちらへ傾けても、同じ角度で透けていきます。物体をつかんで動かしたり回したりすると、正面も物体と一緒に動くので、表面を自分の方へ向け直せば再び見えます。

つかむ仕組みは Meta XR Interaction SDK の部品をそのまま使っています。プレハブ `GrabbableSearchResult` に `Grabbable` / `GrabFreeTransformer` / `GrabInteractable` が付いていて、シーンのインタラクションリグにあるコントローラの `ControllerGrabInteractor` からつかめます。`2-SpatialSearchLiteFrame(Experimental)` シーンのプレハブ `GrabbableCopy` には `HandGrabInteractable` も付いていて、手の `HandGrabInteractor` からもつかめます。

手の枠で指した点は、実世界の位置（深度にレイが当たった位置）として覚えます。手を下ろしてから撮影した画像へこの位置を投影し直すので、枠を作った手は撮影した画像に写りません。レイは頭の中心から引くので、両目で見ている枠の中心とは少しずれますが、点を見ながら物体に合わせれば気になりません。

サーバー側は `server/` にあります。`server.py` がUnityとのWebSocket通信を、`sam3_comfyui.py` がComfyUIの呼び出しを担当します。検索語の代わりに画像上の点（`{"type": "point", "x": .., "y": .., "image": ..}`、左上が原点のピクセル）を受け取ると、SAM 3.1 の点プロンプト（`SAM3_Detect` の `positive_coords`）で、その点に写っている物体1つのマスクを返します。
