# 📚 ドキュメント

camera-tool の使い方と技術資料。ツールの概要は [README](../README.md) を参照。

| ドキュメント | 内容 |
|:--|:--|
| 🚀 [使い方](usage.md) | 起動、設定、画面の操作、テスト |
| 📷 [取込と表示](capture.md) | MediaFrameReader での取込と FaceViewer の表示 |
| 🙂 [顔検出](face-detection.md) | RFB-320 での顔検出 |
| 🏃 [動体検知](motion-detection.md) | 32x24 のマスでの動き検出と、顔検出との連動 |
| 🔲 [検知範囲](detection-area.md) | 検出する場所の指定 |
| 📦 [物体検知](object-detection.md) | D-FINE-N での人・動物・乗り物の検出 |
| 🌐 [Web 配信](web-streaming.md) | WebStream の仕組みと計測 |
| 🔌 [Web API](web-api.md) | パス、API、WebSocket のメッセージ |
| 🎞️ [H.264 配信](h264-streaming.md) | NVENC と WebCodecs での配信 |
| 🔧 [追加の改善](improvements.md) | 割当の削減、前処理の SIMD 化、MJPEG の改善 |

## 🧩 プロジェクトの中身

| プロジェクト | 内容 |
|:--|:--|
| `src/CameraTool.Capture` | MediaFrameReader のラッパー(WPF に依存しない) |
| `src/CameraTool.Vision` | YUV の変換(AVX2 / SSSE3)、JPEG エンコード(WIC)、H.264 エンコード(Media Foundation)、顔検出(ONNX Runtime + RFB-320)、物体検知(ONNX Runtime + D-FINE-N)、動体検知、検知範囲 |
| `src/CameraTool.FaceViewer` | カメラ映像と検知の結果を表示し、検知範囲を編集する WPF アプリ |
| `src/CameraTool.WebStream` | カメラ映像をブラウザーへ配信する ASP.NET Core サーバー(WebSocket の JPEG / H.264、MJPEG) |
| `tests/CameraTool.Vision.Tests` | Vision の単体テスト(xunit.v3) |

## 🖥️ 計測の環境

- カメラ: ELECOM 2MP Webcam (VID_056E / PID_701E)
- PC: Ryzen 9 5900X、GeForce RTX 4060、Windows 11 (26300)、.NET 10
- Release ビルドで各構成を約30秒動かし、5秒ごとの値を平均(最大は期間中の最大)
