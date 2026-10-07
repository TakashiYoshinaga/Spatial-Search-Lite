# SpatialSearch

カメラ画像から言葉や点で指定した物体を探し、深度の点群と組み合わせて表示する Unity パッケージです（`com.yoshinaga.spatialsearch`、名前空間 `SpatialSearch`）。

物体を探す処理はPCのPythonサーバー（このリポジトリの `server/`、ComfyUI の SAM 3.1 を使用）が受け持ち、このパッケージはサーバーとの通信と、結果の表示を受け持ちます。カメラ画像と深度の撮影は機器ごとに違うので、使う側のプロジェクトで用意します（Meta Quest 用の例は `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/SearchCapture.cs`）。

ボタンや文字などのUIは持ちません。検索の状態や見つかった数は Observable で知らせるので、表示の仕方は使う側で決めます。

```text
撮影するクラス（プロジェクトで用意） ─CaptureData─▶ SearchClient ─WebSocket─▶ Python サーバー
結果を表示するクラス               ◀─SearchResult─ SearchClient ◀─マスク画像─  Python サーバー
                       （すべて SpatialSearchDataSource を通してやり取りする）
```

## 必要なもの

| パッケージ | 入れ方 |
|---|---|
| R3（`com.cysharp.r3` と `org.nuget.r3`） | OpenUPM と UnityNuGet の scoped registry から入れます。このパッケージの `package.json` には書けないので、使う側の `manifest.json` に追加してください |
| WebSocketSharpUtl（`com.yoshinaga.websocketsharputl`） | このリポジトリの `Packages/WebSocketSharpUtl` を `file:` で読み込みます |
| URP（`com.unity.render-pipelines.universal`） | シェーダーが URP 用です |

`manifest.json` の例（パスは `manifest.json` から見た相対パス）:

```json
"com.yoshinaga.spatialsearch": "file:../../Packages/SpatialSearch",
"com.yoshinaga.websocketsharputl": "file:../../Packages/WebSocketSharpUtl",
```

## 中身

| クラス・アセット | 役割 |
|---|---|
| `SpatialSearchDataSource` | 各クラスの間でデータを受け渡す場所。撮影データ（`CaptureObservable`）、検索結果（`ResultObservable`）、接続の状態（`ConnectedObservable`）を流します |
| `CaptureData` / `ImageProjection` | 撮影した瞬間のデータと、ワールド座標をカメラ画像のUVへ投影する計算（`SearchData.cs`） |
| `SearchResult` / `Detection` | 検索結果。見つかった物体ごとのマスク画像（`SearchData.cs`） |
| `SearchClient` | 撮影データを受け取ってサーバーへ送り、返ってきたマスクを検索結果にして流します。`WebSocketClientManager` が自動で付きます |
| `SearchResultViewBase` | 結果を表示するクラスの共通の入り口。表示し終えたら `ShownObservable` で、表示した結果と物体の数（`ShownResult`）を知らせます |
| `SearchResultView` | マスクに写っていた点を、まとめて単色の点群で表示します |
| `GrabbableSearchResultView` | 見つかった物体ごとにひな形を複製し、撮影したカメラ画像を貼った四角形で表示します。つかめる範囲（`BoxCollider`）も物体に合わせます |
| `SearchResultMeshBuilder` | `GrabbableSearchResultView` が使う、カメラ画像を貼った四角形のメッシュを作る部品 |
| `Shaders/SpatialSearchHighlightPointsURP`、`Materials/SpatialSearchHighlightMat` | `SearchResultView` の点群用 |
| `Shaders/SpatialSearchColoredPointsURP`、`Materials/SpatialSearchColoredPointsMat` | `GrabbableSearchResultView` の四角形用 |

## 使い方

1. 撮影するクラスを用意し、`CaptureData` を作って `SpatialSearchDataSource.SetCapture` に流します（下の「撮影データの約束」）
2. `SearchClient` と、結果を表示するクラス（`SearchResultView` か `GrabbableSearchResultView`）をシーンに置きます
3. 起動時に、同じ `SpatialSearchDataSource` を各クラスの `Initialize` に渡します
4. 状態を表示したいときは、`CaptureObservable`（検索を始めた）と `ShownObservable`（結果を表示し終えた）を購読します。**`SearchClient.Initialize` より先に購読してください**。未接続のとき `SearchClient` は撮影を受け取ったその場でエラー結果を流すので、後から購読すると「検索中」の表示がエラーの表示を上書きしてしまいます
5. `SearchClient.Connect(サーバーのIP)` で接続します（ポートは Inspector の Server Port、既定は 8765）

```csharp
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

### 結果の表示の設定

- `SearchResultView`: Point Material に `SpatialSearchHighlightMat` を設定します。Color / Point Size で点の色と大きさを変えられます
- `GrabbableSearchResultView`: Object Template に、物体1つぶんのひな形を設定します。ひな形には `MeshFilter` と `MeshRenderer`（マテリアルは `SpatialSearchColoredPointsMat`）を付けます。つかめるようにするときは、つかむ仕組み（Meta XR Interaction SDK の `Grabbable` / `GrabInteractable` など）と `BoxCollider` もひな形に付けます。このパッケージはつかむ仕組みには依存しません

## 撮影データの約束（CaptureData）

| 項目 | 内容 |
|---|---|
| `Query` | 検索語。点で探すときは表示用の名前 |
| `HasPoint` / `PointUv` | `HasPoint` が true なら、検索語ではなく `PointUv`（カメラ画像のUV）に写っている物体1つを探します |
| `Image` | 撮影したカメラ画像。撮影ごとに新しく作ります（結果の表示に貼り続けるので、使い回して上書きしない）。表示し終えたクラスが `Destroy` します |
| `Points[i]` | 深度の各点のワールド座標 |
| `ImageUvs[i]` | `Points[i]` がカメラ画像のどこに写るか（0〜1、**左下が原点**）。マスクをこのUVで引いて、点が物体上にあるかを決めます |
| `Depths[i]` | 点の深度（m） |
| `Normals[i]` | 点での面の向き（長さ1、カメラの側を向く）。求められない点は `Vector3.zero` |
| `PixelRightPerMeter` / `PixelUpPerMeter` | 深度1画素ぶんの右・上への幅を、1m先で測ったワールドのベクトル（深度に比例する） |
| `Projection` | 撮影した瞬間のカメラで、ワールド座標をカメラ画像のUVへ投影する `ImageProjection` |

`HasPoint` / `PointUv` を使うのは `SearchClient` だけです。結果の表示では、`SearchResultView` は `Query`（状態の表示）、`Points`、`ImageUvs` を使い、`Image` は表示し終えたら `Destroy` します。`GrabbableSearchResultView` は、それ以外の項目もすべて使います。

## サーバーとの通信

`server/server.py` との間で、JSON をWebSocketでやり取りします。

| 向き | メッセージ |
|---|---|
| Unity → サーバー（検索語） | `{"type": "search", "query": "monitor", "image": "<PNGのbase64>"}` |
| Unity → サーバー（点） | `{"type": "point", "x": 640, "y": 480, "image": "<PNGのbase64>"}`。x, y は画像のピクセルで **左上が原点**（`SearchClient` が `PointUv` の上下を反転して送ります） |
| サーバー → Unity | `{"type": "result", "query": "...", "detections": [{"score": 0.93, "mask": "<PNGのbase64>"}, ...]}` または `{"type": "error", "message": "..."}` |

マスクは送った画像と同じ大きさで、白い部分が物体です。点で探したときは `detections` が1件になります。
