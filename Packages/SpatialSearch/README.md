# SpatialSearch

カメラ画像から言葉や点で指定した物体を探し、深度の点群と組み合わせて表示する Unity パッケージです（`com.yoshinaga.spatialsearch`、名前空間 `SpatialSearch`）。

物体を探す処理はPCのPythonサーバー（このリポジトリの `server/`、ComfyUI の SAM 3.1 を使用）が受け持ち、このパッケージは、サーバーへ送る内容の組み立てと返ってきた結果の受け取り、結果の表示を受け持ちます。カメラ画像と深度の撮影、サーバーとの通信（WebSocketなど）は機器や環境ごとに違うので、使う側のプロジェクトで用意します（Meta Quest 用の例は `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/` の `SearchCapture.cs` と `WebSocketSearchTransport.cs`）。

ボタンや文字などのUIは持ちません。検索の状態や見つかった数は Observable で知らせるので、表示の仕方は使う側で決めます。

```text
撮影するクラス（プロジェクトで用意） ─CaptureData─▶ SearchClient ─JSON─▶ 通信路（プロジェクトで用意） ─▶ Python サーバー
結果を表示するクラス               ◀─SearchResult─ SearchClient ◀─JSON─ 通信路（プロジェクトで用意） ◀─ Python サーバー
              （撮影・結果・接続の状態は SpatialSearchDataSource を通してやり取りする）
```

## 必要なもの

| パッケージ | 入れ方 |
|---|---|
| R3（`com.cysharp.r3` と `org.nuget.r3`） | 使う側の `manifest.json` に OpenUPM と UnityNuGet の scoped registry を設定し、両パッケージを追加します |
| URP（`com.unity.render-pipelines.universal`） | シェーダーが URP 用です |

このパッケージの `package.json` では `dependencies` が空なので、R3 と URP は自動では入りません。使う側のプロジェクトで追加してください。レジストリの設定とバージョンの例は、[このリポジトリの manifest.json](../../UnityMetaQuest/Packages/manifest.json) にあります。

通信のライブラリには依存しません。通信路（`SearchTransportBase` を継承したクラス）が使うパッケージは、使う側で入れてください。

`manifest.json` の `dependencies` に追加する項目の例です。パスは `manifest.json` から見た相対パスなので、別のプロジェクトでは配置に合わせて変更してください。WebSocketSharpUtl は、このリポジトリの WebSocket 版の通信路を使う場合に必要です。既存の依存パッケージは残してください。

```json
{
  "dependencies": {
    "com.yoshinaga.spatialsearch": "file:../../Packages/SpatialSearch",
    "com.yoshinaga.websocketsharputl": "file:../../Packages/WebSocketSharpUtl"
  }
}
```

## 中身

| クラス・アセット | 役割 |
|---|---|
| `SpatialSearchDataSource` | 各クラスの間でデータを受け渡す場所。撮影データ（`CaptureObservable`）、検索結果（`ResultObservable`）、接続の状態（`ConnectedObservable`）を流します |
| `CaptureData` / `ImageProjection` | 撮影した瞬間のデータと、ワールド座標をカメラ画像のUVへ投影する計算（`SearchData.cs`） |
| `SearchResult` / `Detection` | 検索結果。見つかった物体ごとのマスク画像（`SearchData.cs`） |
| `SearchClient` | 撮影データを受け取ってJSONにし、通信路でサーバーへ送り、返ってきたマスクを検索結果にして流します。同じ GameObject にある通信路を自動で探します（別の GameObject にあるときは、Inspector の Transport に設定します） |
| `SearchTransportBase` | サーバーとの通信路の共通の入り口。文字列の送受信と接続の状態だけを決めてあり、WebSocket や HTTP などの実装は使う側で継承して作ります（下の「通信路の実装」） |
| `SearchResultViewBase` | 結果を表示するクラスの共通の入り口。表示し終えたら `ShownObservable` で、表示した結果と物体の数（`ShownResult`）を知らせます |
| `SearchResultView` | マスクに写っていた点を、まとめて単色の点群で表示します |
| `GrabbableSearchResultView` | 見つかった物体ごとにひな形を複製し、撮影したカメラ画像を貼った四角形で表示します。つかめる範囲（`BoxCollider`）も物体に合わせます |
| `SearchResultMeshBuilder` | `GrabbableSearchResultView` が使う、カメラ画像を貼った四角形のメッシュを作る部品 |
| `Shaders/SpatialSearchHighlightPointsURP`、`Materials/SpatialSearchHighlightMat` | `SearchResultView` の点群用 |
| `Shaders/SpatialSearchColoredPointsURP`、`Materials/SpatialSearchColoredPointsMat` | `GrabbableSearchResultView` の四角形用 |

## 使い方

1. 撮影するクラスを用意し、`CaptureData` を作って `SpatialSearchDataSource.SetCapture` に流します（下の「撮影データの約束」）
2. 通信路を用意します。`SearchTransportBase` を継承したクラスを作ります（下の「通信路の実装」）
3. `SearchClient`、通信路、結果を表示するクラス（`SearchResultView` か `GrabbableSearchResultView` のどちらか1つ）をシーンに置きます。`SearchClient` と通信路は同じ GameObject に付けます。別の GameObject に置く場合は、`SearchClient` の Inspector の Transport に通信路を設定します
4. 起動時に、同じ `SpatialSearchDataSource` を各クラスの `Initialize` に渡します（通信路には渡しません。`SearchClient` が通信路を使います）
5. 状態を表示したいときは、`CaptureObservable`（検索を始めた）と `ShownObservable`（結果を表示し終えた）を購読します。**`SearchClient.Initialize` より先に購読してください**。未接続のとき `SearchClient` は撮影を受け取ったその場でエラー結果を流すので、後から購読すると「検索中」の表示がエラーの表示を上書きしてしまいます
6. `SearchClient.Connect(サーバーのIP)` で接続します（ポートなど接続先の細かい設定は、通信路の実装が決めます。このリポジトリの WebSocket 版は Inspector の Server Port で、既定は 8765）

次は `MonoBehaviour` の `Awake` などで行う初期化の抜粋です。`capture`、`resultView`、`client`、`status` は、使う側で用意した撮影クラス、表示クラス、`SearchClient`、状態表示用のテキストへの参照です。

```csharp
using R3;
using SpatialSearch;

var dataSource = new SpatialSearchDataSource();
capture.Initialize(dataSource);    // プロジェクトで用意した撮影クラス
resultView.Initialize(dataSource); // SearchResultViewBase

// 状態の表示（任意）。SearchClient より先に購読する
dataSource.CaptureObservable.Subscribe(c => status.text = $"Searching \"{c.Query}\"...").AddTo(this);
resultView.ShownObservable.Subscribe(shown => status.text = shown.Result.IsSuccess
    ? $"Found {shown.ObjectCount}"
    : $"Error: {shown.Result.Error}").AddTo(this);

client.Initialize(dataSource);     // SearchClient
client.Connect("192.168.0.10");
```

`ShownResult.ObjectCount` は、`SearchResultView` では検出された物体数（点を1つも表示できなければ0）、`GrabbableSearchResultView` では実際に生成した物体数です。マスクの位置に深度の点がない物体は、Grab版では生成されません。

撮影データは Unity のメインスレッドから `SetCapture` に流してください。前の検索の結果待ちの間に流した撮影は、`SearchClient` が無視し、その撮影の画像を破棄します。

### 結果の表示の設定

- `SearchResultView`: Point Material に `SpatialSearchHighlightMat` を設定します。Color / Point Size で点の色と大きさを変えられます
- `GrabbableSearchResultView`: Object Template に、物体1つぶんのひな形を設定します。ひな形には `MeshFilter` と `MeshRenderer`（マテリアルは `SpatialSearchColoredPointsMat`）を付けます。つかめるようにするときは、つかむ仕組み（Meta XR Interaction SDK の `Grabbable` / `GrabInteractable` など）と `BoxCollider` もひな形に付けます。このパッケージはつかむ仕組みには依存しません

### 通信路の実装

`SearchTransportBase`（`MonoBehaviour`）を継承して、サーバーと文字列をやり取りするクラスを作ります。JSON の組み立てや結果の読み取り、検索中の管理は `SearchClient` が行うので、通信路は文字列を運ぶだけです。

| 実装するもの | 内容 |
|---|---|
| `IsConnected` | 今つながっているか |
| `Connect(serverIp)` | 指定したIPのサーバーへ接続します。接続中なら切断してから繋ぎ直します。ポートやURLの組み立ては実装側で決めます |
| `Reconnect()` | 前回と同じサーバーへ繋ぎ直します。未接続のまま検索が始まったときに `SearchClient` が呼びます |
| `Send(message, completed)` | 文字列を送り、送れたかどうかを `completed(true / false)` で必ず知らせます。`false` のとき `SearchClient` は検索を失敗として扱います |
| `NotifyConnected(bool)` | 接続できたら `true`、切れたとき（接続に失敗したときも）`false` で呼びます |
| `NotifyMessage(string)` | サーバーから文字列を受け取ったら呼びます |

`NotifyConnected` / `NotifyMessage` と `completed` は、別スレッドから呼んでかまいません。メインスレッドへ戻す処理は `SearchClient` が行います。

Meta Quest 用の WebSocket 版の例は `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/WebSocketSearchTransport.cs` にあります（`WebSocketSharpUtl` の `WebSocketClientManager` を包んでいます）。

## 撮影データの約束（CaptureData）

| 項目 | 内容 |
|---|---|
| `Query` | 検索語。点で探すときは表示用の名前 |
| `HasPoint` / `PointUv` | `HasPoint` が true なら、検索語ではなく `PointUv`（0〜1、左下原点のカメラ画像のUV）に写っている物体1つを探します |
| `Image` | 撮影したカメラ画像。PNGへ変換するため、CPUから読み出せる `Texture2D` を撮影ごとに新しく作ります。結果の表示に使い続けるので、使い回して上書きしないでください |
| `Points[i]` | 深度の各点のワールド座標 |
| `ImageUvs[i]` | `Points[i]` がカメラ画像のどこに写るか（0〜1、**左下が原点**）。マスクをこのUVで引いて、点が物体上にあるかを決めます |
| `Depths[i]` | 点の深度（m） |
| `Normals[i]` | 点での面の向き（長さ1、カメラの側を向く）。求められない点は `Vector3.zero` |
| `PixelRightPerMeter` / `PixelUpPerMeter` | 深度1画素ぶんの右・上への幅を、1m先で測ったワールドのベクトル（深度に比例する） |
| `Projection` | 撮影した瞬間のカメラで、ワールド座標をカメラ画像のUVへ投影する `ImageProjection` |

`Points` と `ImageUvs` は、同じ点を同じ添字で表す、同じ長さの配列にしてください。画像の外へ投影される点は除外し、`ImageUvs` の各成分を0〜1の範囲にします。`GrabbableSearchResultView` を使う場合は、`Depths` と `Normals` も同じ長さで用意し、`PixelRightPerMeter`、`PixelUpPerMeter`、`Projection` を設定します。法線が求められなくても `Normals` の配列は必要で、その点の値を `Vector3.zero` にします。

`HasPoint` / `PointUv` を使うのは `SearchClient` だけです。表示には、`SearchResultView` は `Query`（ログ用）、`Points`、`ImageUvs` を使い、`GrabbableSearchResultView` は、それ以外の項目もすべて使います。UIに検索語を出す場合は、`ShownResult.Result.Capture.Query` を参照します。

両方の表示クラスは、新しい結果を受け取ったときに前の結果の `Image` と各 `Detection.Mask` を `Destroy` します。表示クラス自体が破棄されたときも同様です。表示直後に画像を破棄するわけではありません。同じ結果を複数の表示クラスで共有すると破棄が重複するので、この実装では表示クラスを1つにしてください。

## サーバーとの通信

`server/server.py` との間で、JSON の文字列をやり取りします。JSON を作って読むのは `SearchClient` で、運ぶのは通信路です。サーバーはWebSocketで待ち受けるので、このリポジトリの通信路はWebSocketです（HTTPなど別の方法にするときは、同じJSONをその方法で受け付けるサーバーも必要です）。

| 向き | メッセージ |
|---|---|
| Unity → サーバー（検索語） | `{"type": "search", "query": "monitor", "image": "<PNGのbase64>"}` |
| Unity → サーバー（点） | `{"type": "point", "x": 640, "y": 480, "image": "<PNGのbase64>"}`。x, y は画像のピクセルで **左上が原点**（`SearchClient` が `PointUv` の上下を反転して送ります） |
| サーバー → Unity | `{"type": "result", "query": "...", "detections": [{"score": 0.93, "mask": "<PNGのbase64>"}, ...]}` または `{"type": "error", "message": "..."}` |

検索語は空白だけの文字列にせず、`,` と `:` を含めないでください。このリポジトリのサーバーではエラーになります。点で探した場合、サーバーが返す `query` は空文字列ですが、Unity側の `SearchResult.Capture.Query` には撮影時の表示用の名前が残ります。

マスクは送った画像と同じ大きさで、白い部分が物体です。点で探して成功したときは `detections` が1件になります。
