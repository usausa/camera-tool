# 🚀 使い方

## 💻 動作環境

- Windows 11、.NET 10 SDK
- USB カメラ。既定は `ELECOM 2MP Webcam` の YUY2 640x480 30fps
- H.264 配信は NVIDIA の GPU(NVENC)があれば使う。なければ Windows 標準のエンコーダー

## ▶️ 起動

🖥️ FaceViewer(WPF アプリ)

```
dotnet run --project src/CameraTool.FaceViewer
```

🌐 WebStream(配信サーバー)

```
dotnet run --project src/CameraTool.WebStream
```

- ブラウザーで `http://localhost:5080` を開くとビューアーが出る
- ⚠️ ローカル専用で認証はない
- ⚠️ 2つのアプリはカメラを同時に使えない(どちらも排他で開く)

## ⚙️ 設定

`appsettings.json` で指定する。コマンドライン引数でも上書きできる(例: `--Camera:Format=Nv12 --Camera:Width=1280 --Camera:Height=720`)。

### FaceViewer

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Camera:Name` | デバイス名。空なら先頭のデバイス | `ELECOM 2MP Webcam` |
| `Camera:Format` | `Yuy2` / `Nv12` | `Yuy2` |
| `Camera:Width` / `Height` / `Fps` | 解像度とフレームレート | `640` / `480` / `30` |
| `Camera:Conversion` | `Software`: ImageConverter で BGRA に変換 / `Reader`: MediaFrameReader が変換 | `Software` |
| `Display:UpdateMode` | `Immediate`: フレーム到着時に更新 / `Rendering`: CompositionTarget.Rendering で更新 | `Immediate` |
| `Detect:Enable` | 顔検出の ON/OFF(画面からも切り替え可) | `true` |
| `Detect:Model` | モデルファイル | `version-RFB-320.onnx` |
| `Detect:ConfidenceThreshold` / `IouThreshold` | 検出のしきい値 / NMS の IoU | `0.7` / `0.3` |
| `Detect:IntraOpNumThreads` / `InterOpNumThreads` | ONNX Runtime のスレッド数 | `1` / `1` |
| `Detect:MotionGate` | 動きがない間は、顔検出と物体検知を `IdleInterval` ごとにだけ行う(画面からも切り替え可) | `false` |
| `Detect:IdleInterval` / `MotionHoldTime` | 動きがない間の検出の間隔 / 最後の動きから「動きあり」とみなす時間(ms) | `1000` / `2000` |
| `Motion:Enable` | 動体検知の ON/OFF(画面からも切り替え可) | `true` |
| `Motion:Threshold` | 背景との輝度差のしきい値(0〜255、小さいほど敏感) | `25` |
| `Motion:CellRatio` | マスの中で変化した画素の割合がこれ以上なら「動いたマス」 | `0.2` |
| `Motion:MinArea` | 動いたマスの割合(検知範囲のマス数に対する)がこれ以上なら「動きあり」 | `0.005` |
| `Motion:BackgroundRate` | 背景の更新率(1フレームあたり) | `0.05` |
| `Area:Path` | 検知範囲のファイル(環境変数を展開、相対パスは exe のフォルダーから) | `%LOCALAPPDATA%\CameraTool\FaceViewer\area.json` |
| `Object:Enable` | 物体検知の ON/OFF(画面からも切り替え可。モデルは最初に ON にしたときに読む) | `false` |
| `Object:Model` | モデルファイル | `dfine-nano-coco.onnx` |
| `Object:ConfidenceThreshold` | 検出のしきい値 | `0.5` |
| `Object:IntraOpNumThreads` / `InterOpNumThreads` | ONNX Runtime のスレッド数 | `1` / `1` |
| `Object:Interval` | 検出の最小間隔(ms、0 なら検出器が空き次第) | `200` |
| `Object:Classes` | 対象のクラス(COCO の名前をカンマ区切り、空なら 80 クラスすべて) | `person,bird,cat,dog,bicycle,car,motorcycle,bus,truck` |

### WebStream

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Urls` | 待ち受けるアドレス | `http://localhost:5080` |
| `Camera:Name` / `Format` / `Width` / `Height` / `Fps` | FaceViewer と同じ | `ELECOM 2MP Webcam` / `Yuy2` / `640` / `480` / `30` |
| `Stream:Quality` | JPEG の品質(1〜100) | `80` |
| `H264:Enable` | H.264 の配信(`/ws-h264`)の ON/OFF。エンコーダーはクライアントがいる間だけ動く | `true` |
| `H264:Bitrate` | ビットレート(kbps、CBR)。0 なら解像度で決める(640x480 まで 4000、1280x720 まで 8000、それより大きいと 16000) | `0` |
| `H264:KeyFrameInterval` | キーフレームの間隔(秒) | `2` |
| `H264:PreferHardware` | ハードウェアのエンコーダー(NVENC など)を優先。`false` なら Windows 標準のソフトウェアだけ | `true` |
| `Detect:*` | 顔検出と動きとの連動(FaceViewer と同じ) | `Enable` は `true`、`MotionGate` は `false` |
| `Motion:*` | 動体検知(FaceViewer と同じ) | `Enable` は `true` |
| `Area:Path` | 検知範囲のファイル(FaceViewer と同じ形式) | `%LOCALAPPDATA%\CameraTool\WebStream\area.json` |
| `Object:*` | 物体検知(FaceViewer と同じ) | `Enable` は `false` |

## 🖱️ 画面の操作

FaceViewer とビューアーに同じチェックボックスがある。ビューアーで変えるとサーバーの設定が変わる(ほかのビューアーにも反映)。

| 操作 | 内容 |
|:--|:--|
| Face detection | 顔検出の ON/OFF |
| Object detection | 物体検知の ON/OFF |
| Motion detection | 動体検知の ON/OFF |
| Detect only on motion | 動きがない間は、顔検出と物体検知の間隔を空ける |
| Edit area / Reset area | 検知範囲の編集 / 画面全体に戻す([検知範囲](detection-area.md)) |
| 表示方式(ビューアーだけ) | WebSocket(JPEG)/ WebSocket (H.264) / MJPEG |

ビューアーの重ね描きと範囲の編集は、WebSocket の表示(JPEG と H.264)だけで使える。

## 📝 ログ

`DOTNET_ENVIRONMENT=Development` で起動すると、5秒ごとの計測値をログ(`Debug` レベル)に出す。

## 🧪 テスト

xunit.v3(Microsoft Testing Platform)。ビルドしたテストの exe を直接実行する。

| プロジェクト | 対象 |
|:--|:--|
| `tests/CameraTool.Vision.Tests` | 画像の変換、JPEG / H.264 のエンコード、顔検出・物体検知・動体検知、検知範囲 |
| `tests/CameraTool.WebStream.Tests` | WebSocket のヘッダー、配信の仕組み、H.264 のキーフレーム待ち、検知範囲のファイル。カメラと GPU は使わない |

```
dotnet build tests/CameraTool.Vision.Tests
tests/CameraTool.Vision.Tests/bin/Debug/net10.0-windows10.0.26100.0/CameraTool.Vision.Tests.exe
dotnet build tests/CameraTool.WebStream.Tests
tests/CameraTool.WebStream.Tests/bin/Debug/net10.0-windows10.0.26100.0/CameraTool.WebStream.Tests.exe
```

## 🧠 同梱のモデル

| 用途 | モデル | ライセンス |
|:--|:--|:--|
| 顔検出 | [Ultra-Light-Fast-Generic-Face-Detector-1MB](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB) の `version-RFB-320.onnx` | MIT(`version-RFB-320-LICENSE.txt`) |
| 物体検知 | [D-FINE](https://github.com/Peterande/D-FINE) の D-FINE-N(COCO)を ONNX にした [inference4j/dfine-nano-coco](https://huggingface.co/inference4j/dfine-nano-coco) の `model.onnx`(`dfine-nano-coco.onnx` として同梱) | Apache License 2.0(`dfine-nano-coco-LICENSE.txt`) |
