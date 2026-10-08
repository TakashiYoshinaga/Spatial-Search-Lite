# Spatial-Search-Lite

**Meta Quest で、目の前の物を言葉やジェスチャーで探す空間検索アプリです。**

`monitor` や `chair` と入力すると、見つかった物の位置が点群でハイライトされます。見つけた物をつかんで動かしたり、両手の指で作った枠で物を切り出してコピーしたりするシーンもあります。

| 体験できること | デモ動画 |
|---|---|
| 言葉で探した物をハイライトする | [YouTube](https://youtu.be/CpNNvYJ_eqo) |
| 見つけた物をつかんで動かす | [YouTube](https://youtu.be/KBflmsotNTc) |
| 指の枠で物をコピーする（実験機能） | [YouTube](https://youtu.be/BAKXGBKgiSI) |

**体験には Meta Quest 3 / 3S と、AI処理を行うPCが必要です。** この手順では、リポジトリをPCに取得し、UnityでアプリをビルドしてQuestへインストールします。

- [必要なもの](#必要なもの)
- [初回の準備](#初回の準備)
- [起動して検索する](#起動して検索する)（2回目以降はここから）
- [うまくいかないとき](#うまくいかないとき)

## 必要なもの

| 必要なもの | 用途・条件 |
|---|---|
| Meta Quest 3 / 3S とコントローラ | パススルーカメラと深度を使います |
| PC | ComfyUIで SAM 3.1 を動かせる環境。Python 3.10 以上も必要です |
| [ComfyUI](https://www.comfy.org/) と SAM 3.1 のモデル | カメラ画像から物体を探します。モデルの準備は下の手順で行います |
| Unity 6000.3.7f1 | アプリのビルド用。Android Build Support（SDK・NDK・OpenJDKを含む）を入れてください |
| USBケーブル | ビルドしたアプリをPCからQuestへ入れるために使います |
| 同じLANへの接続 | Questと、Pythonサーバーを動かすPCを同じネットワークにつなぎます |

Questのカメラ画像をPCへ送り、PCで物体を認識した結果をQuestに表示します。**体験中はPCで ComfyUI と Pythonサーバーを起動しておいてください。**

## 初回の準備

### 1. リポジトリをPCに取得する

Gitを使う場合は、次のコマンドを実行します。

```bash
git clone https://github.com/TakashiYoshinaga/Spatial-Search-Lite.git
cd Spatial-Search-Lite
```

Gitを使わない場合は、このページの **Code → Download ZIP** からダウンロードして展開します。

**以降のPythonコマンドは、`README.md` があるフォルダで実行してください。** ZIPを使った場合も、展開したフォルダをターミナルで開きます。

### 2. PCのAI環境を準備する

#### ComfyUIとモデルを準備する

1. [ComfyUI](https://www.comfy.org/) をインストールします。すでに使っている場合は最新版に更新してください。サーバーが使う `SAM3_Detect` などのノードが必要です。
2. [`sam3.1_multiplex_fp16.safetensors` をダウンロード](https://huggingface.co/Comfy-Org/sam3.1/blob/main/checkpoints/sam3.1_multiplex_fp16.safetensors)し、**ファイル名を変えずに** ComfyUIの `models/checkpoints/` に置きます。

モデルの配置先は次のとおりです。

```text
ComfyUI/
└── models/
    └── checkpoints/
        └── sam3.1_multiplex_fp16.safetensors
```

- **ポータブル版・git clone版：** ComfyUI本体のフォルダ内の `models/checkpoints/` に置きます。
- **ComfyUI Desktop：** **Menu → Desktop Settings → Storage** を開き、**Shared Models** でモデルフォルダの場所を確認してください。

モデルを置いたあと、ComfyUIが起動中の場合は再起動してください。

#### Pythonサーバーを準備する

リポジトリのルートで、Pythonサーバーの依存パッケージをインストールします。

```bash
python -m pip install -r server/requirements.txt
```

### 3. Questアプリをビルドして入れる

1. Questを開発者モードにし、PCとUSBで接続します。Questに **USBデバッグを許可**する確認が出たら許可してください。初めて設定する場合は、[Meta公式のヘッドセット設定手順](https://developers.meta.com/vr/documentation/unity/unity-env-device-setup/?locale=ja_JP)を参照してください。
2. Unity Hubから、このリポジトリの **`UnityMetaQuest` フォルダ**を Unity 6000.3.7f1 で開きます。
3. まずは **`Assets/Scenes/0-SpatialSearchLite.unity`** を開きます。
4. **File → Build Profiles** で Android のプロファイルを選び、有効にします。プロファイルがなければ **Add Build Profile → Android** で作成し、**Switch Profile** を押します。
5. **Scene List** に開いたシーンを追加し、**体験するシーンだけにチェック**を入れます。
6. **Run Device** に接続したQuestを選び、**Build And Run** を実行します。保存先を指定すると、ビルド後にQuestへインストールされ、アプリが起動します。

ビルド画面の詳しい操作は、[Unity公式のAndroidビルド手順](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/building-and-delivering/build-process)を参照してください。

ほかの体験を試すときは、次のシーンに切り替えてビルドし直します。シーンはすべて `UnityMetaQuest/Assets/Scenes/` にあります。

| シーンファイル | 体験できること |
|---|---|
| `0-SpatialSearchLite.unity` | 言葉で探した物を単色の点群で表示する（最初に試すシーン） |
| `1-SpatialSearchLiteGrab.unity` | 探した物をカメラ画像付きで表示し、コントローラでつかんで動かす |
| `2-SpatialSearchLiteFrame(Experimental).unity` | 指の枠で物をコピーする。言葉での検索もでき、結果を手でもつかめる |

## 起動して検索する

初回の準備が終わったら、**ComfyUI → Pythonサーバー → Questアプリ** の順に起動します。2回目以降も同じ手順です。

### 1. ComfyUIとPythonサーバーを起動する

ComfyUIを普段どおり起動します。ワークフローはサーバーから実行するので、ComfyUIの画面での操作は不要です。

続いて、リポジトリのルートでPythonサーバーを起動します。

```bash
python server/server.py --host 0.0.0.0
```

`listening on ws://0.0.0.0:8765/` と表示されれば、Questからの接続を受け付ける準備ができています。このターミナルは開いたままにします。終了するときは `Ctrl+C` を押してください。

ComfyUIのURLが `http://127.0.0.1:8188` と異なる場合は、[サーバーのオプション](docs/development.md#サーバーのオプション)の `--comfyui` を指定します。

> サーバーには認証・暗号化がありません。信頼できるLAN内で使ってください。

### 2. QuestアプリからPCに接続する

1. Questアプリを起動します。初回のビルド直後にすでに起動している場合は、そのまま進めます。
2. **空間データ**と**カメラ**へのアクセスを求められたら、両方とも許可します。
3. **Server IP address** に、Pythonサーバーを動かしているPCのLAN内のIPアドレスを入力し、**Connect** を押します。

Windowsでは、PCのターミナルで `ipconfig` を実行し、Questと同じネットワークにつながる Wi-Fi / Ethernet の **IPv4 アドレス**を確認します。例えば `192.168.1.10` です。`0.0.0.0` はサーバーの待ち受け用の値なので、この入力欄にはPCのIPアドレスを入れてください。

**ダイアログが閉じたら接続完了です。** 入力したIPアドレスは保存され、次回は入力済みの状態で表示されます。

### 3. 目の前の物を検索する

1. 左コントローラのパネルの **Search** を、右手のレイで押します。
2. 開いた検索ウィンドウに、物の名前を**英語で**入力します。最初は `monitor`、`chair`、`cup` などを試してください。
3. 探したい物の方を向き、ウィンドウの **Search** を押します。ウィンドウは自動で閉じます。
4. 結果が返ると、見つかった物の位置がハイライトされます。左コントローラのパネルには検索の状態と見つかった数が表示されます。

基本シーンでは黄色の点群が表示されます。Grab / Frame シーンでは、撮影したカメラ画像付きで表示されます。別の物を検索すると、前の結果は消えます。

つかんで動かしたり、指の枠でコピーしたりする操作は、[追加機能の操作手順](docs/interactions.md)へ進んでください。

## うまくいかないとき

| 症状 | 確認すること |
|---|---|
| **Connect** を押してもダイアログが閉じない / `Not connected to the server` と出る | QuestとPCが同じLANにいるか、PCのIPアドレスが正しいか、サーバーを `--host 0.0.0.0` で起動したかを確認します。PCのファイアウォールでポート8765への接続も確認してください。接続が切れた場合はアプリを起動し直します |
| サーバーに `could not reach` のようなエラーが出る | ComfyUIが起動しているか、`--comfyui` のURLが合っているかを確認します |
| `ComfyUI execution failed` と出る | ComfyUIを更新し、モデルのファイル名と置き場所（`models/checkpoints/`）を確認します |
| `was not found` と出る | 検索語を英語の一般的な名前に変えるか、物体に近づいて試します。必要なら [サーバーの `--threshold`](docs/development.md#サーバーのオプション) を下げます |
| 点がずれて表示される | 撮影した瞬間の位置で点群を作っています。検索中は頭を大きく動かさずに試してください |

つかむ操作・指の枠の問題は、[追加機能の「うまくいかないとき」](docs/interactions.md#うまくいかないとき)を参照してください。

## さらに詳しく

- [つかむ・指の枠でコピーする](docs/interactions.md)：Grab / Frame シーンの操作手順
- [設定・しくみ](docs/development.md)：サーバーのオプション、Unityの設定項目、実装の解説
- [SpatialSearch パッケージ](Packages/SpatialSearch/README.md)：別のUnityプロジェクトで利用する方法

## ライセンス

このリポジトリは [MIT License](LICENSE) です。SAM 3.1 のモデルは [Comfy-Org/sam3.1](https://huggingface.co/Comfy-Org/sam3.1) で配布されており、SAM License が適用されます。
