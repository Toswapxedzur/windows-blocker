# Windows ボルト

Windows Vault は、Vault 製品ファミリーのネイティブ Windows メンバーです。これは、WebView2 エディター、ネイティブ アプリケーション インベントリ、強制エンジン、カスタム ルール ランタイム、およびローカル Web アプリ ブリッジ ハブを備えた .NET 8 WPF アプリケーションです。

コードは製品契約です。維持されているアプリ内マニュアルは [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md) です。

## 現在の機能

- 選択した Windows アプリケーションのデフォルト グループと、高度なポリシー ルールのカスタム グループ。
- 即時モード、許可モード、およびカウントダウン モード;スケジュール。凍結する。居眠りをする。およびグループのインポート/エクスポート。
- Windows アプリケーション インベントリとウィンドウ ベースの強制コンポーネント。
- `src/WindowsBlocker/WebAssets/` がホストする WebView2 エディター。
- 構文チェックとログ フィードによるカスタム ルールの実行の制御。
- 明示的にリンクされた互換性のあるグループ用のループバック ブリッジ ハブ。
- ネイティブ タイマー、トースト、パネル オーバーレイ ウィンドウ。

## ビルドする

チェックインしたソリューションとプロジェクトを使用します。

```powershell
dotnet build WindowsBlocker.sln
```

アプリケーション プロジェクトは `net8.0-windows` をターゲットとし、WPF と WebView2 を使用します。必要な .NET SDK と WebView2 ランタイムが利用可能な Windows 上でビルドして実行します。

## プロジェクトマップ

|エリア |ソースディレクトリ |
| --- | --- |
|グループモデルとポリシーの評価 | `src/WindowsBlocker/Core/` |
|ネイティブ強制 | `src/WindowsBlocker/Enforcement/` |
|アプリ インベントリと WebView ブリッジ | `src/WindowsBlocker/WebUI/` |
|カスタムルールランタイム | `src/WindowsBlocker/Rules/` |
|ブリッジハブ | `src/WindowsBlocker/Bridge/` |
| WPF ウィンドウとオーバーレイ | `src/WindowsBlocker/` |

## ドキュメントと翻訳

英語の文書は正規のままです。 UI ラベルは、`src/WindowsBlocker/WebAssets/translation/` の完全な JSON カタログを使用します。翻訳されたマニュアルは `manual/en.md` の横にあり、残りの保守ドキュメントの翻訳されたコピーは `i18n-docs/<locale>/` の下にあります。
