using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class ParticipantsWorkspaceTests
{
    private static readonly Guid Ledger = Guid.NewGuid(), Me = Guid.NewGuid();
    private static readonly MemberInfo Own = new(Me, "同じ名前", "Admin", null, null, null, DateTimeOffset.UtcNow, Guid.NewGuid());
    private static readonly MemberInfo Other = new(Guid.NewGuid(), "同じ名前", "Viewer", null, null, null, DateTimeOffset.UtcNow.AddDays(-1), Guid.NewGuid());
    private static readonly MembersInfo Members = new([Own, Other], Me, "Admin");
    private static readonly MemberEditInfo Edit = new(Other, [new(Guid.NewGuid(), "使用中の人物", false), new(Guid.NewGuid(), "未使用", true)]);
    private static readonly LedgerInvitationInfo Invitation = new(Guid.NewGuid(), "https://synthetic-api.example/ledger-invitation#token=" + new string('S', 43), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

    [Fact]
    public async Task ListPreservesServerOrderAndDistinguishesSameNamesByAccountId()
    {
        var reversed = Members with { Members = [Other, Own] };
        using var fixture = new Fixture(_ => Json(reversed));
        var list = await fixture.Workspace.ListAsync();
        Assert.Equal(new[] { Other.AccountId, Me }, list.Members.Select(row => row.AccountId));
        Assert.Equal(2, list.Members.Count(row => row.DisplayName == "同じ名前"));
        Assert.Single(fixture.Requests);
    }
    [Fact]
    public async Task EditReadsMemberAndDisabledPersonCandidatesInOneRequest()
    {
        using var fixture = new Fixture(_ => Json(Edit));
        var edit = await fixture.Workspace.EditAsync(Other.AccountId);
        Assert.Equal(Other, edit.Member);
        Assert.False(edit.PersonCandidates[0].IsSelectable);
        Assert.True(edit.PersonCandidates[1].IsSelectable);
        Assert.EndsWith($"/members/{Other.AccountId:N}/edit", Assert.Single(fixture.Requests).Path);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveUsesOriginalRevisionAndExplicitNullWithoutExtraGet(bool self)
    {
        using var fixture = new Fixture(_ => Json(Members));
        var member = self ? Own : Other;
        await fixture.Workspace.SaveAsync(member, self ? "Admin" : "Editor", null, Me);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(member.Revision, body.RootElement.GetProperty("revision").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("personId").ValueKind);
        Assert.Equal(!self, body.RootElement.TryGetProperty("role", out _));
        Assert.EndsWith(member.AccountId.ToString("N"), request.Path);
    }
    [Fact]
    public async Task SelfRoleChangeAndExclusionNeverSendRequests()
    {
        using var fixture = new Fixture(_ => Json(Members));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workspace.SaveAsync(Own, "Viewer", null, Me));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Workspace.ExcludeAsync(Own, Me));
        Assert.Empty(fixture.Requests);
    }
    [Fact]
    public async Task ExclusionUsesRevisionBodyAndReturnsUpdatedList()
    {
        using var fixture = new Fixture(_ => Json(Members with { Members = [Own] }));
        var updated = await fixture.Workspace.ExcludeAsync(Other, Me);
        Assert.Single(updated.Members);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(Other.Revision, JsonNode.Parse(request.Body!)!["revision"]!.GetValue<Guid>());
    }
    [Fact]
    public async Task RevisionConflictCarriesCompleteEditSnapshotWithoutFetchingOrRetrying()
    {
        using var fixture = new Fixture(_ => Json(new { code = "revision_conflict", latest = Edit }, HttpStatusCode.Conflict));
        var error = await Assert.ThrowsAsync<ParticipantRequestException>(() => fixture.Workspace.SaveAsync(Other, "Editor", null, Me));
        Assert.Equal(Edit.Member, error.Latest!.Member);
        Assert.Equal(Edit.PersonCandidates, error.Latest.PersonCandidates);
        Assert.Single(fixture.Requests);
    }
    [Theory]
    [InlineData(403, "Editor")]
    [InlineData(404, null)]
    [InlineData(404, "Admin")]
    public async Task PermissionFailureCarriesCurrentContextWithoutAnotherRequest(int status, string? role)
    {
        using var fixture = new Fixture(_ => Json(new { code = "forbidden", permissions = new { currentRole = role, myPersonId = (Guid?)null } }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ParticipantRequestException>(() => fixture.Workspace.ListAsync());
        Assert.Equal(role, error.Permissions!.CurrentRole);
        Assert.Null(error.Latest);
        Assert.Single(fixture.Requests);
    }
    [Theory]
    [InlineData(400, "invalid_input", "role")]
    [InlineData(409, "person_already_assigned", "personId")]
    [InlineData(429, null, null)]
    public async Task ConstraintsAndRateLimitAreTypedWithoutLosingAuthentication(int status, string? code, string? field)
    {
        using var fixture = new Fixture(_ => Json(new { code, field }, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<ParticipantRequestException>(() => fixture.Workspace.SaveAsync(Other, "Editor", null, Me));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal(code, error.Code); Assert.Equal(field, error.Field);
        Assert.NotNull(fixture.Store.Current);
        Assert.Single(fixture.Requests);
    }
    [Fact]
    public async Task FailedInvitationCreationReusesRequestIdAndSuccessDoesNotFetchListAgain()
    {
        var count = 0;
        using var fixture = new Fixture(_ => ++count == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(new { invitation = Invitation, invitations = new[] { Invitation } }));
        var draft = new LedgerInvitationDraft();
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.CreateInvitationAsync(draft));
        Assert.Equal(Invitation, Assert.Single((await fixture.Workspace.CreateInvitationAsync(draft)).Invitations));
        Assert.Equal(fixture.Requests[0].Body, fixture.Requests[1].Body);
        Assert.Equal(draft.RequestId, JsonNode.Parse(fixture.Requests[1].Body!)!["requestId"]!.GetValue<Guid>());
        Assert.NotEqual(draft.RequestId, new LedgerInvitationDraft().RequestId);
        Assert.Equal(2, fixture.Requests.Count);
    }
    [Fact]
    public async Task ExistingInvitationCanBeCopiedAndCancellationUsesResponseList()
    {
        using var fixture = new Fixture(request => request.Method == HttpMethod.Get ? Json(new LedgerInvitationsInfo([Invitation])) : Json(new LedgerInvitationsInfo([])));
        Assert.Equal(Invitation.Url, Assert.Single((await fixture.Workspace.InvitationsAsync()).Invitations).Url);
        Assert.Empty((await fixture.Workspace.CancelInvitationAsync(Invitation.InvitationId)).Invitations);
        Assert.DoesNotContain("#token=", Invitation.ToString());
        Assert.DoesNotContain(Invitation.Url, Invitation.ToString());
        Assert.Equal(2, fixture.Requests.Count);
        Assert.All(fixture.Requests, request => Assert.DoesNotContain("token=", request.Path));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("missingSelf")]
    [InlineData("role")]
    [InlineData("revision")]
    [InlineData("person")]
    [InlineData("date")]
    public async Task InvalidMemberPayloadIsRejected(string change)
    {
        var json = JsonSerializer.SerializeToNode(Members, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var member = json["members"]![0]!;
        switch (change)
        {
            case "duplicate": json["members"]!.AsArray().Add(member.DeepClone()); break;
            case "missingSelf": json["currentAccountId"] = Guid.NewGuid(); break;
            case "role": member["role"] = "Owner"; break;
            case "revision": member["revision"] = Guid.Empty; break;
            case "person": member["personId"] = Guid.NewGuid(); break;
            case "date": member["joinedAtUtc"] = "invalid"; break;
        }
        using var fixture = new Fixture(_ => Json(json));
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.ListAsync());
    }
    [Theory]
    [InlineData("https://other.example/ledger-invitation#token=")]
    [InlineData("http://synthetic-api.example/ledger-invitation#token=")]
    [InlineData("https://synthetic-api.example/auth/enroll#token=")]
    [InlineData("https://synthetic-api.example/ledger-invitation?token=secret#token=")]
    [InlineData("https://user@synthetic-api.example/ledger-invitation#token=")]
    public async Task InvitationCannotCopyForeignOriginOrAccountRegistrationLink(string prefix)
    {
        using var fixture = new Fixture(_ => Json(new LedgerInvitationsInfo([Invitation with { Url = prefix + new string('S', 43) }])));
        await Assert.ThrowsAsync<ApiConnectionException>(() => fixture.Workspace.InvitationsAsync());
    }
    [Fact]
    public async Task TokenRefreshRetainsMemberWriteAndNeverRequestsAccountEnrollment()
    {
        var first = true;
        using var fixture = new Fixture(request => request.RequestUri!.AbsolutePath == "/connect/token"
            ? Json(new { access_token = "new", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 900 })
            : first ? Reject() : Json(Members));
        HttpResponseMessage Reject() { first = false; return new(HttpStatusCode.Unauthorized); }
        await fixture.Workspace.SaveAsync(Other, "Editor", null, Me);
        Assert.Equal(fixture.Requests[0].Body, fixture.Requests[2].Body);
        Assert.All(fixture.Requests, request => Assert.DoesNotContain("enrollment", request.Path));
    }

    private static HttpResponseMessage Json(object data, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(data) };
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        internal PeopleWorkspaceTests.Store Store { get; } = new();
        internal List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
        internal ParticipantsWorkspace Workspace { get; }
        internal Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            http = new(new Handler(request =>
            {
                Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));
                return respond(request);
            }));
            var api = new MonetailApiClient(endpoint, http);
            Workspace = new(api, new(api, endpoint, Store), Ledger);
        }
        public void Dispose() => http.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(action(request));
    }
}
