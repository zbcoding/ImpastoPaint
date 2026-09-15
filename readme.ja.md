# Impasto

[🇬🇧 English](readme.md) · 機械翻訳です。修正はプルリクエストでお寄せください。

Impasto は、Linux、Windows、macOS 向けのペイント・画像編集アプリケーションです。

Impasto は、手早いペイント、画像の編集、切り抜き、サイズ変更、レイヤーベースの編集に最適です。
テキストツールで文字を描き、シェイプツールで自由な線を描き、選択ツールで画像レイヤーの一部をコピー＆ペーストできます。
下のスクリーンショットをご覧ください。

![編集可能なオブジェクトとしてのシェイプとテキスト、オブジェクトごとの履歴付き](docs/screenshots/impasto-object-layers.png)

![テキストツールと、開いた状態の UI 設定ダイアログ](docs/screenshots/impasto-text-tool.png)

![キャンバスの中心線上で保持されたシェイプの制御点と、ドラッグ中に表示されるガイド](docs/screenshots/impasto-snap-to-grid.png)
注目の機能: 移動するものや描くものが、キャンバスのグリッド、ルーラーの単位、あるいはキャンバスの端や中心線にスナップします。

## ダウンロード

Linux、Windows、macOS 向けのビルドは
[リリースページ](https://github.com/zbcoding/ImpastoPaint/releases/latest)にあります。Windows と
macOS のビルドには署名がないため、SmartScreen や Gatekeeper が警告を表示します。

Impasto には GTK 4.18 以降が必要で、これが Linux で動作する環境を左右します:

- **Flatpak**: [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) または
  リリースページから入手できます。GTK を同梱しているため、Ubuntu 22.04 や 24.04 を含め、
  flatpak が動作する環境ならどこでも実行できます。迷った場合はこちらを使ってください。
- **AppImage**（`Impasto-x86_64.AppImage`）: GTK を同梱しているのでシステムの GTK は不要ですが、
  このバンドルは **glibc 2.43** に対してビルドされているため、それより古い環境では起動しません。
  つまり Ubuntu 26.04、Fedora 44、Arch では動作しますが、Ubuntu 24.04、Ubuntu 22.04、Fedora 43、
  Debian 13 では動作しません。その場合は Flatpak を選んでください。
- **Zip**（`Impasto-linux-dotnet-*.zip`）: ディストリビューションが提供する GTK を使うため、
  GTK 4.18 以降があらかじめインストールされている必要があります。

Impasto は MIT License の下でライセンスされています（`license-mit.txt` を参照）。サードパーティの
クレジット表記、告知、ライセンス文は `THIRD-PARTY-NOTICES.md` に記載されています。

## Pinta との関係

Impasto は新しく独立したプロジェクトです。MIT ライセンスで公開されている
[Pinta](https://github.com/PintaProject/Pinta) のソースコードを出発点としています。Pinta 自体も
[Paint.NET™](https://www.getpaint.net/) に影響を受けた GTK アプリケーションです。
Pinta のコントリビューターは Impasto アプリケーション内に記載されています。
Impasto は Pinta プロジェクトや同じコントリビューターによって保守されているものではありません。

<details>
<summary><h2>Windows でのビルド</h2></summary>

まず、必要な GTK 関連の依存パッケージをインストールします:
- [MSYS2](https://www.msys2.org) をインストールします
- CLANG64 ターミナルから `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader` を実行します。
  - ARM64 版 Windows では `CLANGARM64` ターミナルを使い、`clang-x86_64` を `clang-aarch64` に置き換えてください。

その後、[Visual Studio](https://visualstudio.microsoft.com/) で `Pinta.sln` を開くとアプリケーションをビルドできます。
Visual Studio インストーラーで .NET 10 がインストールされていることを確認してください。

コマンドラインでビルドする場合:
- [.NET 10 SDK をインストールします](https://dotnet.microsoft.com/)。
- ビルド:
  - `dotnet build`
- 実行:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>macOS でのビルド</h2></summary>

- .NET 10 と GTK4 をインストールします
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - Apple Silicon では、アプリケーションが GTK ライブラリを読み込めるように環境変数 `DYLD_LIBRARY_PATH=/opt/homebrew/lib` を設定します
  - Intel では、アプリケーションが GTK ライブラリを読み込めるように環境変数 `DYLD_LIBRARY_PATH=/usr/local/lib` を設定します
- ビルド:
  - `dotnet build`
- 実行:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Linux でのビルド</h2></summary>

- お使いの Linux ディストリビューション向けの手順に従って [.NET 10](https://dotnet.microsoft.com/) をインストールします。
- その他の依存パッケージをインストールします（手順は Ubuntu 22.10 向けですが、他のディストリビューションでもほぼ同様です）:
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - ライブラリの最低バージョン: `gtk` >= 4.18 および `libadwaita` >= 1.8
  - 任意の依存パッケージ: `webp-pixbuf-loader`
- ビルド（方法 1、開発・テスト向け）:
  - `dotnet build`
  - `dotnet run --project Impasto`
- ビルド（方法 2、インストール向け）:
  - `./autogen.sh`
    - tarball からビルドする場合は、代わりに `./configure` を実行します。
    - `/usr/local` 以外のディレクトリにインストールするには、`--prefix=<インストール先ディレクトリ>` 引数を追加します。
  - `make install`

</details>

## ヘルプの入手 / 貢献について:

貢献を歓迎します。要点は次のとおりです:

- **コード** — 最も手軽な貢献方法は、Issue を作成して提案や要望を説明することです。
  コードスニペット、ファイル名、背景となる情報を含めてください。
  PR を送っていただくこともできます。無償版への PR として受け取った貢献は、
  Impasto の Premium 版や、MIT ライセンスの他のソフトウェアで利用される場合があります。
- **AI コーディングツール** — 歓迎します。提出するコードを自分で説明でき、
  プロジェクトのアーキテクチャに合わせて調整できるようにしてください。AI の支援を受けたコードは、
  マージ前に追加のレビューを受けることがあります。
- **翻訳** — プルリクエストとして貢献してください。新しい `.po` ファイルを AI で下書きし、
  その後その言語の話者が編集・校正する形を想定しています。

貢献の前に、git / PR のワークフローを含む完全なガイドを `CONTRIBUTING.md` でお読みください。

- [バグ / 問題](https://github.com/zbcoding/ImpastoPaint/issues)を報告できます。
- 各リリースの主な変更点は `CHANGELOG.md` に記録されています。
