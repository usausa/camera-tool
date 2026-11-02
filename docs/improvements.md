# 🔧 追加の改善

後から加えた改善。最初の2つと割当の見直し(2回目)は、FaceViewer と WebStream の両方に効く。

## 📥 フレーム受信の割当をなくす

- FrameArrived を、CsWinRT の投影ではなく COM の vtable で購読する(`FrameArrivedSubscription`)。ComWrappers で作ったハンドラーを add_FrameArrived に渡し、受け取った IMediaFrameReader から直接フレームを読む
- 投影ではイベントのたびに引数のラッパーが作られ、約440 B/フレームの割当になっていた(プローブで計測。直接購読では割当なし)
- FaceViewer(検出なし)の割当: 118 → 102 KB/s(YUY2 640x480)、113 → 104 KB/s(NV12 1280x720)
- WebStream: 割当トレースから WinRT のイベント引数が消え、配信経路に残る割当は ASP.NET Core と .NET の内部だけになった

## ⚡ 検出の前処理の SIMD 化

- TensorConverter で、列ごとの座標を先に計算し、4隅の値をスカラーで集めてから AVX2 で8画素ずつ補間・色変換する
- この PC(Ryzen 9 5900X)では gather 命令が遅いので使わない
- 計算の順序はスカラー版と同じで、結果はビット単位で一致する(テストで確認)

| 構成 | 変更前 | 変更後 |
|:--|--:|--:|
| FaceViewer YUY2 640x480 | 0.93 ms | 0.27 ms |
| FaceViewer NV12 1280x720 | 0.91 ms | 0.29 ms |
| WebStream YUY2 640x480 | 0.77 ms | 0.25 ms |
| WebStream NV12 1280x720 | 0.78 ms | 0.38 ms |
| WebStream NV12 1920x1080 | − | 0.35 ms |

前処理は約1/3。減った時間は1フレーム約0.5 ms(1コアの約1.5 %)で、CPU 使用率の差は計測のばらつきに埋もれる程度。

## 🎞️ MJPEG の表示を1フレーム早める(WebStream)

- Chromium は `multipart/x-mixed-replace` の各パートを、次のパートのヘッダーを受け取った時点で表示する
- 以前は「境界 + ヘッダー(Content-Length)+ JPEG」の順に書いていたので、各フレームが次のフレームの到着まで表示されず、1フレーム分(30fps で約33 ms)遅れていた
- JPEG の直後に、境界と次のパートのヘッダーを書くように変えた
- ⚠️ パートに Content-Length は付かない(次の JPEG の大きさはまだ分からない)。Content-Length で区切るクライアントでは読めないので、境界か JPEG の SOI / EOI で区切る。VLC などのプレイヤーは境界で区切るはず(この PC には入っておらず未確認)

テスト用のサーバーから2枚を3秒間隔で送り、組み込みブラウザー(Chromium)で表示された時刻を比べた。

| 書き方 | 1枚目 | 2枚目 |
|:--|--:|--:|
| ヘッダー + JPEG(変更前) | 3.0 秒後(2枚目の到着時) | 計測した6秒間は表示されない |
| ヘッダー + JPEG + 境界 | 3.0 秒後 | 計測した6秒間は表示されない |
| ✅ JPEG + 境界 + 次のヘッダー(変更後) | 25 ms 後 | 3.0 秒後 |

- JPEG の直後に境界だけを書いても、Chromium では変わらない
- 変更後は、`<img>` を作ってから 31 ms で最初のフレームが表示され、3秒間に89フレーム(約30fps)が描かれた
- curl で受け取った3秒分(61フレーム)も、すべてのパートが JPEG の SOI から EOI までそろっていた

## ♻️ 割当の見直し(2回目)

割当のトレース(GC の AllocationTick とスタック)で残りの割当を洗い出し、アプリ側で減らせるものを直した。

- **FaceViewer: 更新の通知をウィンドウメッセージにする**: 映像と、顔・物体・動きの重ね描きの更新は、ほかのスレッドから UI スレッドへ通知する。Dispatcher への投稿は1回ごとに DispatcherOperation や Task などを作るので、メッセージ専用のウィンドウへの PostMessage に替えた(`UpdateRequest`)。続けて来た通知を1回にまとめるのは今までどおり
- **FaceViewer: 同期コンテキストを使い回す**: WPF は Dispatcher の操作やウィンドウメッセージのたびに同期コンテキストを作る。起動時に `BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance` を設定した
- **検出スレッドは実行コンテキストを引き継がない**(両アプリ): ホストの実行コンテキストを引き継いでいたので、FaceViewer では投稿のたびに複製されていた。`Thread.UnsafeStart` で始める
- **CPU 時間は `Environment.CpuUsage` で取る**(両アプリ): 1秒ごとの `Process.GetCurrentProcess()` は毎回割り当てる
- **WebStream: WebSocket の送信に取り消しのトークンを渡さない**: 取り消しできるトークンを渡すと、.NET の WebSocket は送信ごとに遅い経路(非同期の待ちと登録)を通る。送信中に取り消されたときは接続を中断する。待っている間の取り消しは今までどおり正常に閉じる(サーバーの停止では 1001 を送る)

YUY2 640x480、Release、各約40秒。

| アプリ | 構成 | 変更前 | 変更後 |
|:--|:--|--:|--:|
| FaceViewer | 顔検出 + 動体検知 | 201 KB/s | 161 KB/s |
| FaceViewer | + 物体検知 | 226 KB/s | 215 KB/s |
| WebStream | JPEG 1台 | 約 13 KB/s | 約 10 KB/s |
| WebStream | H.264 1台 | 約 34 KB/s | 約 29 KB/s |
| WebStream | JPEG + H.264、検知すべて | 約 22 KB/s | 約 12 KB/s |

- FaceViewer はアプリの計測値(5秒ごと)の平均。WebStream は割当が少なく、トレースの標本(約100 KB ごと)からの目安
- 実際の部屋を写しているので、場面で ±20〜40 KB/s ほどばらつく。物体検知ありの差はこの範囲に入る。直した割当そのものは、トレースから消えたことを確かめた
- GC はどの構成でも 0 回。FaceViewer の表示は約30fps、表示遅延(到着→表示)は平均 0.35〜0.39 ms で変わらない
- 残りの主なもの: FaceViewer は計測値パネルの文字のレイアウト(1秒ごと、約60〜70 KB/s)、WPF の描画の内部、毎フレームの `WriteableBitmap.Lock`。WebStream は H.264 の送信バッファの取り直し(.NET の WebSocket の内部)、1秒ごとの状態、開発時のログ
