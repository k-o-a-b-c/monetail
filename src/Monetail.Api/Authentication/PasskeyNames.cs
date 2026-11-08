using System.Formats.Cbor;
using Microsoft.AspNetCore.Identity;

namespace Monetail.Api.Authentication;

/// <summary>Identity検証後の表示名だけを決める。認証・認可・安全性の判定には使わない。</summary>
public static class PasskeyNames
{
    // MITのpasswordless-id/webauthnの主要項目。出典・ライセンスは同梱NOTICEを参照。
    private static readonly IReadOnlyDictionary<Guid, string> Providers = new Dictionary<Guid, string>
    {
        [new("ea9b8d66-4d01-1d21-3ce4-b6b48cb575d4")] = "Google パスワード マネージャー",
        [new("bada5566-a7aa-401f-bd96-45619a55120d")] = "1Password",
        [new("08987058-cadc-4b81-b6e1-30de50dcbe96")] = "Windows Hello",
        [new("9ddd1817-af5a-4672-a2b9-3e3dd95000a9")] = "Windows Hello",
        [new("6028b017-b1d4-4c02-b4b3-afcdafc96bb2")] = "Windows Hello",
        [new("d548826e-79b4-db40-a3d8-11116f7e8349")] = "Bitwarden",
        [new("531126d6-e717-415c-9320-3d9aa6981239")] = "Dashlane",
        [new("fbfc3007-154e-4ecc-8c0b-6e020557d7bd")] = "Apple パスワード",
        [new("dd4ec289-e01d-41c9-bb89-70fa845d4bf2")] = "iCloud キーチェーン（管理対象）"
    };

    /// <summary>登録検証に成功した鍵のCBORを標準Readerで読み、解析不能でも登録を妨げない。</summary>
    public static string ForVerifiedRegistration(UserPasskeyInfo passkey)
    {
        try
        {
            var reader = new CborReader(passkey.AttestationObject, CborConformanceMode.Strict);
            reader.ReadStartMap();
            byte[]? authenticatorData = null;
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                if (reader.ReadTextString() == "authData") authenticatorData = reader.ReadByteString();
                else reader.SkipValue();
            }
            reader.ReadEndMap();
            // rpIdHash(32) + flags(1) + signCount(4) の次がAAGUID(16)。ATフラグ必須。
            if (reader.BytesRemaining != 0 || authenticatorData is not { Length: >= 55 } data || (data[32] & 0x40) == 0)
                return "パスキー";
            // WebAuthnのUUIDはネットワーク順。.NET既定の混在エンディアンへ取り違えない。
            var aaguid = new Guid(data.AsSpan(37, 16), bigEndian: true);
            return Providers.GetValueOrDefault(aaguid, "パスキー");
        }
        catch (Exception error) when (error is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return "パスキー";
        }
    }
}
