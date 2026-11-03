# 43 パスキー認証の設計

このファイルは認証仕様の**分野別案内**と**旧見出しリンクの互換入口**です。Codexは対象の文書だけを読み、すべての仕様を一括で読み込まないでください。確定済みの契約と未実装事項は各分野の正本に保持しています。

## 分野別の正本

| 対象 | 文書 |
| --- | --- |
| パスキー・アカウント・登録・鍵の追加／削除・全失効と復旧 | [パスキーと登録・復旧](auth-passkeys.md) |
| 認証DB・セッション期限・Cookie・CSRF・チャレンジ | [セッションとHTTP保護](auth-session-security.md) |
| WindowsのOAuth・PKCE・DPAPI・トークン復元 | [Windows版OAuth](auth-windows-oauth.md) |
| 認証／管理APIのエンドポイント・再送・対象条件 | [認証・管理API契約](auth-api-contract.md) |

起動設定・運用は[42](42-api-foundation.md)、家計簿側は[58](58-ledger-foundation-design.md)、検証結果は[tests/README](../tests/README.md)を参照してください。

## 旧見出しへのリンク

既存の `43-passkey-authentication-design.md#...` は下記の見出しから各正本へ辿れます。新規参照は分野別ファイルへ直接リンクしてください。

<a id="43-パスキー認証の設計"></a> [43 パスキー認証の設計](auth-passkeys.md#43-パスキー認証の設計)

<a id="アカウントと人物"></a> [アカウントと人物](auth-passkeys.md#アカウントと人物)

<a id="実装と導入の境界"></a> [実装と導入の境界](auth-passkeys.md#実装と導入の境界)

<a id="認証コードの境界"></a> [認証コードの境界](auth-passkeys.md#認証コードの境界)

<a id="普段のログイン"></a> [普段のログイン](auth-passkeys.md#普段のログイン)

<a id="登録コードと全失効"></a> [登録コードと全失効](auth-passkeys.md#登録コードと全失効)

<a id="アカウント登録の招待リンクdec-050未実装"></a> [アカウント登録の招待リンク（DEC-050・未実装）](auth-passkeys.md#アカウント登録の招待リンクdec-050未実装)

<a id="保存と移行"></a> [保存と移行](auth-session-security.md#保存と移行)

<a id="セッションとhttpの境界"></a> [セッションとHTTPの境界](auth-session-security.md#セッションとhttpの境界)

<a id="windows版のoauth認証dec-051"></a> [Windows版のOAuth認証（DEC-051）](auth-windows-oauth.md#windows版のoauth認証dec-051)

<a id="windows版の認証情報保存"></a> [Windows版の認証情報保存](auth-windows-oauth.md#windows版の認証情報保存)

<a id="api契約"></a> [API契約](auth-api-contract.md#api契約)

<a id="運用と家計簿の境界"></a> [運用と家計簿の境界](auth-api-contract.md#運用と家計簿の境界)

<a id="管理apiの再送と対象条件"></a> [管理APIの再送と対象条件](auth-api-contract.md#管理apiの再送と対象条件)

<a id="根拠"></a> [根拠](auth-passkeys.md#根拠)
