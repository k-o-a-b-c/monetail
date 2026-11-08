using System.Formats.Cbor;
using Microsoft.AspNetCore.Identity;
using Monetail.Api.Authentication;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class PasskeyNamingTests
{
    public static TheoryData<string, string> Known => new()
    {
        { "ea9b8d66-4d01-1d21-3ce4-b6b48cb575d4", "Google パスワード マネージャー" },
        { "bada5566-a7aa-401f-bd96-45619a55120d", "1Password" },
        { "08987058-cadc-4b81-b6e1-30de50dcbe96", "Windows Hello" },
        { "9ddd1817-af5a-4672-a2b9-3e3dd95000a9", "Windows Hello" },
        { "6028b017-b1d4-4c02-b4b3-afcdafc96bb2", "Windows Hello" },
        { "d548826e-79b4-db40-a3d8-11116f7e8349", "Bitwarden" },
        { "531126d6-e717-415c-9320-3d9aa6981239", "Dashlane" },
        { "fbfc3007-154e-4ecc-8c0b-6e020557d7bd", "Apple パスワード" },
        { "dd4ec289-e01d-41c9-bb89-70fa845d4bf2", "iCloud キーチェーン（管理対象）" }
    };

    [Theory, MemberData(nameof(Known))]
    public void KnownNetworkOrderAaguidsHaveExpectedNames(string aaguid, string expected) =>
        Assert.Equal(expected, Name(Attestation(Convert.FromHexString(aaguid.Replace("-", "")))));

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("unknown")]
    [InlineData("mixed-endian")]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("truncated")]
    [InlineData("not-map")]
    [InlineData("short-data")]
    [InlineData("no-at")]
    [InlineData("wrong-type")]
    [InlineData("nested-spoof")]
    [InlineData("trailing")]
    [InlineData("duplicate")]
    public void MissingOrUnusableAttestationFallsBackWithoutThrowing(string mode)
    {
        var bytes = Convert.FromHexString("bada5566a7aa401fbd9645619a55120d");
        var writer = new CborWriter(CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        writer.WriteStartMap(null);
        if (mode is "nested-spoof") { writer.WriteTextString("attStmt"); writer.WriteStartMap(1); }
        if (mode != "missing")
        {
            writer.WriteTextString("authData");
            if (mode == "wrong-type") writer.WriteTextString("invalid");
            else writer.WriteByteString(mode == "short-data" ? [1] : Data(mode switch {
                "zero" => new byte[16], "unknown" => Enumerable.Repeat((byte)7, 16).ToArray(),
                "mixed-endian" => new Guid("bada5566-a7aa-401f-bd96-45619a55120d").ToByteArray(), _ => bytes
            }, mode == "no-at" ? (byte)0 : (byte)0x40));
        }
        if (mode == "nested-spoof") writer.WriteEndMap();
        if (mode == "duplicate") { writer.WriteTextString("authData"); writer.WriteByteString(Data(bytes)); }
        writer.WriteEndMap();
        if (mode == "trailing") writer.WriteInt32(0);
        byte[]? encoded = mode switch {
            "empty" => [], "null" => null, "truncated" => [0xa1, 0x68], "not-map" => [0x80], _ => writer.Encode()
        };
        Assert.Equal("パスキー", Name(encoded!));
    }

    [Fact]
    public void IndefiniteCborAndUnknownNestedFieldsAreHandledStructurally()
    {
        var data = Data(Convert.FromHexString("bada5566a7aa401fbd9645619a55120d"));
        var writer = new CborWriter();
        writer.WriteStartMap(null);
        writer.WriteTextString("attStmt"); writer.WriteStartArray(1); writer.WriteStartMap(1);
        writer.WriteTextString("authData"); writer.WriteTextString("not the root authData"); writer.WriteEndMap(); writer.WriteEndArray();
        writer.WriteTextString("authData"); writer.WriteStartIndefiniteLengthByteString();
        writer.WriteByteString(data[..20]); writer.WriteByteString(data[20..]); writer.WriteEndIndefiniteLengthByteString();
        writer.WriteTextString("fmt"); writer.WriteTextString("none"); writer.WriteEndMap();
        Assert.Equal("1Password", Name(writer.Encode()));
    }
    private static byte[] Data(byte[] aaguid, byte flags = 0x40) => [.. new byte[32], flags, 0, 0, 0, 0, .. aaguid, 0, 1, 1];
    private static byte[] Attestation(byte[] aaguid)
    {
        var writer = new CborWriter(); writer.WriteStartMap(1); writer.WriteTextString("authData");
        writer.WriteByteString(Data(aaguid)); writer.WriteEndMap(); return writer.Encode();
    }
    private static string Name(byte[] attestation) => PasskeyNames.ForVerifiedRegistration(
        new UserPasskeyInfo([1], [2], DateTimeOffset.UnixEpoch, 0, [], true, false, false, attestation, []));
}
