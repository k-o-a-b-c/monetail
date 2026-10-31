using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monetail.Api.Features.Monetail.Ledgers;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>実HTTPと隔離SQLiteで、人物・参加者・招待の権限と保存の原子性を確認する。</summary>
public sealed class LedgerManagementTests
{
    [Fact]
    public async Task PersonReplaySurvivesRenameAndDeletionWithoutRecreation()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var id = await LedgerAsync(client);
        var requestId = Guid.NewGuid();
        var input = new { requestId, name = "  父  ", validFrom = "2026-04-01", validTo = (string?)null };
        var first = await Call(client, "POST", $"/ledgers/{id}/people", input, HttpStatusCode.Created);
        var person = first.GetProperty("people")[0];
        Assert.Equal("父", person.GetProperty("name").GetString());
        Assert.Equal("2026-04-01", person.GetProperty("validFrom").GetString());
        var key = person.GetProperty("personId").GetString();
        var changed = await Call(client, "PATCH", $"/ledgers/{id}/people/{key}", new { revision = Revision(person), name = "お父さん" });
        var replay = await Call(client, "POST", $"/ledgers/{id}/people", input);
        Assert.Single(replay.GetProperty("people").EnumerateArray());
        Assert.Equal("お父さん", replay.GetProperty("people")[0].GetProperty("name").GetString());
        await Call(client, "POST", $"/ledgers/{id}/people", new { requestId, name = "別内容" }, HttpStatusCode.Conflict);
        await Call(client, "DELETE", $"/ledgers/{id}/people/{key}", new { revision = Revision(changed.GetProperty("people")[0]) });
        var deleted = await Call(client, "POST", $"/ledgers/{id}/people", input, HttpStatusCode.Conflict);
        Assert.Equal("created_result_deleted", Code(deleted));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_people"));
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM operation_requests"));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("long")]
    [InlineData("surrogate")]
    [InlineData("date")]
    [InlineData("period")]
    [InlineData("unknown")]
    public async Task InvalidPersonInputDoesNotWrite(string invalid)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var id = await LedgerAsync(client);
        var before = app.Database.Snapshot(app.Database.Tables());
        object input = invalid switch
        {
            "empty" => new { requestId = Guid.NewGuid(), name = " \t　 " },
            "long" => new { requestId = Guid.NewGuid(), name = new string('あ', 101) },
            "surrogate" => new { requestId = Guid.NewGuid(), name = string.Concat(Enumerable.Repeat("😀", 51)) },
            "date" => new { requestId = Guid.NewGuid(), name = "父", validFrom = "2026-02-30" },
            "period" => new { requestId = Guid.NewGuid(), name = "父", validFrom = "2026-10-01", validTo = "2026-09-30" },
            _ => new { requestId = Guid.NewGuid(), name = "父", authUserId = app.UserId }
        };
        var result = await Call(client, "POST", $"/ledgers/{id}/people", input, HttpStatusCode.BadRequest);
        Assert.Equal("invalid_input", Code(result));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task NameComparisonIsExactAndLengthUsesUtf16()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var client = await LoginAsync(app);
        var id = await LedgerAsync(client);
        foreach (var name in new[] { "A", "a", "Ａ", "é", "e\u0301", new string('あ', 100), string.Concat(Enumerable.Repeat("😀", 50)) })
            await PersonAsync(client, id, name);
        var duplicate = await Call(client, "POST", $"/ledgers/{id}/people", new { requestId = Guid.NewGuid(), name = " A " }, HttpStatusCode.Conflict);
        Assert.Equal("duplicate_person_name", Code(duplicate));
        Assert.Equal(7L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_people"));
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Editor")]
    public async Task MembersReadEveryoneButCanOnlyRenameTheirOwnPerson(string role)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var me = await PersonAsync(owner, id, "子");
        var other = await PersonAsync(owner, id, "父");
        var account = await app.AddAccountAsync();
        AddMember(app, id, account.UserId, role, me.GetProperty("personId").GetString());
        using var member = await LoginAsync(app, account);
        var people = await Call(member, "GET", $"/ledgers/{id}/people");
        Assert.Equal(2, people.GetProperty("people").GetArrayLength());
        Assert.Equal(me.GetProperty("personId").GetString(), people.GetProperty("myPersonId").GetString());
        var ownPath = $"/ledgers/{id}/people/{me.GetProperty("personId").GetString()}";
        var renamed = await Call(member, "PATCH", ownPath, new { revision = Revision(me), name = "本人の新しい名前" });
        var latest = renamed.GetProperty("people").EnumerateArray().Single(row => row.GetProperty("personId").GetString() == me.GetProperty("personId").GetString());
        await Call(member, "PATCH", ownPath, new { revision = Revision(latest), validFrom = (string?)null }, HttpStatusCode.Forbidden);
        await Call(member, "PATCH", $"/ledgers/{id}/people/{other.GetProperty("personId").GetString()}", new { revision = Revision(other), name = "他人" }, HttpStatusCode.Forbidden);
        await Call(member, "POST", $"/ledgers/{id}/people", new { requestId = Guid.NewGuid(), name = "追加" }, HttpStatusCode.Forbidden);
        await Call(member, "GET", $"/ledgers/{id}/members", expected: HttpStatusCode.Forbidden);
        await Call(member, "GET", $"/ledgers/{id}/invitations", expected: HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GlobalAdminDoesNotBypassLedgerMembership()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app, await app.AddAccountAsync());
        var id = await LedgerAsync(owner);
        using var outsider = await LoginAsync(app);
        foreach (var suffix in new[] { "people", "members", "invitations", $"members/{app.UserId}/edit" })
            await Call(outsider, "GET", $"/ledgers/{id}/{suffix}", expected: HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LinkedPersonCannotBeDeletedUntilExplicitlyUnlinked()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var person = await PersonAsync(owner, id, "父");
        var member = Member(await Call(owner, "GET", $"/ledgers/{id}/members"), app.UserId);
        var linked = await Call(owner, "PATCH", $"/ledgers/{id}/members/{app.UserId}", new { revision = Revision(member), personId = person.GetProperty("personId").GetString() });
        var before = app.Database.Snapshot(app.Database.Tables());
        var refused = await Call(owner, "DELETE", $"/ledgers/{id}/people/{person.GetProperty("personId").GetString()}", new { revision = Revision(person) }, HttpStatusCode.Conflict);
        Assert.Equal("person_referenced", Code(refused));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        await Call(owner, "PATCH", $"/ledgers/{id}/members/{app.UserId}", new { revision = Revision(Member(linked, app.UserId)), personId = (string?)null });
        var deleted = await Call(owner, "DELETE", $"/ledgers/{id}/people/{person.GetProperty("personId").GetString()}", new { revision = Revision(person) });
        Assert.Empty(deleted.GetProperty("people").EnumerateArray());
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_members"));
    }

    [Fact]
    public async Task OrderHasItsOwnRevisionAndRejectsStaleOrIncompleteLists()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var first = await PersonAsync(owner, id, "父");
        var initial = await Call(owner, "GET", $"/ledgers/{id}/people");
        var second = await PersonAsync(owner, id, "母");
        var stale = await Call(owner, "PUT", $"/ledgers/{id}/people/order", new { revision = initial.GetProperty("orderRevision").GetString(), personIds = new[] { first.GetProperty("personId").GetString() } }, HttpStatusCode.Conflict);
        Assert.Equal(2, stale.GetProperty("latest").GetProperty("people").GetArrayLength());
        var revision = stale.GetProperty("latest").GetProperty("orderRevision").GetString();
        await Call(owner, "PUT", $"/ledgers/{id}/people/order", new { revision, personIds = new[] { first.GetProperty("personId").GetString(), first.GetProperty("personId").GetString() } }, HttpStatusCode.BadRequest);
        var renamed = await Call(owner, "PATCH", $"/ledgers/{id}/people/{first.GetProperty("personId").GetString()}", new { revision = Revision(first), name = "父親" });
        Assert.Equal(revision, renamed.GetProperty("orderRevision").GetString());
        var sorted = await Call(owner, "PUT", $"/ledgers/{id}/people/order", new { revision, personIds = new[] { second.GetProperty("personId").GetString(), first.GetProperty("personId").GetString() } });
        Assert.Equal("母", sorted.GetProperty("people")[0].GetProperty("name").GetString());
        Assert.Equal(Revision(second), Revision(sorted.GetProperty("people")[0]));
        Assert.Equal(Revision(renamed.GetProperty("people")[0]), Revision(sorted.GetProperty("people")[1]));
    }

    [Fact]
    public async Task PersonConflictReturnsAllLatestFieldsAndNullClearsOnlyTheSpecifiedDate()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var initial = await Call(owner, "POST", $"/ledgers/{id}/people", new { requestId = Guid.NewGuid(), name = "父", validFrom = "2026-04-01", validTo = "2026-09-30" }, HttpStatusCode.Created);
        var person = initial.GetProperty("people")[0];
        var path = $"/ledgers/{id}/people/{person.GetProperty("personId").GetString()}";
        var updated = await Call(owner, "PATCH", path, new { revision = Revision(person), name = "父親" });
        var conflict = await Call(owner, "PATCH", path, new { revision = Revision(person), validFrom = (string?)null }, HttpStatusCode.Conflict);
        Assert.Equal("父親", conflict.GetProperty("latest").GetProperty("name").GetString());
        Assert.Equal("2026-04-01", conflict.GetProperty("latest").GetProperty("validFrom").GetString());
        Assert.Equal("2026-09-30", conflict.GetProperty("latest").GetProperty("validTo").GetString());
        var cleared = await Call(owner, "PATCH", path, new { revision = Revision(updated.GetProperty("people")[0]), validFrom = (string?)null });
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("people")[0].GetProperty("validFrom").ValueKind);
        Assert.Equal("2026-09-30", cleared.GetProperty("people")[0].GetProperty("validTo").GetString());
    }

    [Fact]
    public async Task MemberConflictAndDuplicateAssignmentDoNotPartiallyChangeRole()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var person = await PersonAsync(owner, id, "子");
        var first = await app.AddAccountAsync();
        var second = await app.AddAccountAsync();
        AddMember(app, id, first.UserId); AddMember(app, id, second.UserId);
        var members = await Call(owner, "GET", $"/ledgers/{id}/members");
        var firstMember = Member(members, first.UserId);
        await Call(owner, "PATCH", $"/ledgers/{id}/members/{first.UserId}", new { revision = Revision(firstMember), role = "Editor", personId = person.GetProperty("personId").GetString() });
        var conflict = await Call(owner, "PATCH", $"/ledgers/{id}/members/{first.UserId}", new { revision = Revision(firstMember), role = "Viewer" }, HttpStatusCode.Conflict);
        Assert.Equal("Editor", conflict.GetProperty("latest").GetProperty("member").GetProperty("role").GetString());
        var rejected = await Call(owner, "PATCH", $"/ledgers/{id}/members/{second.UserId}", new { revision = Revision(Member(members, second.UserId)), role = "Admin", personId = person.GetProperty("personId").GetString() }, HttpStatusCode.Conflict);
        Assert.Equal("person_already_assigned", Code(rejected));
        Assert.Equal("Viewer", app.Database.Scalar($"SELECT role FROM ledger_members WHERE auth_user_id='{second.UserId}'"));
        var edit = await Call(owner, "GET", $"/ledgers/{id}/members/{second.UserId}/edit");
        Assert.False(edit.GetProperty("personCandidates")[0].GetProperty("isSelectable").GetBoolean());
    }

    [Fact]
    public async Task SelfDemotionAndSelfExclusionAreRejected()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var self = Member(await Call(owner, "GET", $"/ledgers/{id}/members"), app.UserId);
        var before = app.Database.Snapshot(app.Database.Tables());
        await Call(owner, "PATCH", $"/ledgers/{id}/members/{app.UserId}", new { revision = Revision(self), role = "Viewer" }, HttpStatusCode.Forbidden);
        await Call(owner, "DELETE", $"/ledgers/{id}/members/{app.UserId}", new { revision = Revision(self) }, HttpStatusCode.Forbidden);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("session")]
    public async Task AuthorizationLossDuringSaveRollsBackAllBusinessChanges(string mutation)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var sql = mutation == "role" ? $"UPDATE ledger_members SET role='Viewer' WHERE auth_user_id='{app.UserId}'" : "UPDATE auth_sessions SET RevokedAtUtc='2020-01-01'";
        app.Database.Execute($"CREATE TRIGGER change_actor AFTER INSERT ON ledger_people BEGIN {sql}; END");
        var before = app.Database.Snapshot(app.Database.Tables());
        await Call(owner, "POST", $"/ledgers/{id}/people", new { requestId = Guid.NewGuid(), name = "父" }, mutation == "role" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    [Fact]
    public async Task InvitationReplayPreviewAcceptAndRejoinPreserveTheContract()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var requestId = Guid.NewGuid();
        var issued = await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId }, HttpStatusCode.Created);
        var invite = issued.GetProperty("invitation");
        var value = InviteToken(invite);
        var replay = await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId });
        Assert.Equal(invite.GetProperty("url").GetString(), replay.GetProperty("invitation").GetProperty("url").GetString());
        Assert.Equal(value, app.Database.Scalar("SELECT token FROM ledger_invitations"));
        var before = app.Database.Snapshot(app.Database.Tables());
        var ownPreview = await Call(owner, "POST", "/ledger-invitations/preview", new { token = value });
        Assert.Equal("already_member", ownPreview.GetProperty("status").GetString());
        await Call(owner, "POST", "/ledger-invitations/accept", new { token = value });
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        var account = await app.AddAccountAsync();
        using var guest = await LoginAsync(app, account);
        var preview = await Call(guest, "POST", "/ledger-invitations/preview", new { token = value });
        Assert.Equal("valid", preview.GetProperty("status").GetString());
        var joined = await Call(guest, "POST", "/ledger-invitations/accept", new { token = value });
        Assert.Equal("Viewer", joined.GetProperty("ledger").GetProperty("role").GetString());
        Assert.Equal(id, app.Database.Scalar($"SELECT last_opened_ledger_id FROM ledger_navigation WHERE auth_user_id='{account.UserId}'"));
        var used = await Call(guest, "POST", "/ledger-invitations/accept", new { token = value }, HttpStatusCode.Conflict);
        Assert.Equal("invitation_used", Code(used));
        var member = Member(await Call(owner, "GET", $"/ledgers/{id}/members"), account.UserId);
        await Call(owner, "DELETE", $"/ledgers/{id}/members/{account.UserId}", new { revision = Revision(member) });
        await Call(guest, "GET", $"/ledgers/{id}/people", expected: HttpStatusCode.NotFound);
        await Call(guest, "POST", "/ledger-invitations/accept", new { token = value }, HttpStatusCode.Conflict);
        var next = await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created);
        await Call(guest, "POST", "/ledger-invitations/accept", new { token = InviteToken(next.GetProperty("invitation")) });
        Assert.Equal("Viewer", app.Database.Scalar($"SELECT role FROM ledger_members WHERE auth_user_id='{account.UserId}'"));
        Assert.Null(app.Database.Scalar($"SELECT person_id FROM ledger_members WHERE auth_user_id='{account.UserId}'") as string);
    }

    [Theory]
    [InlineData("ledger_members")]
    [InlineData("ledger_invitations")]
    [InlineData("ledger_navigation")]
    public async Task JoinFailureRollsBackMembershipInvitationAndNavigation(string table)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var issued = await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created);
        using var guest = await LoginAsync(app, await app.AddAccountAsync());
        var operation = table == "ledger_invitations" ? "UPDATE" : "INSERT";
        app.Database.Execute($"CREATE TRIGGER fail_join BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT,'synthetic-private-error'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        var failure = await Call(guest, "POST", "/ledger-invitations/accept", new { token = InviteToken(issued.GetProperty("invitation")) }, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("store_unavailable", Code(failure));
        Assert.DoesNotContain("synthetic", failure.ToString());
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        app.Database.Execute("DROP TRIGGER fail_join");
        await Call(guest, "POST", "/ledger-invitations/accept", new { token = InviteToken(issued.GetProperty("invitation")) });
    }

    [Fact]
    public async Task ExpiredAndRevokedInvitationsNeverPermitJoin()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var first = (await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created)).GetProperty("invitation");
        var second = (await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created)).GetProperty("invitation");
        using var guest = await LoginAsync(app, await app.AddAccountAsync());
        var remaining = await Call(owner, "DELETE", $"/ledgers/{id}/invitations/{first.GetProperty("invitationId").GetString()}");
        Assert.Single(remaining.GetProperty("invitations").EnumerateArray());
        var revoked = await Call(guest, "POST", "/ledger-invitations/accept", new { token = InviteToken(first) }, HttpStatusCode.Conflict);
        Assert.Equal("invitation_revoked", Code(revoked));
        app.Time.Advance(TimeSpan.FromDays(7));
        var expired = await Call(guest, "POST", "/ledger-invitations/accept", new { token = InviteToken(second) }, HttpStatusCode.Conflict);
        Assert.Equal("invitation_expired", Code(expired));
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_members"));
        Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_invitations"));
    }

    [Fact]
    public async Task SimultaneousJoinAdmitsOnlyOneAccount()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var invitation = (await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created)).GetProperty("invitation");
        using var first = await LoginAsync(app, await app.AddAccountAsync());
        using var second = await LoginAsync(app, await app.AddAccountAsync());
        var body = new { token = InviteToken(invitation) };
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/ledger-invitations/accept", body), second.PostAsJsonAsync("/ledger-invitations/accept", body));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(2L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_members"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task SimultaneousAssignmentCannotStealTheSamePerson()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var person = await PersonAsync(owner, id, "子");
        var first = await app.AddAccountAsync(); var second = await app.AddAccountAsync();
        AddMember(app, id, first.UserId); AddMember(app, id, second.UserId);
        var members = await Call(owner, "GET", $"/ledgers/{id}/members");
        var responses = await Task.WhenAll(new[] { first.UserId, second.UserId }.Select(accountId => owner.PatchAsJsonAsync(
            $"/ledgers/{id}/members/{accountId}", new { revision = Revision(Member(members, accountId)), personId = person.GetProperty("personId").GetString() })));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_members WHERE person_id IS NOT NULL"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task AdminsExcludingEachOtherLeaveOneCurrentAdmin()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var account = await app.AddAccountAsync();
        AddMember(app, id, account.UserId, "Admin");
        using var other = await LoginAsync(app, account);
        var members = await Call(owner, "GET", $"/ledgers/{id}/members");
        async Task<HttpResponseMessage> Exclude(HttpClient client, string accountId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"/ledgers/{id}/members/{accountId}")
            { Content = JsonContent.Create(new { revision = Revision(Member(members, accountId)) }) };
            return await client.SendAsync(request);
        }
        var responses = await Task.WhenAll(Exclude(owner, account.UserId), Exclude(other, app.UserId));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NotFound);
            Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_members WHERE role='Admin'"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task PeopleAndInvitationsShareTheExistingAccountWriteLimit()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var person = await PersonAsync(owner, id, "父");
        await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created);
        var self = Member(await Call(owner, "GET", $"/ledgers/{id}/members"), app.UserId);
        await Call(owner, "PATCH", $"/ledgers/{id}/members/{app.UserId}", new { revision = Revision(self), personId = (string?)null });
        for (var i = 0; i < 16; i++)
            await Call(owner, "PATCH", $"/ledgers/{id}/people/{person.GetProperty("personId").GetString()}", new { revision = Guid.NewGuid(), name = "変更" }, HttpStatusCode.Conflict);
        using var blocked = await owner.PostAsJsonAsync($"/ledgers/{id}/people", new { requestId = Guid.NewGuid(), name = "追加" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        using var sameAccount = await LoginAsync(app);
        using var alsoBlocked = await sameAccount.PostAsJsonAsync($"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.TooManyRequests, alsoBlocked.StatusCode);
        await Call(owner, "GET", $"/ledgers/{id}/people");
        using var another = await LoginAsync(app, await app.AddAccountAsync());
        await LedgerAsync(another);
    }

    [Fact]
    public async Task PreviewUsesTheSharedReadLimitDespiteBeingPost()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var invite = (await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId = Guid.NewGuid() }, HttpStatusCode.Created)).GetProperty("invitation");
        for (var i = 0; i < 120; i++)
        {
            if (i % 4 == 0) await Call(owner, "POST", "/ledger-invitations/preview", new { token = InviteToken(invite) });
            else await Call(owner, "GET", $"/ledgers/{id}/{(i % 4 == 1 ? "people" : i % 4 == 2 ? "members" : "invitations")}");
        }
        using var blocked = await owner.PostAsJsonAsync("/ledger-invitations/preview", new { token = InviteToken(invite) });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        await PersonAsync(owner, id, "書込枠は独立");
    }

    [Theory]
    [InlineData("PATCH", "csrf", HttpStatusCode.BadRequest)]
    [InlineData("PATCH", "origin", HttpStatusCode.Forbidden)]
    [InlineData("PATCH", "large", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("PUT", "origin", HttpStatusCode.Forbidden)]
    [InlineData("PUT", "large", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("DELETE", "csrf", HttpStatusCode.BadRequest)]
    [InlineData("PREVIEW", "origin", HttpStatusCode.Forbidden)]
    [InlineData("PREVIEW", "http", HttpStatusCode.BadRequest)]
    public async Task NewEndpointsKeepTheHttpBoundary(string method, string invalid, HttpStatusCode expected)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var person = await PersonAsync(owner, id, "父");
        var path = method == "PREVIEW" ? "/ledger-invitations/preview" : method == "PUT" ? $"/ledgers/{id}/people/order" : $"/ledgers/{id}/people/{person.GetProperty("personId").GetString()}";
        var before = app.Database.Snapshot(app.Database.Tables());
        if (invalid == "csrf") owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (invalid == "origin") { owner.DefaultRequestHeaders.Remove("Origin"); owner.DefaultRequestHeaders.Add("Origin", "https://other.example.test"); }
        if (invalid == "http") path = "http://monetail.example.test" + path;
        using var request = new HttpRequestMessage(new HttpMethod(method == "PREVIEW" ? "POST" : method), path)
        { Content = new StringContent(invalid == "large" ? new string('x', 70000) : "{}", Encoding.UTF8, "application/json") };
        using var response = await owner.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task SqliteEnforcesSameLedgerAndOneToOnePersonReferences()
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var first = await LedgerAsync(owner); var second = await LedgerAsync(owner);
        var person = await PersonAsync(owner, first, "父");
        var key = person.GetProperty("personId").GetString();
        var account = await app.AddAccountAsync();
        AddMember(app, first, account.UserId);
        using var connection = app.Database.OpenForeignKeyConnection();
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection,
            $"UPDATE ledger_members SET person_id='{key}' WHERE ledger_id='{second}'")).SqliteErrorCode);
        AuthDatabaseFixture.Execute(connection, $"UPDATE ledger_members SET person_id='{key}' WHERE ledger_id='{first}' AND auth_user_id='{account.UserId}'");
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection,
            $"UPDATE ledger_members SET person_id='{key}' WHERE ledger_id='{first}' AND auth_user_id='{app.UserId}'")).SqliteErrorCode);
        Assert.Equal(19, Assert.Throws<SqliteException>(() => AuthDatabaseFixture.Execute(connection,
            $"UPDATE ledger_people SET valid_from='2026-10-01',valid_to='2026-09-30' WHERE ledger_id='{first}'")).SqliteErrorCode);
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("operation")]
    [InlineData("actor")]
    public async Task SharedRequestIdCannotChangeLedgerOperationOrActor(string mismatch)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var requestId = Guid.NewGuid();
        await Call(owner, "POST", $"/ledgers/{id}/people", new { requestId, name = "父" }, HttpStatusCode.Created);
        if (mismatch == "ledger")
        {
            var another = await LedgerAsync(owner);
            var failure = await Call(owner, "POST", $"/ledgers/{another}/people", new { requestId, name = "父" }, HttpStatusCode.Conflict);
            Assert.Equal("request_conflict", Code(failure));
        }
        else if (mismatch == "operation")
            Assert.Equal("request_conflict", Code(await Call(owner, "POST", $"/ledgers/{id}/invitations", new { requestId }, HttpStatusCode.Conflict)));
        else
        {
            var account = await app.AddAccountAsync(); AddMember(app, id, account.UserId, "Admin");
            using var other = await LoginAsync(app, account);
            Assert.Equal("request_conflict", Code(await Call(other, "POST", $"/ledgers/{id}/people", new { requestId, name = "父" }, HttpStatusCode.Conflict)));
        }
        Assert.Equal(1L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_people"));
        Assert.Equal(0L, app.Database.Scalar("SELECT COUNT(*) FROM ledger_invitations"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersonDeletionDistinguishesIncomingReferencesFromStoreFailure(bool referenced)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var owner = await LoginAsync(app);
        var id = await LedgerAsync(owner);
        var person = await PersonAsync(owner, id, "父");
        if (referenced)
            app.Database.Execute($"CREATE TABLE synthetic_reference(ledger_id TEXT,person_id TEXT,FOREIGN KEY(ledger_id,person_id) REFERENCES ledger_people(ledger_id,person_id) ON DELETE RESTRICT); INSERT INTO synthetic_reference VALUES('{id}','{person.GetProperty("personId").GetString()}')");
        else
            app.Database.Execute("CREATE TRIGGER fail_person_delete BEFORE DELETE ON ledger_people BEGIN SELECT RAISE(ABORT,'synthetic-private-error'); END");
        var before = app.Database.Snapshot(app.Database.Tables());
        var failure = await Call(owner, "DELETE", $"/ledgers/{id}/people/{person.GetProperty("personId").GetString()}",
            new { revision = Revision(person) }, referenced ? HttpStatusCode.Conflict : HttpStatusCode.ServiceUnavailable);
        Assert.Equal(referenced ? "person_referenced" : "store_unavailable", Code(failure));
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    private static string Code(JsonElement value) => value.GetProperty("code").GetString()!;
    private static string Revision(JsonElement value) => value.GetProperty("revision").GetString()!;
    private static JsonElement Member(JsonElement value, string id) => value.GetProperty("members").EnumerateArray().Single(row => row.GetProperty("accountId").GetString() == id);
    private static string InviteToken(JsonElement value) => new Uri(value.GetProperty("url").GetString()!).Fragment["#token=".Length..];
    private static void AddMember(LoginTestApp app, string id, string accountId, string role = "Viewer", string? personId = null) =>
        app.Database.Execute($"INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision,person_id) VALUES('{id}','{accountId}','{role}','2026-10-01','{Guid.NewGuid():N}',{(personId is null ? "NULL" : "'" + personId + "'")})");
    private static async Task<string> LedgerAsync(HttpClient client) =>
        (await Call(client, "POST", "/ledgers", new { requestId = Guid.NewGuid(), name = "家計簿" }, HttpStatusCode.Created)).GetProperty("ledgerId").GetString()!;
    private static async Task<JsonElement> PersonAsync(HttpClient client, string id, string name) =>
        (await Call(client, "POST", $"/ledgers/{id}/people", new { requestId = Guid.NewGuid(), name }, HttpStatusCode.Created)).GetProperty("people").EnumerateArray().Single(row => row.GetProperty("name").GetString() == name);
    private static async Task<HttpClient> LoginAsync(LoginTestApp app, (string UserId, byte[] CredentialId)? account = null)
    {
        var client = app.Client();
        var options = await app.BeginAsync(client);
        using var login = await app.VerifyAsync(client, account is { } user ? app.AssertionFor(options, user.UserId, user.CredentialId) : app.Assertion(options));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await LoginTestApp.CsrfAsync(client);
        return client;
    }
    private static async Task<JsonElement> Call(HttpClient client, string method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{method} {path}: expected {expected}, got {response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }
}
