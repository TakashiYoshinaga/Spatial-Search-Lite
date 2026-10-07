# Spatial-Search-Lite

Meta Quest で見ている現実の空間から、言葉で指定した物体を探して、その場所を点群でハイライトするサンプルです。

検索ウィンドウに「monitor」と入力して Search を押すと、Questのカメラ画像をPCへ送り、画像認識AI（SAM 3.1）が見つけた物体の位置に点群が表示されます。

両手で写真を撮るときの枠を作って物体を指すと、検索語なしでその物体1つだけを切り出して複製することもできます（[7. 指の枠で物体をコピーする](#7-指の枠で物体をコピーするspatialsearchliteframe-シーン)）。

```text
Quest : カメラ画像 + 深度 を撮影 ──(WebSocket)──▶ PC : Pythonサーバー ──▶ ComfyUI (SAM 3.1)
Quest : マスクに写っていた深度の点を表示 ◀──(WebSocket)── PC : 物体ごとのマスク画像
```

## 必要なもの

| 用途 | 必要なもの |
|---|---|
| ヘッドセット | Meta Quest 3 / 3S（パススルーカメラと深度を使います。指の枠を使うときはハンドトラッキングも使います） |
| Unity | Unity 6000.3.7f1、Android Build Support |
| PC（AI側） | [ComfyUI](https://www.comfy.org/)、SAM 3.1 のチェックポイント `sam3.1_multiplex_fp16.safetensors`、Python 3.10 以上 |

QuestとPCは同じLANにつないでください。

## 使い方

起動する順番は **ComfyUI → Pythonサーバー → Questアプリ** です。

### 1. ComfyUIを準備して起動する

#### ComfyUIを最新にする

SAM 3.1 を動かすノード（`SAM3_Detect` など。名前は SAM3 ですが SAM 3.1 もこのノードで動きます）はComfyUI本体に入っている新しいノードです。古いComfyUIでは動かないので、最新版に更新してください。

#### SAM 3.1 のチェックポイントを置く（初回だけ）

1. ComfyUI公式の Hugging Face リポジトリ [Comfy-Org/sam3.1](https://huggingface.co/Comfy-Org/sam3.1) から [`sam3.1_multiplex_fp16.safetensors`](https://huggingface.co/Comfy-Org/sam3.1/blob/main/checkpoints/sam3.1_multiplex_fp16.safetensors)（約1.75GB）をダウンロードします。ページのダウンロードボタンから保存できます。コマンドで取得する場合は次のとおりです

   ```bash
   curl -L -o sam3.1_multiplex_fp16.safetensors https://huggingface.co/Comfy-Org/sam3.1/resolve/main/checkpoints/sam3.1_multiplex_fp16.safetensors
   ```

2. ダウンロードしたファイルを、ComfyUIの `models/checkpoints/` フォルダへ入れます。**ファイル名は変えないでください**

   ```text
   ComfyUI/
   └── models/
       └── checkpoints/
           └── sam3.1_multiplex_fp16.safetensors
   ```

   - **ポータブル版・git clone版：** ComfyUI本体のフォルダの中にある `models/checkpoints/` です
   - **ComfyUI Desktop：** インストール時に選んだデータの保存先の中にある `models/checkpoints/` です（本体のインストール先ではありません）。場所が分からないときは、Desktopのメニューからモデルフォルダを開けます

3. ComfyUIが起動中なら再起動します

SAM 3.1 のモデルは SAM License で配布されています。

#### ComfyUIを起動する

ComfyUIを普段どおり起動します。**画面で何かを操作する必要はありません。** 起動しておくだけで大丈夫です。開発者モード（dev mode）も不要です。

### 2. Pythonサーバーを起動する

初回だけ依存パッケージを入れます。

```bash
pip install -r server/requirements.txt
```

サーバーを起動します。Questから接続するので `--host 0.0.0.0` を付けます。

```bash
python server/server.py --host 0.0.0.0
```

`listening on ws://0.0.0.0:8765/` と表示されれば準備完了です。止めるときは `Ctrl+C` を押します。

| オプション | 既定値 | 内容 |
|---|---|---|
| `--host` | `127.0.0.1` | 待ち受けるアドレス。Questから繋ぐときは `0.0.0.0` |
| `--port` | `8765` | 待ち受けるポート |
| `--comfyui` | `http://127.0.0.1:8188` | ComfyUIのURL |
| `--checkpoint` | `sam3.1_multiplex_fp16.safetensors` | SAM 3.1 のチェックポイント名 |
| `--threshold` | `0.5` | これより自信の低い検出は捨てます。見つからないときは下げてみてください |
| `--max-detections` | `8` | 1回の検索で見つける物体の最大数 |

> **注意:** このサーバーには認証も暗号化もありません。信頼できるLANの中だけで使ってください。

### 3. Questアプリをビルドする

1. `UnityMetaQuest` フォルダを Unity で開きます
2. 使うシーンを開きます
   - `Assets/Scenes/SpatialSearchLite.unity` : 見つかった物をピンクの点群で表示します
   - `Assets/Scenes/SpatialSearchLiteGrab.unity` : 見つかった物を、撮影したカメラ画像を貼った面で表示し、コントローラでつかんで動かせます（[6. 見つけた物をつかむ](#6-見つけた物をつかむspatialsearchlitegrab-シーン)）
   - `Assets/Scenes/SpatialSearchLiteFrame.unity` : Grab シーンの機能に加えて、両手の指で作った枠で物体を指すと、その物体1つを複製します。複製は手でもつかめます（[7. 指の枠で物体をコピーする](#7-指の枠で物体をコピーするspatialsearchliteframe-シーン)）
3. Build Profiles のシーン一覧で、使うシーンにチェックを入れます
4. Questへビルドして実行します

### 4. サーバーに接続する

1. 起動時に **空間データ** と **カメラ** へのアクセスを求められるので、両方とも許可します（許可しないと検索できません）
2. 目の前に **Server IP address** のダイアログが出るので、PCのIPアドレスを入力して **Connect** を押します（Windowsなら `ipconfig` で確認できます）。接続できるとダイアログが閉じます

入力したIPアドレスは保存され、次回からは最初から入った状態で表示されます。ダイアログが閉じない場合は接続できていません。IPアドレスを確かめて、もう一度 **Connect** を押してください。途中で接続が切れた場合は、アプリを起動し直してください。

### 5. Questで検索する

1. 左コントローラのパネルにある **Search** ボタンを右手のレイで押すと、検索ウィンドウが開きます（もう一度押すと閉じます）
2. 検索ウィンドウに探したい物の名前を **英語で** 入力します（例: `monitor`、`chair`、`cup`）
3. 探したい物の方を向いて、ウィンドウの **Search** を押します。ウィンドウは自動で閉じます
4. 数秒後、見つかった物体の位置にピンクの点群が表示されます（`SpatialSearchLiteGrab` シーンでは撮影したカメラ画像を貼った面）。左コントローラのパネルに検索の状態と見つかった数が表示されます

別の物を探すときは、もう一度検索ウィンドウを開いて検索します。前の検索結果の点群は消えます。

### 6. 見つけた物をつかむ（SpatialSearchLiteGrab シーン）

`SpatialSearchLiteGrab` シーンでは、見つかった物体ごとに別々の面になり、コントローラでつかんで動かせます。面には、撮影した瞬間のカメラ画像が貼ってあります。

1. コントローラを物体に近づけます。つかめる距離に入ると、物体が白っぽくなります
2. **グリップボタン**（中指で握るボタン）を握るとつかめます。つかんでいる間は物体が黄色がかります
3. 握ったまま動かすと物体が付いてきます。離すとその場に残ります

両手のコントローラで同じ物体をつかむと、大きさも変えられます。このシーンでは手（ハンドトラッキング）ではつかめません（`SpatialSearchLiteFrame` シーンでは手でもつかめます）。

### 7. 指の枠で物体をコピーする（SpatialSearchLiteFrame シーン）

`SpatialSearchLiteFrame` シーンでは、両手で写真を撮るときの枠を作って物体を指すと、その物体1つだけを切り出し、撮影した位置に複製します。検索語は要りません。複製は手でもコントローラでもつかんで動かせます。

事前に Quest の **設定 > 動きのトラッキング > ハンドトラッキング** をオンにしてください。サーバーへの接続まではコントローラで操作し、そのあとコントローラを置きます。

1. 顔の前（腕を軽く伸ばしたあたり）で、両手の親指と人差し指でL字を作ります。他の指は軽く握ります
   - 左手は枠の **左上** の角: 人差し指を右へ、親指を下へ向けます
   - 右手は枠の **右下** の角: 人差し指を左へ、親指を上へ向けます
2. 枠として認識されると白い四角が出て、枠の中心の先にある物の表面に点が出ます。点を物体の真ん中に合わせます
3. そのまま約2秒止めると、点の周りのリングが一周して緑になり、`Lower your hands` と出ます（ロック）
4. 両手を下ろして、カメラに写らないようにします。手が見えなくなると撮影され、`Copying...` と出ます
5. 数秒後、指した物体の複製がその場所に現れます。手でつまむか握ってつかみ、好きな場所へ動かせます

- 点が **橙色** のときは、物体の縁や穴を指しています。ロックしないので、物体の真ん中へ点を動かしてください
- ロックしてから3秒以内に手を下ろさないと取り消されます（`Canceled`）。ロックした点が視界から外れたときも取り消されます（`Lost the target`）
- 手を下ろす間に頭が動いても大丈夫です。ロックした点を実世界の位置として覚えていて、撮影した画像の上でその位置を指します
- 物体の一部（顔だけ、取っ手だけなど）が切り出されることがあります。物体の真ん中を指すと、全体が取れやすくなります
- このシーンでも、左コントローラの **Search** から検索語で探せます。新しく複製するか検索すると、前の結果は消えます

## 設定できる項目

| 場所（Hierarchy） | 項目 | 内容 |
|---|---|---|
| `[MainCoordinator]` の Main Coordinator | Default Server Ip | 一度もIPを入力していないときにダイアログへ入れておく値 |
| `Logic/Search` の Search Capture | Min / Max Depth Meters | この距離の範囲にある点だけを使います |
| `Logic/WebSocket` の Search Client | Server Port | Pythonサーバーのポート |
| `SearchWindow` / `ServerDialog` の Head Follow | Distance / Height Offset / Follow Speed | ウィンドウを出す距離・高さ、頭に追いつく速さ |
| `Logic/SearchResultView`（SpatialSearchLite） | Color / Point Size | 結果の点の色と大きさ |
| `Logic/SearchResultView`（SpatialSearchLiteGrab） | Outlier Ratio / Min Collider Size | つかめる範囲の箱を決めるときに外す端の点の割合と、箱の最小の大きさ |
| `Assets/Materials/SpatialSearchColoredPointsMat` | Depth Tolerance | 重なった板を同じ面として描く奥行きの幅（板の大きさの何倍か）。ちらつくときは上げ、後ろの板が透けて見えるときは下げます |
| 〃 | Fade Start / Fade End | 物体の正面（点群から求めた面の向き）と今の視点の向きの内積（1=正面から、0=真横、負=裏側）。Fade Start 以上で不透明、Fade End 以下で完全に透明、その間はなめらかに変えます。もっと横から見ても見えるようにするときは Fade End を下げます |
| `Assets/Prefabs/GrabbableSearchResult` の Interactable Color Visual | Hover / Select Color State | 近づけた・つかんだときに重ねる色（Aが重ねる強さ） |
| `Logic/FrameCopy` の Frame Copy Controller（SpatialSearchLiteFrame） | Stable Angle / Lock Seconds | 点がこの角度（度）以内に止まっている時間がこの秒数になると、ロックします |
| 〃 | Hands Gone Seconds / Lower Hands Timeout | 両手がカメラ画像から消えてから撮影するまでの時間と、ロックしてから手を下ろすのを待つ時間（秒） |
| 〃 | Result Timeout | サーバーの結果を待つ時間（秒）。過ぎると次の操作を受け付けます |
| 〃 | Dot Angle / Ring Angle / Aim / Edge / Locked Color | 点とリングの見た目の大きさ（度）と、狙っているとき・縁を指しているとき・ロックしたときの色 |

## うまくいかないとき

| 症状 | 確認すること |
|---|---|
| コントローラのパネルに `Not connected to the server` と出る | 入力したIPアドレス、PCのファイアウォール（ポート8765）、サーバーを `--host 0.0.0.0` で起動したかを確認し、アプリを起動し直して接続してください |
| サーバーに `could not reach` のようなエラーが出る | ComfyUIが起動しているか、`--comfyui` のURLが合っているか |
| `ComfyUI execution failed` と出る | チェックポイントのファイル名と置き場所（`models/checkpoints/`）が合っているか |
| `was not found` になる | 検索語を英語の一般的な名前にする、物体に近づく、`--threshold` を下げる |
| 点がずれて表示される | 検索中は頭を大きく動かさないでください。撮影した瞬間の位置で点群を作っています |
| 物体が透けて見える・見えなくなる | 物体の正面（表面の向き）から離れて横や裏から見るほど透明にしています。表面を自分の方へ向け直すか、マテリアルの Fade Start / Fade End を調整してください |
| 点群をつかめない | `SpatialSearchLiteGrab` / `SpatialSearchLiteFrame` シーンか確認し、コントローラを点群の中まで入れてからグリップボタンを握ってください。手でつかめるのは `SpatialSearchLiteFrame` シーンだけです |
| 指で枠を作っても白い四角が出ない | ハンドトラッキングがオンで、コントローラを手から離しているか確認してください。左手が左上・右手が右下で、人差し指が横、親指が縦（左手は下、右手は上）になっているかも確かめてください |
| 枠は出るが点が出ない | 枠の先にある物までの距離が、Search Capture の Min / Max Depth Meters（既定は 0.1〜5m）の範囲にあるか確認してください |
| `Copying...` のまま進まない | 検索語での検索の結果待ちと重なると、撮影が無視されます。30秒で元に戻るので、やり直してください |

## しくみ

Unity側のスクリプトは `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/` にあります。`MainCoordinator` が3つのクラスに同じ `SpatialSearchDataSource` を渡し、各クラスはそこに流れてくるデータを受け取って動きます。ボタンや入力欄などUIの操作は `MainCoordinator` がまとめて受け持ち、3つのクラスはUIを知りません。`SpatialSearchLiteFrame` シーンでは、手の枠を扱う `FrameCopyController` も加わります。

| クラス | 役割 |
|---|---|
| `MainCoordinator` | 3つのクラスをつなぎ、UIの操作（IPダイアログ、検索ウィンドウの開閉、検索の開始）を受け持ちます |
| `SearchCapture` | `Capture(検索語)` が呼ばれたらカメラ画像と深度を撮影します。深度の各点が画像のどこに写るか（UV）と、その点での面の向き（法線）も求めて `SetCapture` します。`CaptureAt(位置)` で撮影すると、その位置が画像のどこに写るかも撮影データに入れます（点で探す）。手の枠のために、深度へ向けたレイキャストもします |
| `SearchClient` | 撮影データを受け取ったら、画像と検索語（点で探すときは画像上の点）をサーバーへ送ります。返ってきたマスクを `SetResult` します |
| `FrameCopyController` | 両手の枠の形を判定し、頭から枠の中心へのレイが深度に当たった位置に点を出します。点がロックされたら、手が下りるのを待って `SearchCapture.CaptureAt` で撮影します（SpatialSearchLiteFrame シーン） |
| `SearchResultView` | マスクの白い部分に写っていた点だけを集めて、まとめて単色で表示します（SpatialSearchLite シーン） |
| `GrabbableSearchResultView` | 見つかった物体ごとにひな形のプレハブ `GrabbableSearchResult` を複製し、物体の点を、撮影したカメラ画像を貼った四角形で表示します。つかめる範囲の箱も物体に合わせます（SpatialSearchLiteGrab シーン。SpatialSearchLiteFrame シーンではひな形が `GrabbableCopy`） |
| `SearchResultMeshBuilder` | `GrabbableSearchResultView` が使う部品です。点（深度の1画素）ごとに、その画素が写している範囲の四角形を作り、四隅をカメラ画像へ投影してUVにします |

画像は点ごとに1色を割り当てるのではなく、四角形の中でカメラ画像の画素そのものを引いて貼っています（シェーダーは `SpatialSearchColoredPointsURP`）。そのため、近づいても粗いドットにはなりません。

四角形はシェーダーが毎フレーム視点の方へ向けるので（ビルボード）、物体を傾けても板は常にこちらを向きます。四隅と画像の対応は撮影したときのままなので、撮影した向きから見るとぴったり合い、大きく傾けると貼った画像が少しずれて見えます。

そこで、物体の正面から離れて見るほど物体を透明にしています。正面は点群から求めた面の向きで（本や画面なら表面の向き）、それと今の視点から見た向きの内積で決めていて、真横ではほとんど透明に、裏側からは見えなくなります。撮影したカメラの方向ではなく面の向きを基準にしているので、机の上の本のように斜めから撮った物でも、表面を自分に向けた姿勢から上下左右のどちらへ傾けても、同じ角度で透けていきます。物体をつかんで動かしたり回したりすると、正面も物体と一緒に動くので、表面を自分の方へ向け直せば再び見えます。

つかむ仕組みは Meta XR Interaction SDK の部品をそのまま使っています。プレハブ `GrabbableSearchResult` に `Grabbable` / `GrabFreeTransformer` / `GrabInteractable` が付いていて、シーンのインタラクションリグにあるコントローラの `ControllerGrabInteractor` からつかめます。`SpatialSearchLiteFrame` シーンのプレハブ `GrabbableCopy` には `HandGrabInteractable` も付いていて、手の `HandGrabInteractor` からもつかめます。

手の枠で指した点は、実世界の位置（深度にレイが当たった位置）として覚えます。手を下ろしてから撮影した画像へこの位置を投影し直すので、枠を作った手は撮影した画像に写りません。レイは頭の中心から引くので、両目で見ている枠の中心とは少しずれますが、点を見ながら物体に合わせれば気になりません。

サーバー側は `server/` にあります。`server.py` がUnityとのWebSocket通信を、`sam3_comfyui.py` がComfyUIの呼び出しを担当します。検索語の代わりに画像上の点（`{"type": "point", "x": .., "y": .., "image": ..}`、左上が原点のピクセル）を受け取ると、SAM 3.1 の点プロンプト（`SAM3_Detect` の `positive_coords`）で、その点に写っている物体1つのマスクを返します。

## ライセンス

[MIT License](LICENSE)
