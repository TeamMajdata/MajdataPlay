[English](README.md) / [日本語](README.ja.md)

---

# Cubism Unity Components

Unity用のCubism SDKのオープンコンポーネントです。

モデルをロードするには Live2D Cubism Core と組み合わせて使用します。

SDKパッケージのダウンロードページをお探しの場合は、[ダウンロードページ](https://www.live2d.com/download/cubism-sdk/download-unity/)にアクセスしてください。

## ライセンス

ご使用前に[ライセンス](LICENSE.md)をお読みください。

## お知らせ

ご使用前に[お知らせ](NOTICE.ja.md)をお読みください。

## Cubism 5.3新機能や過去バージョンとの互換性について

本 SDK はCubism 5.3に対応した製品です。  
Cubism 5.3 Editorに搭載された新機能のSDK対応については [こちら](https://docs.live2d.com/cubism-sdk-manual/cubism-5-3-new-functions/)をご確認ください。  
過去バージョンのCubism SDKとの互換性については [こちら](https://docs.live2d.com/cubism-sdk-manual/compatibility-with-cubism-5-3/)をご確認ください。

## 動作要件

本 SDK は **Universal Render Pipeline（URP）** を前提としており、モデルの描画にはカスタムレンダーパスの利用が必要です。
詳しくは[Cubism 5 SDK for Unity R4_1 以前との違い](https://docs.live2d.com/cubism-sdk-manual/differences-from-before-unity-r4_1/) や [Built-in Render PipelineからURPへの移行について](https://docs.live2d.com/cubism-sdk-manual/migration-from-birp-to-urp/) をご確認ください。
なお**Built-in Render Pipeline** および **High Definition Render Pipeline（HDRP）** はサポートしておりません。

**Input System** パッケージ（`com.unity.inputsystem`）との併用を想定しています。旧来の Input Manager のみのプロジェクトでは、Unity の手順に従い Input System の追加やプロジェクト設定の調整が必要になる場合があります。

## 構造

### コンポーネント

コンポーネントは役割ごとにグループ化されており、このグループ化はフォルダー構造と名前空間の両方に反映されます。

#### Coreラッパー

このグループのコンポーネントとクラスは、CubismコアライブラリをC#とUnityにラップするためのレイヤーであり、`./Assets/Live2D/Cubism/Core`にあります。

#### フレームワーク

このグループのコンポーネントとクラスは、リップシンクやCubismの組み込み用ファイルとUnityの統合などの追加機能を提供します。CubismファイルをプレハブとAnimationClipに変換する機能はここにあります。すべてのフレームワークコードは`./Assets/Live2D/Cubism/Framework`にあります。

#### レンダリング

このグループのコンポーネントとクラスは、Unityの機能を使用してCubismモデルをレンダリングする機能を提供します。コードは、`./Assets/Live2D/Cubism/Rendering`にあります。

### エディター拡張機能

Unity Editor拡張機能は、`./Assets/Live2D/Cubism/Editor`にあります。

### リソース

シェーダー等のアセットのリソースは、`./Assets/Live2D/Cubism/Rendering/Resources`にあります。

## 開発環境

| Unity | バージョン |
| --- | --- |
| LTS | 6000.3.11f1 |
| LTS | 6000.0.71f1 |

| ライブラリ / ツール | バージョン |
| --- | --- |
| Android SDK / NDK | *2 |
| Visual Studio 2022 | 17.14.29 |
| Windows SDK | 10.0.26100.7705 |
| Xcode | 26.3 |

*2 Unityに組み込まれたライブラリまたは推奨ライブラリを使用してください。

### C#コンパイラ

Unity2018.4以降でサポートされているRoslynまたはmcsコンパイラを使用してビルドします。

注：mcsコンパイラは非推奨であり、ビルドのみをチェックします。

使用できるC#のバージョンについては、次の公式ドキュメントを参照してください。

https://docs.unity3d.com/ja/2018.4/Manual/CSharpCompiler.html

## テスト済みの環境

| プラットフォーム | バージョン |
| --- | --- |
| Android | 16 |
| iOS | 26.4 |
| iPadOS | 26.4 |
| Ubuntu | 24.04.3 |
| macOS | 26.4 |
| Windows 11 | 25H2 (*4) |
| Google Chrome | 146.0.7680.165 |

*4 UWP向けビルドは動作確認をしておりません。

## ブランチ

最新の機能や修正をお探しの場合、`develop`ブランチをご確認ください。

`master`ブランチは、公式のSDKリリースごとに`develop`ブランチと同期されます。

## 使用法

`./Assets`の下にあるすべてのファイルを、Unityプロジェクト内のLive2DCubismSDKがあるフォルダーにコピーしてください。

### unsafeブロック

Coreラッパーでは、unsafeコードのブロックを許可する必要があり、Unityが作成するC#プロジェクトにはそれに応じてパッチが適用されます。unsafeコードを選択できない場合、現在のところ最善の方法は、コンポーネントをコンパイルして、そのdllをUnityプロジェクトに適用することです。

## プロジェクトへの貢献

プロジェクトに貢献する方法はたくさんあります。バグのログの記録、このGitHubでのプルリクエストの送信、Live2Dコミュニティでの問題の報告と提案の作成です。

### フォークとプルリクエスト

修正、改善、さらには新機能をもたらすかどうかにかかわらず、プルリクエストに感謝します。メインリポジトリを可能な限りクリーンに保つために、必要に応じて個人用フォークと機能ブランチを作成してください。

### バグ

Live2Dコミュニティでは、問題のレポートと機能リクエストを定期的にチェックしています。バグレポートを提出する前に、Live2Dコミュニティで検索して、問題のレポートまたは機能リクエストがすでに投稿されているかどうかを確認してください。問題がすでに存在する場合は、関連するコメントを追記してください。

### 提案

SDKの将来についてのフィードバックにも関心があります。Live2Dコミュニティで提案や機能のリクエストを送信できます。このプロセスをより効果的にするために、それらをより明確に定義するのに役立つより多くの情報を含めるようお願いしています。

## コーディングガイドライン

### ネーミング

可能な限り、[Microsoftガイドライン](https://msdn.microsoft.com/en-us/library/ms229002(v=vs.110).aspx)に準拠するようにしてください。プライベートフィールドには、アンダースコアで始まる小文字の名前を付けます。

### スタイル

- Unity Editor拡張機能では、LINQやその他すべての凝ったものを使って表現力豊かなコードを書いてみてください。
- それ以外の場所ではLINQを使用せず、`foreach`よりも`for`を優先してください。
- アクセス修飾子を明示的にするようにしてください。`void Update()`ではなく `private void Update()`を使いましょう。

## フォーラム

ご不明な点がございましたら、公式のLive2Dフォーラムに参加して、他のユーザーと話し合ってください。

- [Live2D 公式クリエイターズフォーラム](https://creatorsforum.live2d.com/)
- [Live2D Creator's Forum(English)](https://community.live2d.com/)
