# Spatial-Search-Lite

Meta Quest で見ている現実の空間から、言葉で指定した物体を探して、その場所を点群でハイライトするサンプルです。

検索ウィンドウに「monitor」と入力して Search を押すと、Questのカメラ画像をPCへ送り、画像認識AI（SAM 3.1）が見つけた物体の位置に点群が表示されます。

```text
Quest : カメラ画像 + 深度 を撮影 ──(WebSocket)──▶ PC : Pythonサーバー ──▶ ComfyUI (SAM 3.1)
Quest : マスクに写っていた深度の点を表示 ◀──(WebSocket)── PC : 物体ごとのマスク画像
```

## 必要なもの

| 用途 | 必要なもの |
|---|---|
| ヘッドセット | Meta Quest 3 / 3S（パススルーカメラと深度を使います） |
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
2. `Assets/Scenes/SpatialSearchLite.unity` を開きます
3. Questへビルドして実行します

### 4. サーバーに接続する

1. 起動時に **空間データ** と **カメラ** へのアクセスを求められるので、両方とも許可します（許可しないと検索できません）
2. 目の前に **Server IP address** のダイアログが出るので、PCのIPアドレスを入力して **Connect** を押します（Windowsなら `ipconfig` で確認できます）。接続できるとダイアログが閉じます

入力したIPアドレスは保存され、次回からは最初から入った状態で表示されます。ダイアログが閉じない場合は接続できていません。IPアドレスを確かめて、もう一度 **Connect** を押してください。途中で接続が切れた場合は、アプリを起動し直してください。

### 5. Questで検索する

1. 左コントローラのパネルにある **Search** ボタンを右手のレイで押すと、検索ウィンドウが開きます（もう一度押すと閉じます）
2. 検索ウィンドウに探したい物の名前を **英語で** 入力します（例: `monitor`、`chair`、`cup`）
3. 探したい物の方を向いて、ウィンドウの **Search** を押します。ウィンドウは自動で閉じます
4. 数秒後、見つかった物体の位置にピンクの点群が表示されます。左コントローラのパネルに検索の状態と見つかった数が表示されます

別の物を探すときは、もう一度検索ウィンドウを開いて検索します。

## 設定できる項目

| 場所（Hierarchy） | 項目 | 内容 |
|---|---|---|
| `[MainCoordinator]` の Main Coordinator | Default Server Ip | 一度もIPを入力していないときにダイアログへ入れておく値 |
| `Logic/Search` の Search Capture | Min / Max Depth Meters | この距離の範囲にある点だけを使います |
| `Logic/WebSocket` の Search Client | Server Port | Pythonサーバーのポート |
| `SearchWindow` / `ServerDialog` の Head Follow | Distance / Height Offset / Follow Speed | ウィンドウを出す距離・高さ、頭に追いつく速さ |
| `Logic/SearchResultView` | Color / Point Size | 結果の点の色と大きさ |

## うまくいかないとき

| 症状 | 確認すること |
|---|---|
| コントローラのパネルに `Not connected to the server` と出る | 入力したIPアドレス、PCのファイアウォール（ポート8765）、サーバーを `--host 0.0.0.0` で起動したかを確認し、アプリを起動し直して接続してください |
| サーバーに `could not reach` のようなエラーが出る | ComfyUIが起動しているか、`--comfyui` のURLが合っているか |
| `ComfyUI execution failed` と出る | チェックポイントのファイル名と置き場所（`models/checkpoints/`）が合っているか |
| `was not found` になる | 検索語を英語の一般的な名前にする、物体に近づく、`--threshold` を下げる |
| 点がずれて表示される | 検索中は頭を大きく動かさないでください。撮影した瞬間の位置で点群を作っています |

## しくみ

Unity側のスクリプトは `UnityMetaQuest/Assets/Scripts/SpatialSearchLite/` にあります。`MainCoordinator` が3つのクラスに同じ `SpatialSearchDataSource` を渡し、各クラスはそこに流れてくるデータを受け取って動きます。ボタンや入力欄などUIの操作は `MainCoordinator` がまとめて受け持ち、3つのクラスはUIを知りません。

| クラス | 役割 |
|---|---|
| `MainCoordinator` | 3つのクラスをつなぎ、UIの操作（IPダイアログ、検索ウィンドウの開閉、検索の開始）を受け持ちます |
| `SearchCapture` | `Capture(検索語)` が呼ばれたらカメラ画像と深度を撮影します。深度の各点が画像のどこに写るかも計算して `SetCapture` します |
| `SearchClient` | 撮影データを受け取ったら画像と検索語をサーバーへ送ります。返ってきたマスクを `SetResult` します |
| `SearchResultView` | マスクの白い部分に写っていた点だけを集めて表示します |

サーバー側は `server/` にあります。`server.py` がUnityとのWebSocket通信を、`sam3_comfyui.py` がComfyUIの呼び出しを担当します。

## ライセンス

[MIT License](LICENSE)
