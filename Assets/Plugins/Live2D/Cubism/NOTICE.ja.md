[English](NOTICE.md) / [日本語](NOTICE.ja.md)

---

# お知らせ

## [制限事項] HarmonyOS NEXT及びTuanjieへの対応について (2026-04-02)

Cubism SDK for Unity R5 beta3以降ではRender Graph Moduleを利用しているため、HarmonyOS NEXT及びTuanjieに対応しておりません。

本製品での対応時期につきましては、TuanjieがRender Graph Moduleをサポート次第、今後のリリースを持ってお知らせいたします。


## [制限事項] オフスクリーン描画の描画順が変更されたときの、オフスクリーン描画が属するパーツ以下のDrawableの描画順について (2026-01-08)

Cubism SDK for Unity上でオフスクリーン描画の描画順が変更された際、オフスクリーン描画が属するパーツに含まれるDrawableやオフスクリーン描画の描画順は変更されません。
今後、オフスクリーン描画の描画順の変更と連動してオフスクリーン描画が属するパーツに含まれるDrawableやオフスクリーン描画の描画順が変更される機能の追加を検討しております。

対応バージョンや時期につきましては今後のリリースをもってお知らせいたします。


## [制限事項] WebGL書き出し時のAudioClipからのリップシンク対応について (2024-11-28)

Cubism SDK for Unityの音声からのリップシンクは、波形情報の取得にAudioClipのAPIを利用しています。
しかし、AudioClipから動的に波形情報を取得するためのAPIはUnityからWebGLへの書き出しに非対応となっているため、Cubism SDK for UnityのリップシンクもWebGL書き出しには非対応となります。

詳しくは、Unity社 公式ドキュメントをご確認ください。

* [WebGL のオーディオ](https://docs.unity3d.com/ja/current/Manual/webgl-audio.html)


## [制限事項] Windows ARM64向けの対応状況について (2024-01-18)

Unity 2023.1以降にて指定可能となったWindows ARM64向けビルドにつきまして、Cubism SDK for Unityは現在対応しておりません。
対応バージョンや時期につきましては今後のリリースをもってお知らせいたします。


## [注意事項] Apple社のPrivacy Manifest Policy対応について

Apple社が対応を必要としているPrivacy Manifest Policyについて、本製品では指定されているAPI及びサードパーティ製品を使用しておりません。
もし本製品で対応が必要と判断した場合、今後のアップデートにて順次対応する予定です。
詳しくはApple社が公開しているドキュメントをご確認ください。

[Privacy updates for App Store submissions](https://developer.apple.com/news/?id=3d8a9yyh)


## [注意事項] macOS Catalina 以降での `.bundle` と `.dylib` の利用について

macOS Catalina 以降で `.bundle` と `.dylib` を利用する際、公証の確認のためオンライン環境に接続している必要があります。

詳しくは、Apple社 公式ドキュメントをご確認ください。

* [Apple社 公式ドキュメント](https://developer.apple.com/documentation/security/notarizing_your_app_before_distribution)
---

©Live2D
