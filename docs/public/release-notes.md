# nikotFiler v0.1.0-preview.2

**ランタイム非同梱版**に変更しました。アプリの操作・機能は前のプレビュー版と同じです。

## 起動方法

1. [.NET Desktop Runtime 10（Windows x64）](https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0) をインストールします。対応する .NET 10 SDK がある場合は追加インストール不要です。
2. `nikotFiler-0.1.0-win-x64-framework-dependent.zip` をすべて展開します。
3. 展開先の `nikotFiler.exe` を起動します。同じフォルダーの DLL・JSON も必要です。

配布 ZIP には .NET ランタイムを含みません。WPF アプリのため、コンソール用の「.NET Runtime」ではなく「.NET Desktop Runtime」を使用してください。

## 確認内容

- Windows CI のビルドと30項目の回帰テストを通過。
- 配布フォルダーに EXE とアプリの DLL・設定定義があり、.NET ランタイム本体を含まないことを確認。
- タブ追加・削除、1／左右2画面、文字サイズ80～250%、基本ファイル操作に対応。

未署名のプレビュー版です。開発 PC ではアプリケーション制御により対話操作の確認ができていません。コピーの中止・失敗時にコピー先に一部が残る場合があります。異なるドライブ間のフォルダー移動と操作の取り消しは未対応です。

詳細は [README](https://github.com/konikatsu/nikotFiler#readme) を参照してください。
