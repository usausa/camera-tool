# 📷 camera-tool

Windows のカメラ映像を、低遅延・低アロケーションで表示・配信し、顔・物体・動きを検知する C# のツール。カメラの制御には MediaFrameReader(Windows Runtime)を使う。

## ✨ できること

| 機能 | 🖥️ FaceViewer | 🌐 WebStream |
|:--|:--:|:--:|
| 🎥 カメラ映像の表示 | ✅ | ✅(ブラウザー) |
| 🙂 顔検出(枠と信頼度) | ✅ | ✅ |
| 📦 物体検知(人・動物・乗り物など) | ✅ | ✅ |
| 🏃 動体検知(動いたところをマスで表示) | ✅ | ✅ |
| 🔲 検知範囲(検出する場所を指定) | ✅ | ✅ |
| 📡 配信(WebSocket の JPEG / H.264、MJPEG) | − | ✅ |

- 🖥️ **FaceViewer**: WPF のデスクトップアプリ
- 🌐 **WebStream**: ブラウザーへ配信する ASP.NET Core サーバー(ローカル専用、認証なし)
- ⚡ **低遅延**: カメラから画面に出るまで約 35 ms(640x480。大半はカメラ側)
- ♻️ **低アロケーション**: 定常状態では GC がほぼ起きない
- 🚫 録画や通知の機能はない

## 🧩 構成

| プロジェクト | 内容 |
|:--|:--|
| `src/CameraTool.Capture` | MediaFrameReader のラッパー |
| `src/CameraTool.Vision` | 画像の変換、JPEG / H.264 のエンコード、顔検出・物体検知・動体検知 |
| `src/CameraTool.FaceViewer` | WPF アプリ |
| `src/CameraTool.WebStream` | 配信サーバー |
| `tests/CameraTool.Vision.Tests` | Vision の単体テスト |

## 📚 ドキュメント

使い方と技術資料は [docs](docs/README.md) にある。

- 🚀 [使い方](docs/usage.md): 起動、設定、画面の操作、テスト
- 📷 [取込と表示](docs/capture.md)
- 🙂 [顔検出](docs/face-detection.md) / 🏃 [動体検知](docs/motion-detection.md) / 🔲 [検知範囲](docs/detection-area.md) / 📦 [物体検知](docs/object-detection.md)
- 🌐 [Web 配信](docs/web-streaming.md) / 🔌 [Web API](docs/web-api.md) / 🎞️ [H.264 配信](docs/h264-streaming.md)
- 🔧 [追加の改善](docs/improvements.md)
