# Japanese terminology — draft contribution

These are the contribution's terminology choices, pending native proofreading and
visual review. The supplied English glossary is preserved unchanged.

| Source | Japanese | Meaning / distinction |
| --- | --- | --- |
| Balance volume | 音量差を抑える | Reduce differences across the mix; not file compression. |
| Light / Moderate / Strong | 弱 / 中 / 強 | Audio intensity; CLI IDs remain light/moderate/strong. |
| Light / Dark | ライト / ダーク | Application theme. |
| Night mode | ナイトモード | Stronger balancing at low listening volume. |
| Audio / video / image | 音声 / 動画 / 画像 | Media types; 映像 for the encoded video stream where appropriate. |
| Frame / frame rate | フレーム / フレームレート | Preserve FPS where used as an abbreviation. |
| Track / stream | トラック / ストリーム | Media streams, distinct from channel and loop-search comparison series. |
| Channel | チャンネル | Left/right audio channels. |
| Clip / timeline | クリップ / タイムライン | Video segments and their arrangement. |
| Cut / crop | カット / トリミング | Temporal cut / spatial crop. |
| Resize / upscale | サイズを変更 / 拡大 | General resizing / AI resolution increase. |
| Pitch / height | 音程 / 高さ | Audio pitch / image dimensions. |
| Loop / junction | ループ / つなぎ目 | Repetition / connection between end and start. |
| Forward/backward / Ping-Pong | 往復 | Forward then reverse playback. |
| Candidate | 候補 | A proposed result to preview. |
| Proxy | 簡易プレビュー / 簡易表示 | Not a guarantee of full-resolution detail. |
| Remux | リマックス | Container change without re-encoding. |
| Stream copy / re-encode | ストリームコピー / 再エンコード | Encoded stream copy / encoding again. |
| Normalize clips | 統一形式に変換 | Match media signatures; not audio-volume normalization. |
| Burn subtitles | 字幕を焼き込む | Render text into video images. |
| Searchable PDF | 検索可能なPDF | PDF retaining/adding a text layer. |
| Stem separation | 音源分離 | Vocals, drums, bass and other components. |
| Remove / cancel | 取り除く / キャンセル | Queue removal / stop a task; source files are preserved. |
| Delete content | 削除 | Removes audio/image content in a new result. |
| Preset / queue / batch | プリセット / キュー / バッチ | Settings group / pending work / processing group. |

Keep ASCII variable braces, numeric format syntax, CLI values, model identifiers
and file-filter patterns unchanged. Japanese mnemonic labels use suffixes such as
`(&F)`; layout and collisions still need actual-window acceptance. Japanese glyph
fallback cannot be certified by a successful CSV or XML round-trip alone.
