# 📷 取込と表示

FaceViewer は MediaFrameReader でフレームを受け取り、BGRA に変換して WriteableBitmap に表示する。

## 📏 指標

| 指標 | 内容 |
|:--|:--|
| Capture latency | `MediaFrameReference.SystemRelativeTime` から FrameArrived まで。ほぼ1フレーム周期で、大半はカメラからの転送 |
| Process | FrameArrived でトリプルバッファへ書く時間(BGRA のコピーか YUV からの変換) |
| Display latency | FrameArrived から WriteableBitmap へのコピーまで |
| CPU | プロセス全体の CPU 使用率(全コアに対する割合) |
| Allocated / GC | プロセス全体の割当と GC 回数 |

## 📊 計測結果

### Phase 1 → Phase 2(YUY2 640x480)

| 項目 | Phase 1 | Phase 2 (Reader) | Phase 2 (Software) |
|:--|--:|--:|--:|
| フレームの読み取り | 投影 API(`SoftwareBitmap`) | COM vtable を直接呼ぶ | COM vtable を直接呼ぶ |
| BGRA への変換 | MediaFrameReader | MediaFrameReader | ImageConverter (AVX2) |
| GC | 約15回/秒(すべて gen2) | 0 | 0 |
| Allocated | 309 KB/s | 129 KB/s | 119 KB/s |
| Display latency(平均 / 最大) | 2.2 / 4.7 ms | 0.8 / 1.7 ms | 0.3 / 0.5 ms |

- Phase 1 だけ Debug ビルドで計測
- GC は投影の `SoftwareBitmap` がメモリプレッシャーを登録するため(ビルド構成とは無関係)

### フォーマットと変換方式(表示は Immediate)

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

- どれも取込・表示とも約30fps、GC 0回
- 最大値は、UI スレッドが一時的に遅れたときの外れ値を含む

### 表示の更新方式(YUY2 640x480)

| 変換 | 更新方式 | 表示 fps | Display latency | Allocated |
|:--|:--|--:|--:|--:|
| Reader | Immediate | 29.8 | 0.8 / 1.7 ms | 129 KB/s |
| Reader | Rendering | 27.9 | 12.5 / 40.1 ms | 175 KB/s |
| Software | Immediate | 29.7 | 0.3 / 0.5 ms | 119 KB/s |
| Software | Rendering | 28.4 | 12.4 / 31.9 ms | 180 KB/s |

## 💡 分かったこと

- **投影の `SoftwareBitmap` は gen2 GC を起こす**: Dispose しても約4フレームに1回フル GC。COM vtable を直接呼ぶと GC 0
- **フォーマット**: YUY2 はカメラ本来の非圧縮形式で、Capture latency が最小(640x480 まで)。NV12 は Windows が MJPG をデコードしたもので、解像度により約 +1.5〜6 ms
- **色レンジ**: YUY2 はリミテッド(16-235)、MJPG 由来の NV12 はフル(0-255)で、どちらも BT.601。自前で変換するときは `MF_MT_VIDEO_NOMINAL_RANGE` に従う(取り違えると白飛び・黒つぶれ)
- **変換方式**: AVX2 の自前変換は 1080p でも 1 ms 以下。CPU は MediaFrameReader の BGRA8 変換の 1/2〜1/3 で、遅延は同じ。既定は `Software`
- **表示の更新方式**: `Rendering` は描画ティックまで待つので平均約12 ms 遅れ、取りこぼしも出る。既定は `Immediate`
- **残りの割当(約120〜130 KB/s)**: 約6割は計測値パネルの文字描画(1秒ごと)、約3割はフレームごとの Dispatcher 呼び出し。映像の経路は1フレーム約1.7 KB。WinRT のイベント引数と Dispatcher 呼び出しの分は後でなくし、文字描画の分も減らした([追加の改善](improvements.md))
