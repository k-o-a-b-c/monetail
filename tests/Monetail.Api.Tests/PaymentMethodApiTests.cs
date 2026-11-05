using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Monetail.Api.Tests;
public sealed class PaymentMethodApiTests
{
    [Fact]
    public async Task CrudReplayAndIndependentVersions()
    {
        await using var f = await Fixture.Create();
        var original = await f.Call("GET"); var version = f.OwnerVersion(); var otherVersion = f.OwnerVersion(f.Other);
        var personRevision = f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{f.Owner}'");
        var request = Guid.NewGuid();
        var created = await f.Call("POST",new { requestId=request, ownerPersonId=f.Owner, name="  現金  ", validFrom="2000-01-01", validTo="2001-01-01" },201);
        Assert.Equal(2, original.GetProperty("ownerOrderRevisions").GetArrayLength());
        var row = created.GetProperty("paymentMethods")[0]; Assert.Equal("現金",row.GetProperty("name").GetString());
        Assert.NotEqual(version,f.OwnerVersion()); Assert.Equal(otherVersion,f.OwnerVersion(f.Other));
        version=f.OwnerVersion();
        var replay=await f.Call("POST",new {requestId=request,ownerPersonId=f.Owner,name="現金",validFrom="2000-01-01",validTo="2001-01-01"});
        Assert.Equal(created.GetProperty("paymentMethodId").GetString(),replay.GetProperty("paymentMethodId").GetString()); Assert.Equal(version,f.OwnerVersion());
        var path="/"+row.GetProperty("paymentMethodId").GetString(); var revision=row.GetProperty("revision").GetString();
        var unchanged=await f.Call("PATCH",new {revision,name=" 現金 "},suffix:path);
        Assert.Equal(revision,unchanged.GetProperty("paymentMethods")[0].GetProperty("revision").GetString());
        var updated=await f.Call("PATCH",new {revision,validTo=(string?)null},suffix:path);
        var changed=updated.GetProperty("paymentMethods")[0]; Assert.NotEqual(revision,changed.GetProperty("revision").GetString());
        Assert.Equal("2000-01-01",changed.GetProperty("validFrom").GetString()); Assert.Equal(JsonValueKind.Null,changed.GetProperty("validTo").ValueKind);
        Assert.Equal(version,f.OwnerVersion()); Assert.Equal(personRevision,f.App.Database.Scalar($"SELECT revision FROM ledger_people WHERE person_id='{f.Owner}'"));
        await f.Call("PATCH",new {revision,name="古い版"},409,path);
        await f.Call("DELETE",new {revision},409,path);
        await f.Call("DELETE",new {revision=changed.GetProperty("revision").GetString()},suffix:path);
        Assert.NotEqual(version,f.OwnerVersion()); Assert.Equal(otherVersion,f.OwnerVersion(f.Other));
        var removed=await f.Call("POST",new {requestId=request,ownerPersonId=f.Owner,name="現金",validFrom="2000-01-01",validTo="2001-01-01"},409);
        Assert.Equal("created_result_deleted",removed.GetProperty("code").GetString());
    }
    [Theory]
    [InlineData("Viewer",false,403)] [InlineData("Editor",false,403)] [InlineData("Editor",true,201)] [InlineData("Admin",false,201)]
    public async Task WriterPermissions(string role,bool own,int expected)
    {
        await using var f=await Fixture.Create(); f.Role(role,own);
        await f.Call("POST",new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="カード"},expected);
    }
    [Theory]
    [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task EditorCannotChangeOtherOwners(string method)
    {
        await using var f=await Fixture.Create(); var row=await f.Payment(); f.Role("Editor",false); var before=f.Snapshot();
        await f.Call(method,method=="PATCH"?new {revision=Revision(row),name="変更"}:(object)new {revision=Revision(row)},403,Path(row));
        Assert.Equal(before,f.Snapshot());
    }
    [Theory]
    [InlineData("empty")] [InlineData("long")] [InlineData("emoji")] [InlineData("date")] [InlineData("period")] [InlineData("missingOwner")] [InlineData("unknown")]
    public async Task InvalidCreateDoesNotWrite(string kind)
    {
        await using var f=await Fixture.Create(); var before=f.Snapshot();
        object input=kind switch {
            "empty"=>new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="　 "},
            "long"=>new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name=new string('あ',101)},
            "emoji"=>new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name=string.Concat(Enumerable.Repeat("😀",51))},
            "date"=>new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="カード",validFrom="2026-02-30"},
            "period"=>new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="カード",validFrom="2026-10-01",validTo="2026-09-30"},
            "missingOwner"=>new {requestId=Guid.NewGuid(),name="カード"},
            _=>new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="カード",displayOrder=1}};
        await f.Call("POST",input,400);Assert.Equal(before,f.Snapshot());
    }
    [Fact]
    public async Task NamesAreExactWithinOwnerAndForeignOwnerIsRejected()
    {
        await using var f=await Fixture.Create();
        foreach(var name in new[]{"A","a",new string('あ',100),string.Concat(Enumerable.Repeat("😀",50))}) await f.Payment(name);
        var duplicate=await f.Call("POST",new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name=" A "},409);
        Assert.Equal("duplicate_payment_method_name",duplicate.GetProperty("code").GetString());
        await f.Call("POST",new {requestId=Guid.NewGuid(),ownerPersonId=f.Other,name="A"},201);
        var otherLedger=(await Send(f.Client,"POST","/ledgers",new {requestId=Guid.NewGuid(),name="別"},201)).GetProperty("ledgerId").GetString();
        var outsider=(await Send(f.Client,"POST",$"/ledgers/{otherLedger}/people",new {requestId=Guid.NewGuid(),name="他人"},201)).GetProperty("people")[0].GetProperty("personId").GetString();
        await f.Call("POST",new {requestId=Guid.NewGuid(),ownerPersonId=outsider,name="不可"},400);
    }
    [Theory]
    [InlineData("ownerPersonId")] [InlineData("displayOrder")] [InlineData("name")] [InlineData("none")] [InlineData("date")]
    public async Task PatchRejectsInvalidFields(string kind)
    {
        await using var f=await Fixture.Create();var row=await f.Payment();var before=f.Snapshot();
        object input=kind switch {"ownerPersonId"=>new {revision=Revision(row),ownerPersonId=f.Other},"displayOrder"=>new {revision=Revision(row),displayOrder=3},
        "name"=>new {revision=Revision(row),name=(string?)null},"none"=>new {revision=Revision(row)},_=>new {revision=Revision(row),validTo="2026/01/01"}};
        await f.Call("PATCH",input,400,Path(row));Assert.Equal(before,f.Snapshot());
    }
    [Theory]
    [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task CompetingChangesOnlyOneOldRevisionSucceeds(string method)
    {
        await using var f=await Fixture.Create();var row=await f.Payment();var route=f.Root+Path(row);
        var requests=Enumerable.Range(0,2).Select(i=>f.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method),route){Content=JsonContent.Create(method=="PATCH"?new {revision=Revision(row),name="変更"+i}:(object)new {revision=Revision(row)})})).ToArray();
        var results=await Task.WhenAll(requests);
        Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK); Assert.Single(results,r=>r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound);
        foreach(var r in results)r.Dispose();
    }
    [Fact]
    public async Task ConcurrentReplayCreatesOneAndDifferentPayloadConflicts()
    {
        await using var f=await Fixture.Create();var input=new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="カード"};
        var results=await Task.WhenAll(f.Client.PostAsJsonAsync(f.Root,input),f.Client.PostAsJsonAsync(f.Root,input));
        Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);
        foreach(var r in results)r.Dispose();
        Assert.Equal(1L,f.App.Database.Scalar("SELECT COUNT(*) FROM ledger_payment_methods"));
        await f.Call("POST",new {input.requestId,input.ownerPersonId,name="別"},409);
    }
    [Theory]
    [InlineData("insert")] [InlineData("record")] [InlineData("permission")] [InlineData("cas")]
    public async Task CreationFailureRollsBackOrderPaymentAndReplayRecord(string failure)
    {
        await using var f=await Fixture.Create();
        var sql=failure switch {
        "insert"=>"CREATE TRIGGER fail BEFORE INSERT ON ledger_payment_methods BEGIN SELECT RAISE(ABORT,'synthetic'); END",
        "record"=>"CREATE TRIGGER fail BEFORE INSERT ON operation_requests BEGIN SELECT RAISE(ABORT,'synthetic'); END",
        "permission"=>"CREATE TRIGGER fail AFTER INSERT ON ledger_payment_methods BEGIN UPDATE ledger_members SET role='Viewer'; END",
        _=>"CREATE TRIGGER fail BEFORE UPDATE OF payment_order_revision ON ledger_people BEGIN SELECT RAISE(IGNORE); END"};
        f.App.Database.Execute(sql);var before=f.Snapshot();
        await f.Call("POST",new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="カード"},failure=="permission"?403:failure=="cas"?409:503);
        Assert.Equal(before,f.Snapshot());
    }
    [Theory]
    [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task ConditionalRowWriteFailureDoesNotApplyChanges(string method)
    {
        await using var f=await Fixture.Create();var row=await f.Payment();
        f.App.Database.Execute($"CREATE TRIGGER fail BEFORE {(method=="PATCH"?"UPDATE":"DELETE")} ON ledger_payment_methods BEGIN SELECT RAISE(IGNORE); END");var before=f.Snapshot();
        await f.Call(method,method=="PATCH"?new {revision=Revision(row),name="変更"}:(object)new {revision=Revision(row)},409,Path(row));Assert.Equal(before,f.Snapshot());
    }
    [Fact]
    public async Task RestrictReferenceBlocksDeleteAndOrderVersionRemainsUnchanged()
    {
        await using var f=await Fixture.Create();var row=await f.Payment();var id=row.GetProperty("paymentMethodId").GetString();
        // テスト専用の参照表で将来の業務FKを再現。本番モデルには業務表を追加しない。
        f.App.Database.Execute($"CREATE TABLE test_payment_reference(ledger_id TEXT NOT NULL,payment_method_id TEXT NOT NULL,FOREIGN KEY(ledger_id,payment_method_id) REFERENCES ledger_payment_methods(ledger_id,payment_method_id) ON DELETE RESTRICT); INSERT INTO test_payment_reference VALUES('{f.Ledger}','{id}')");
        var before=f.Snapshot();var result=await f.Call("DELETE",new {revision=Revision(row)},409,Path(row));
        Assert.Equal("payment_method_referenced",result.GetProperty("code").GetString());Assert.Equal(before,f.Snapshot());
    }
    [Theory]
    [InlineData("PATCH","owner")] [InlineData("DELETE","owner")]
    [InlineData("PATCH","role")] [InlineData("DELETE","role")]
    [InlineData("PATCH","session")] [InlineData("DELETE","session")]
    public async Task FinalAuthorizationRecheckRollsBackChanges(string method,string change)
    {
        await using var f=await Fixture.Create();var row=await f.Payment();f.Role("Editor",true);
        var mutation=change switch {"owner"=>$"UPDATE ledger_members SET person_id='{f.Other}'", "role"=>"UPDATE ledger_members SET role='Viewer'",_=>"UPDATE auth_sessions SET RevokedAtUtc=CreatedAtUtc"};
        f.App.Database.Execute($"CREATE TRIGGER change_actor AFTER {(method=="PATCH"?"UPDATE":"DELETE")} ON ledger_payment_methods BEGIN {mutation}; END");var before=f.Snapshot();
        await f.Call(method,method=="PATCH"?new {revision=Revision(row),name="未保存"}:(object)new {revision=Revision(row)},change=="session"?401:403,Path(row));
        Assert.Equal(before,f.Snapshot());
    }
    [Fact]
    public async Task DeleteOrderCasFailureRollsBackDeletedRow()
    {
        await using var f=await Fixture.Create();var row=await f.Payment();
        f.App.Database.Execute("CREATE TRIGGER stop_order BEFORE UPDATE OF payment_order_revision ON ledger_people BEGIN SELECT RAISE(IGNORE); END");var before=f.Snapshot();
        var conflict=await f.Call("DELETE",new {revision=Revision(row)},409,Path(row));Assert.Equal(before,f.Snapshot());
        Assert.Single(conflict.GetProperty("latest").GetProperty("paymentMethods").EnumerateArray());
    }
    [Fact]
    public async Task DuplicatePatchDoesNotChangeRowOrOrderAndAppendKeepsGaps()
    {
        await using var f=await Fixture.Create();var first=await f.Payment();var second=await f.Payment("カード");var before=f.Snapshot();
        var duplicate=await f.Call("PATCH",new {revision=Revision(second),name=" 現金 "},409,Path(second));
        Assert.Equal("duplicate_payment_method_name",duplicate.GetProperty("code").GetString());Assert.Equal(before,f.Snapshot());
        await f.Call("DELETE",new {revision=Revision(first)},suffix:Path(first));var next=await f.Payment("新規");Assert.Equal(2,next.GetProperty("displayOrder").GetInt32());
    }
    [Theory]
    [InlineData("Viewer")] [InlineData("Editor")]
    public async Task ReadsIncludeAllOwnersEvenWithoutOwnPerson(string role)
    {
        await using var f=await Fixture.Create();await f.Payment();f.Role(role,false);
        var result=await f.Call("GET");Assert.Equal(role,result.GetProperty("currentRole").GetString());Assert.Equal(JsonValueKind.Null,result.GetProperty("myPersonId").ValueKind);
        Assert.Equal(2,result.GetProperty("ownerOrderRevisions").GetArrayLength());Assert.Single(result.GetProperty("paymentMethods").EnumerateArray());
    }
    [Theory]
    [InlineData("anonymous",401)] [InlineData("csrf",400)] [InlineData("origin",403)] [InlineData("notMember",404)]
    public async Task AuthenticationAndBoundaryRejectWithoutWrites(string kind,int status)
    {
        await using var f=await Fixture.Create();
        using var anonymous=f.App.Client();var client=kind=="anonymous"?anonymous:f.Client;
        if(kind=="csrf")client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if(kind=="origin")client.DefaultRequestHeaders.Remove("Origin");
        if(kind=="notMember")f.App.Database.Execute("DELETE FROM ledger_members");var before=f.Snapshot();
        using var response=await client.PostAsJsonAsync(f.Root,new {requestId=Guid.NewGuid(),ownerPersonId=f.Owner,name="不可"});
        Assert.Equal((HttpStatusCode)status,response.StatusCode);Assert.Equal(before,f.Snapshot());
    }
    private static string Revision(JsonElement row)=>row.GetProperty("revision").GetString()!;
    private static string Path(JsonElement row)=>"/"+row.GetProperty("paymentMethodId").GetString();
    private static async Task<JsonElement> Send(HttpClient client,string method,string path,object? input=null,int status=200)
    {
        using var request=new HttpRequestMessage(new HttpMethod(method),path);if(input is not null)request.Content=JsonContent.Create(input);
        using var response=await client.SendAsync(request);Assert.Equal((HttpStatusCode)status,response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public LoginTestApp App=null!;public HttpClient Client=null!;public string Ledger="",Owner="",Other="";
        public string Root=>$"/ledgers/{Ledger}/payment-methods";
        public static async Task<Fixture> Create()
        {
            var f=new Fixture{App=await LoginTestApp.CreateAsync()};f.Client=f.App.Client();await f.App.LoginAsync(f.Client);await LoginTestApp.CsrfAsync(f.Client);
            f.Ledger=(await Send(f.Client,"POST","/ledgers",new {requestId=Guid.NewGuid(),name="家計簿"},201)).GetProperty("ledgerId").GetString()!;
            f.Owner=(await Send(f.Client,"POST",$"/ledgers/{f.Ledger}/people",new {requestId=Guid.NewGuid(),name="父"},201)).GetProperty("people")[0].GetProperty("personId").GetString()!;
            f.Other=(await Send(f.Client,"POST",$"/ledgers/{f.Ledger}/people",new {requestId=Guid.NewGuid(),name="母"},201)).GetProperty("people")[1].GetProperty("personId").GetString()!;
            return f;
        }
        public Task<JsonElement> Call(string method,object? input=null,int status=200,string suffix="")=>Send(Client,method,Root+suffix,input,status);
        public async Task<JsonElement> Payment(string name="現金")
        {
            var result=await Call("POST",new {requestId=Guid.NewGuid(),ownerPersonId=Owner,name},201);
            return result.GetProperty("paymentMethods").EnumerateArray().Single(row=>row.GetProperty("paymentMethodId").GetString()==result.GetProperty("paymentMethodId").GetString());
        }
        public string OwnerVersion(string? owner=null)=>(string)App.Database.Scalar($"SELECT payment_order_revision FROM ledger_people WHERE person_id='{owner??Owner}'")!;
        public string Snapshot()=>App.Database.Snapshot(App.Database.Tables());
        public void Role(string role,bool own)=>App.Database.Execute($"UPDATE ledger_members SET role='{role}',person_id={(own?$"'{Owner}'":"NULL")} WHERE ledger_id='{Ledger}' AND auth_user_id='{App.UserId}'");
        public async ValueTask DisposeAsync(){Client.Dispose();await App.DisposeAsync();}
    }
}
