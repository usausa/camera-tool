# camera-tool

C#/WPF でカメラを使うアプリケーションを作成・検証するためのソリューションです。
カメラの制御には MediaFrameReader (Windows Runtime) を使い、低遅延・低アロケーションな実装を検証します。

## プロジェクト

| プロジェクト | 内容 |
|:--|:--|
| `src/CameraTool.Capture` | MediaFrameReader のラッパー(WPF に依存しない) |
| `src/CameraTool.Vision` | YUV → BGRA 変換(AVX2)、顔検出(ONNX Runtime + RFB-320) |
| `src/CameraTool.FaceViewer` | カメラ映像と顔検出の結果を表示する WPF アプリ |
| `tests/CameraTool.Vision.Tests` | Vision の単体テスト(xunit.v3) |

## 実行

```
dotnet run --project src/CameraTool.FaceViewer
```

設定は `appsettings.json` で指定し、コマンドライン引数でも上書きできます(例: `--Camera:Format=Nv12 --Camera:Width=1280 --Camera:Height=720`)。

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Camera:Name` | デバイス名。空なら先頭のデバイス | `ELECOM 2MP Webcam` |
| `Camera:Format` | `Yuy2` / `Nv12` | `Yuy2` |
| `Camera:Width` / `Height` / `Fps` | 解像度とフレームレート | `640` / `480` / `30` |
| `Camera:Conversion` | `Software`: ImageConverter で BGRA に変換 / `Reader`: MediaFrameReader が BGRA8 に変換 | `Software` |
| `Display:UpdateMode` | `Immediate`: フレーム到着時に Dispatcher で更新 / `Rendering`: CompositionTarget.Rendering で更新 | `Immediate` |
| `Detect:Enable` | 顔検出の有効/無効(画面からも切り替え可能) | `true` |
| `Detect:Model` | モデルファイル | `version-RFB-320.onnx` |
| `Detect:ConfidenceThreshold` / `IouThreshold` | 検出のしきい値と NMS の IoU | `0.7` / `0.3` |
| `Detect:IntraOpNumThreads` / `InterOpNumThreads` | ONNX Runtime のスレッド数 | `1` / `1` |

モデルは [Ultra-Light-Fast-Generic-Face-Detector-1MB](https://github.com/Linzaer/Ultra-Light-Fast-Generic-Face-Detector-1MB) の `version-RFB-320.onnx`(MIT ライセンス、`version-RFB-320-LICENSE.txt`)です。

`DOTNET_ENVIRONMENT=Development` で起動すると、5秒ごとの計測値がログ(`Debug` レベル)に出力されます。

## テスト

xunit.v3 (Microsoft Testing Platform) のテストなので、ビルドしたテストの exe を直接実行します。

```
dotnet build tests/CameraTool.Vision.Tests
tests/CameraTool.Vision.Tests/bin/Debug/net10.0-windows10.0.26100.0/CameraTool.Vision.Tests.exe
```

## 計測結果

- 環境: ELECOM 2MP Webcam (VID_056E / PID_701E)、Windows 11 (26300)、.NET 10、Release ビルド
- 方法: 各構成を約30秒実行し、5秒ごとの値を平均(最大値は期間中の最大)

### 指標

| 指標 | 内容 |
|:--|:--|
| Capture latency | `MediaFrameReference.SystemRelativeTime` から FrameArrived まで。ほぼ1フレーム周期で、大半はカメラからの転送時間 |
| Process | FrameArrived の中でトリプルバッファへ書き込む時間(BGRA のコピー、または YUV からの変換) |
| Display latency | FrameArrived から WriteableBitmap へのコピーまで |
| CPU | プロセス全体の CPU 使用率(全コアに対する割合) |
| Allocated / GC | プロセス全体の割当量と GC 回数 |

### Phase 1 → Phase 2(YUY2 640x480)

| 項目 | Phase 1 | Phase 2 (Reader) | Phase 2 (Software) |
|:--|--:|--:|--:|
| フレームの読み取り | 投影 API(`SoftwareBitmap`) | COM vtable を直接呼ぶ | COM vtable を直接呼ぶ |
| BGRA への変換 | MediaFrameReader | MediaFrameReader | ImageConverter (AVX2) |
| GC | 約15回/秒(すべて gen2) | 0 | 0 |
| Allocated | 309 KB/s | 129 KB/s | 119 KB/s |
| Display latency(平均 / 最大) | 2.2 / 4.7 ms | 0.8 / 1.7 ms | 0.3 / 0.5 ms |

Phase 1 の値は Debug ビルドで計測したものです。GC は投影の `SoftwareBitmap` が GC のメモリプレッシャーを登録するために起きており、ビルド構成には関係ありません。

### フォーマットと変換方式(表示の更新方式は Immediate)

| フォーマット | 変換 | Capture latency | Process | Display latency | CPU |
|:--|:--|--:|--:|--:|--:|
| YUY2 640x480 | Reader | 32.4 / 32.7 ms | 0.11 / 0.51 ms | 0.8 / 1.7 ms | 0.44 % |
| YUY2 640x480 | Software | 34.0 / 48.4 ms | 0.13 / 0.28 ms | 0.3 / 0.5 ms | 0.23 % |
| NV12 640x480 | Reader | 35.7 / 50.7 ms | 0.18 / 1.38 ms | 1.1 / 2.9 ms | 0.82 % |
| NV12 640x480 | Software | 35.6 / 50.9 ms | 0.16 / 0.89 ms | 0.5 / 5.3 ms | 0.27 % |
| NV12 1280x720 | Reader | 37.4 / 52.3 ms | 0.25 / 0.63 ms | 1.5 / 2.3 ms | 1.24 % |
| NV12 1280x720 | Software | 37.3 / 52.6 ms | 0.39 / 0.94 ms | 1.1 / 26.1 ms | 0.50 % |
| NV12 1920x1080 | Reader | 39.5 / 54.1 ms | 0.54 / 0.78 ms | 2.4 / 3.1 ms | 1.64 % |
| NV12 1920x1080 | Software | 39.4 / 54.3 ms | 0.75 / 1.25 ms | 1.4 / 2.3 ms | 0.49 % |

いずれも取込・表示ともに約30fpsで、GC は0回でした。最大値には UI スレッドが一時的に遅れたときの外れ値を含みます。

### 表示の更新方式(YUY2 640x480)

| 変換 | 更新方式 | 表示 fps | Display latency | Allocated |
|:--|:--|--:|--:|--:|
| Reader | Immediate | 29.8 | 0.8 / 1.7 ms | 129 KB/s |
| Reader | Rendering | 27.9 | 12.5 / 40.1 ms | 175 KB/s |
| Software | Immediate | 29.7 | 0.3 / 0.5 ms | 119 KB/s |
| Software | Rendering | 28.4 | 12.4 / 31.9 ms | 180 KB/s |

### 分かったこと

- **投影の `SoftwareBitmap` は gen2 GC を誘発する。** Dispose しても約4フレームに1回フル GC が起きる。フレームごとの経路を COM vtable の直接呼び出しにすると GC は 0 になる。
- **フォーマット**: YUY2 はカメラ本来の非圧縮形式で、Capture latency が最も小さい(640x480 まで)。NV12 は Windows 側で MJPG をデコードしたもので、解像度に応じて約 +1.5〜6 ms かかる。
- **色レンジ**: YUY2 はリミテッドレンジ(16-235)、MJPG 由来の NV12 はフルレンジ(0-255)で、どちらも BT.601。自前で変換するときは `MF_MT_VIDEO_NOMINAL_RANGE` に従う必要がある(フルレンジをリミテッドとして扱うと白飛び・黒つぶれが起きる)。
- **変換方式**: AVX2 の自前変換は 1080p でも 1 ms 以下で、CPU 使用率は MediaFrameReader の BGRA8 変換の 1/2〜1/3。遅延はほぼ同じなので、既定値を `Software` にした。
- **表示の更新方式**: `Rendering` は描画ティックまで待つため平均約12 ms 遅れ、フレームの取りこぼしも起きる。`Immediate` を既定値にした。
- **残りの割当(約120〜130 KB/s)**: 約6割は計測値パネルの文字描画(1秒ごと)、約3割はフレームごとの Dispatcher 呼び出しで、ほかに `WriteableBitmap.Lock` と WinRT のイベント引数が少しずつある。映像の経路だけなら1フレームあたり約1.7 KB で、計測中に GC は起きていない。

## 顔検出

### 処理の流れ

```
FrameArrived (取込スレッド)
  ├─ BGRA に変換してトリプルバッファへ → 表示
  └─ 検出器が空いていれば: 320x240 に縮小 + RGB 化 + (x-127)/128 でテンソルへ(前処理)
                            ↓
                     検出スレッド: ONNX Runtime で推論 → しきい値 → NMS → 顔枠を表示
```

- 検出は専用スレッドで動き、処理中に届いたフレームは検出せずに飛ばします。推論が取込や表示を待たせることはありません。
- ONNX Runtime は IoBinding で入出力を事前確保したバッファに結び付けているので、推論1回あたりの割当は約21バイトです。
- 顔枠のラベルは子 Visual にして、文字が変わったときだけ描き直し、位置は `Offset` で動かしています。

### 計測結果(Release ビルド、変換は Software、表示は Immediate)

| 構成 | 検出 fps | 前処理 | 推論 | Capture latency | Display latency | CPU | Allocated |
|:--|--:|--:|--:|--:|--:|--:|--:|
| YUY2 640x480 検出なし | - | - | - | 33.9 / 48.6 ms | 0.8 / 18.6 ms | 0.30 % | 118 KB/s |
| YUY2 640x480 1スレッド | 29.5 | 0.93 / 1.25 ms | 7.92 / 14.01 ms | 34.0 / 48.5 ms | 0.4 / 1.2 ms | 1.30 % | 208 KB/s |
| YUY2 640x480 2スレッド | 29.5 | 0.99 / 1.18 ms | 6.84 / 9.56 ms | 34.0 / 48.5 ms | 0.6 / 5.2 ms | 5.29 % | 190 KB/s |
| YUY2 640x480 4スレッド | 30.0 | 0.95 / 1.17 ms | 4.49 / 5.96 ms | 34.0 / 48.6 ms | 0.9 / 29.9 ms | 13.48 % | 239 KB/s |
| NV12 1280x720 検出なし | - | - | - | 37.2 / 52.3 ms | 1.0 / 7.6 ms | 0.26 % | 113 KB/s |
| NV12 1280x720 1スレッド | 29.8 | 0.91 / 1.47 ms | 8.36 / 11.53 ms | 37.2 / 52.0 ms | 1.0 / 8.1 ms | 1.58 % | 201 KB/s |

スレッド数は `Detect:IntraOpNumThreads` です。どの構成でも取込・表示は約30fpsで、GC は0回でした。

### 分かったこと

- **1スレッドでもカメラと同じ約30fpsで検出できる**(推論 約8 ms)。検出を有効にしても Capture latency と Display latency は変わらない。
- **推論スレッドを増やすと CPU 使用率が大きく増える。** 4スレッドで推論は約4.5 ms まで縮むが、ONNX Runtime のスレッドがスピン待ちするため CPU は約10倍になり、表示にも時々遅れが出た。既定値は1スレッドにした。
- **前処理は約0.9 ms**(スカラー実装、入力サイズは 320x240 固定なので解像度にほぼ依存しない)。
- **割当は検出で約 +80〜90 KB/s**(顔枠の更新と Dispatcher 呼び出し)。ラベルを毎回 `FormattedText` で描くと約 +150〜230 KB/s、文字を図形にして `DrawImage` で描くと約 1.7 MB/s まで増えたため、子 Visual を使う方式にした。
- **モデルの警告**: このモデルは古い形式で、重みがグラフ入力にも含まれるため、ONNX Runtime の定数畳み込みなどの最適化が効かない(警告はログレベルで抑えている)。重みをグラフ入力から外したモデルにすれば、推論がさらに速くなる可能性がある。
