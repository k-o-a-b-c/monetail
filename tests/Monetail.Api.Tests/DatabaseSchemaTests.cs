using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

public sealed class DatabaseSchemaTests
{
    [Fact]
    public async Task EveryModelTableKeyIndexForeignKeyAndCheckIsCreatedAlongWithRequiredTriggersAndSeeds()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var tables = context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables.ToArray();
        Assert.Equal(tables.Select(t => t.Name).Order(),db.Tables().Order());
        Assert.DoesNotContain(db.Tables(),t => t.StartsWith("__",StringComparison.Ordinal));
        foreach (var table in tables)
        {
            var name=table.Name.Replace("'","''");
            var ddl=(string)db.Scalar($"SELECT sql FROM sqlite_schema WHERE name='{name}' AND type='table'")!;
            foreach(var check in table.CheckConstraints) { Assert.Contains(Assert.IsType<string>(check.Name),ddl); Assert.Contains(Assert.IsType<string>(check.Sql),ddl); }
            foreach(var index in table.Indexes)
                Assert.Equal(index.IsUnique ? 1L : 0L,db.Scalar($"SELECT \"unique\" FROM pragma_index_list('{name}') WHERE name='{index.Name}'"));
            Assert.Equal((long)table.ForeignKeyConstraints.Sum(fk=>fk.Columns.Count),db.Scalar($"SELECT COUNT(*) FROM pragma_foreign_key_list('{name}')"));
            Assert.Equal((long)table.PrimaryKey!.Columns.Count,db.Scalar($"SELECT COUNT(*) FROM pragma_table_info('{name}') WHERE pk>0"));
        }
        foreach(var trigger in new[]{"trg_ledger_payment_methods_owner_immutable","trg_tax_rate_history_overlap_insert","trg_tax_rate_history_overlap_update"})
            Assert.Equal(1L,db.Scalar($"SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='{trigger}'"));
        Assert.Equal(3L,db.Scalar("SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger'"));
        Assert.Equal("標準",db.Scalar("SELECT name FROM tax_categories WHERE code='Standard'"));
        Assert.Equal("食品軽減",db.Scalar("SELECT name FROM tax_categories WHERE code='ReducedFood'"));
        Assert.Equal("新聞軽減",db.Scalar("SELECT name FROM tax_categories WHERE code='ReducedNewspaper'"));
        Assert.Equal("非課税",db.Scalar("SELECT name FROM tax_categories WHERE code='Exempt'"));
        Assert.Equal(0L,db.Scalar("SELECT COUNT(*) FROM tax_rate_history"));
        Assert.Equal(0L,db.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(1L,db.Scalar("SELECT COUNT(*) FROM OpenIddictApplications WHERE ClientId='monetail-windows' AND ClientType='public'"));
        Assert.Contains("ft:pkce",(string)db.Scalar("SELECT Requirements FROM OpenIddictApplications")!);
    }

    [Fact]
    public async Task ReinitializationRefusesForeignKeyDamageWithoutDeletingOrRepairingRows()
    {
        using var db=new AuthDatabaseFixture(); await db.InitializeAsync();
        db.Execute("INSERT INTO auth_account_admin_notes(AuthUserId,AdminNote) VALUES('missing','維持する値')");
        var before=db.Snapshot(db.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.InitializeAsync());
        Assert.Equal(before,db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task PreviousCategorySchemaIsRefusedWithoutTransformingItsValues()
    {
        await using var app=await LoginTestApp.CreateAsync();
        using var client=app.Client(); await LoginTestApp.CsrfAsync(client);
        app.Database.Execute("""
            DROP TABLE ledger_categories;
            CREATE TABLE ledger_categories(ledger_id TEXT,category_id TEXT,name TEXT,is_enabled INTEGER,display_order INTEGER,tax_category_code TEXT,income_expense TEXT,revision TEXT);
            INSERT INTO ledger_categories VALUES('old-ledger','old-category','保持する旧科目',0,7,'Standard','Expense','old-revision');
            CREATE TABLE __MonetailMigrationsHistory(MigrationId TEXT PRIMARY KEY,ProductVersion TEXT);
            INSERT INTO __MonetailMigrationsHistory VALUES('20261106204316_AddLedgerCategoriesAndTaxHistory','10.0.13');
            """);
        var before=app.Database.Snapshot(app.Database.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>app.Database.InitializeAsync());
        Assert.Equal(before,app.Database.Snapshot(app.Database.Tables()));
        using var response=await client.PostAsync("/auth/login/options", new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.Equal(before,app.Database.Snapshot(app.Database.Tables()));
    }
}
