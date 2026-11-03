using Monetail.Api.Authentication;

namespace Monetail.Api.Tests;

/// <summary>テストだけで発行URLから合成トークンを取り出す。製品の応答DTOへ生値を追加しない。</summary>
internal static class EnrollmentTestTokens
{
    public static string Token(this IssuedEnrollmentInvitation invitation) => invitation.EnrollmentUrl.EnrollmentToken();
    public static string EnrollmentToken(this string url) => new Uri(url).Fragment["#token=".Length..];
}
