using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Receipts;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>本番のDI・初期化経路とFK有効な隔離SQLiteで、レシート4表を検証する。</summary>
public sealed class ReceiptSchemaTests
{
    private static readonly string[] ReceiptTables = ["ledger_receipts", "ledger_receipt_groups", "ledger_receipt_details", "ledger_receipt_detail_people"];

    [Fact]
    public async Task FreshFileCreatesExactlyTheFourReceiptTablesWithoutMigrationsOrExtraBusinessColumns()
    {
        using var db = new AuthDatabaseFixture(createFile: false);
        Assert.False(File.Exists(db.DatabasePath));
        await db.InitializeAsync();
        foreach (var table in ReceiptTables) Assert.Equal(0L, db.Scalar($"SELECT COUNT(*) FROM {table}"));
        Assert.DoesNotContain(db.Tables(), name => name.Contains("Migration", StringComparison.Ordinal));
        Assert.Equal(17L, db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ledger_receipts')"));
        Assert.Equal(5L, db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ledger_receipt_groups')"));
        Assert.Equal(13L, db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ledger_receipt_details')"));
        Assert.Equal(3L, db.Scalar("SELECT COUNT(*) FROM pragma_table_info('ledger_receipt_detail_people')"));
        Assert.Equal(4L, db.Scalar("SELECT COUNT(*) FROM tax_categories"));
        Assert.Equal(0L, db.Scalar("SELECT COUNT(*) FROM tax_rate_history"));
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await DatabaseInitializer.EnsureReadyAsync(context, default);
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(LedgerReceipt), typeof(LedgerReceiptGroup), typeof(LedgerReceiptDetail), typeof(LedgerReceiptDetailPerson) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("LedgerId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.DoesNotContain(entity.GetProperties(), property => property.IsShadowProperty());
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
            Assert.DoesNotContain(entity.GetIndexes(), index => index.IsUnique && index.Properties.Count == 1);
        }
        Assert.True(model.FindEntityType(typeof(LedgerReceipt))!.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.Null(model.FindEntityType(typeof(LedgerReceiptGroup))!.FindProperty("Revision"));
        Assert.Null(model.FindEntityType(typeof(LedgerReceiptDetail))!.FindProperty("Revision"));
        Assert.Equal("TEXT", model.FindEntityType(typeof(LedgerReceiptDetail))!.FindProperty("TaxRate")!.GetColumnType());
    }

    [Fact]
    public async Task SameIdsAcrossLedgersAreAllowedButAllFourPrimaryKeysRejectDuplicates()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var receipt = Id(); var group = Id(); var detail = Id();
        foreach (var ledger in new[] { seed.Ledger, seed.OtherLedger })
        {
            InsertReceipt(connection, seed, ledger, receipt);
            InsertGroup(connection, ledger, group, receipt, seed.Category);
            InsertDetail(connection, ledger, detail, group);
            InsertPerson(connection, ledger, detail, seed.Person);
            Constraint(() => InsertReceipt(connection, seed, ledger, receipt));
            Constraint(() => InsertGroup(connection, ledger, group, receipt, seed.Category));
            Constraint(() => InsertDetail(connection, ledger, detail, group));
            Constraint(() => InsertPerson(connection, ledger, detail, seed.Person));
        }
        Assert.Equal(2L, db.Scalar("SELECT COUNT(*) FROM ledger_receipts"));
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("receipt")]
    [InlineData("category")]
    [InlineData("group")]
    [InlineData("detail")]
    [InlineData("person")]
    public async Task EveryBusinessForeignKeyRejectsReferencesOnlyPresentInAnotherLedger(string target)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var receipt = Id(); var group = Id(); var detail = Id();
        InsertReceipt(connection, seed, seed.OtherLedger, receipt);
        InsertGroup(connection, seed.OtherLedger, group, receipt, seed.Category);
        InsertDetail(connection, seed.OtherLedger, detail, group);
        var localReceipt = Id(); var localGroup = Id(); var localDetail = Id();
        InsertReceipt(connection, seed, seed.Ledger, localReceipt);
        InsertGroup(connection, seed.Ledger, localGroup, localReceipt, seed.Category);
        InsertDetail(connection, seed.Ledger, localDetail, localGroup);
        Action invalid = target switch
        {
            "payment" => () => InsertReceipt(connection, seed, seed.Ledger, Id(), seed.OtherPayment),
            "receipt" => () => InsertGroup(connection, seed.Ledger, Id(), receipt, seed.Category),
            "category" => () => InsertGroup(connection, seed.Ledger, Id(), localReceipt, seed.OtherCategory),
            "group" => () => InsertDetail(connection, seed.Ledger, Id(), group),
            "detail" => () => InsertPerson(connection, seed.Ledger, detail, seed.Person),
            _ => () => InsertPerson(connection, seed.Ledger, localDetail, seed.OtherPerson)
        };
        Constraint(invalid, 787);
    }

    [Fact]
    public async Task OneCategoryPerReceiptIsUniqueButCanBeReusedByOtherReceipts()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var first = Id(); var second = Id();
        InsertReceipt(connection, seed, seed.Ledger, first); InsertReceipt(connection, seed, seed.Ledger, second);
        InsertGroup(connection, seed.Ledger, Id(), first, seed.Category);
        Constraint(() => InsertGroup(connection, seed.Ledger, Id(), first, seed.Category), 2067);
        InsertGroup(connection, seed.Ledger, Id(), second, seed.Category);
    }

    [Theory]
    [InlineData("tax_inclusion_mode", "2")]
    [InlineData("tax_rounding_mode", "'unknown'")]
    [InlineData("total_amount", "2147483648")]
    [InlineData("total_amount", "-2147483649")]
    [InlineData("total_amount", "0.5")]
    [InlineData("total_amount", "NULL")]
    [InlineData("transaction_at", "'2026-02-30T12:00:00'")]
    [InlineData("transaction_at", "'2026-04-01T24:00:00'")]
    [InlineData("transaction_at", "'0000-01-01T00:00:00'")]
    [InlineData("transaction_at", "'2026-04-01 12:00:00'")]
    [InlineData("transaction_at", "'2026-04-01T12:00:00Z'")]
    [InlineData("transaction_at", "'2026-04-01T12:00:00.1'")]
    public async Task ReceiptChecksRejectInvalidEnumsAmountsAndLocalTimestamps(string column, string value)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); InsertReceipt(connection, seed, seed.Ledger, Id());
        Constraint(() => Execute(connection, $"UPDATE ledger_receipts SET {column}={value}"));
    }

    [Theory]
    [InlineData("NULL", "'Week'")]
    [InlineData("1", "NULL")]
    [InlineData("0", "'Week'")]
    [InlineData("-1", "'Month'")]
    [InlineData("1.5", "'Month'")]
    [InlineData("1", "'Day'")]
    public async Task RecurrenceRejectsUnpairedNullsInvalidIntervalsAndUnknownUnits(string interval, string unit)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); InsertReceipt(connection, seed, seed.Ledger, Id());
        Constraint(() => Execute(connection, $"UPDATE ledger_receipts SET recurrence_interval={interval},recurrence_unit={unit}"));
    }

    [Theory]
    [InlineData("display_order", "-1")]
    [InlineData("unit_price", "2147483648")]
    [InlineData("unit_price", "-2147483649")]
    [InlineData("unit_price", "0.5")]
    [InlineData("unit_discount_amount", "2147483648")]
    [InlineData("unit_discount_amount", "-2147483649")]
    [InlineData("quantity", "0")]
    [InlineData("quantity", "-1")]
    [InlineData("quantity", "2147483648")]
    [InlineData("quantity", "1.5")]
    [InlineData("expense_type", "'Unknown'")]
    [InlineData("occurrence_type", "'Unknown'")]
    public async Task DetailChecksRejectInvalidAmountsQuantityOrderAndEnums(string column, string value)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); InsertTree(connection, seed);
        Constraint(() => Execute(connection, $"UPDATE ledger_receipt_details SET {column}={value}"));
    }

    [Fact]
    public async Task BoundaryAmountsLocalDatesDuplicateDisplayOrdersAndAllEnumValuesAreAccepted()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); var tree = InsertTree(connection, seed);
        foreach (var total in new[] { int.MinValue, 0, int.MaxValue })
            Execute(connection, "UPDATE ledger_receipts SET total_amount=$value", ("$value", total));
        foreach (var at in new[] { "0001-01-01T00:00:00", "2024-02-29T23:59:59", "9999-12-31T23:59:59" })
            Execute(connection, "UPDATE ledger_receipts SET transaction_at=$at", ("$at", at));
        foreach (var unit in new[] { "Week", "Month", "Year" })
            Execute(connection, "UPDATE ledger_receipts SET recurrence_interval=3,recurrence_unit=$unit", ("$unit", unit));
        Execute(connection, "UPDATE ledger_receipts SET recurrence_interval=NULL,recurrence_unit=NULL,tax_inclusion_mode=1,tax_rounding_mode='half_up'");
        foreach (var expense in new[] { "Fixed", "Variable", "Excluded" })
        foreach (var occurrence in new[] { "Regular", "Irregular", "Temporary" })
            Execute(connection, "UPDATE ledger_receipt_details SET expense_type=$expense,occurrence_type=$occurrence,unit_price=-2147483648,unit_discount_amount=2147483647,quantity=2147483647",
                ("$expense", expense), ("$occurrence", occurrence));
        InsertDetail(connection, seed.Ledger, Id(), tree.Group); // 同順位も許容。対象人物0人も許容。
    }

    [Theory]
    [InlineData("ledger_receipts", "shop_name")]
    [InlineData("ledger_receipts", "payment_method_id")]
    [InlineData("ledger_receipts", "revision")]
    [InlineData("ledger_receipts", "created_by_auth_user_id")]
    [InlineData("ledger_receipts", "updated_by_auth_user_id")]
    [InlineData("ledger_receipts", "created_at_utc")]
    [InlineData("ledger_receipts", "updated_at_utc")]
    [InlineData("ledger_receipt_groups", "category_id")]
    [InlineData("ledger_receipt_details", "name")]
    [InlineData("ledger_receipt_details", "discount_rate")]
    public async Task RequiredValuesCannotBeNull(string table, string column)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); InsertTree(connection, seed);
        Constraint(() => Execute(connection, $"UPDATE {table} SET {column}=NULL"));
    }

    [Fact]
    public async Task MastersAuditUsersAndParentsAreRestrictedAndExplicitChildFirstDeletionSucceeds()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); InsertTree(connection, seed);
        foreach (var sql in new[] {
            "DELETE FROM ledger_receipts", "DELETE FROM ledger_receipt_groups", "DELETE FROM ledger_receipt_details",
            "DELETE FROM ledger_categories", "DELETE FROM ledger_payment_methods", "DELETE FROM ledger_people", "DELETE FROM ledgers",
            $"DELETE FROM AspNetUsers WHERE Id='{seed.User}'", $"DELETE FROM AspNetUsers WHERE Id='{seed.Editor}'" })
            Constraint(() => Execute(connection, sql));
        Constraint(() => Execute(connection, "UPDATE ledger_receipts SET created_by_auth_user_id='missing'"), 787);
        Constraint(() => Execute(connection, "UPDATE ledger_receipts SET updated_by_auth_user_id='missing'"), 787);
        Execute(connection, "DELETE FROM ledger_receipt_detail_people; DELETE FROM ledger_receipt_details; DELETE FROM ledger_receipt_groups; DELETE FROM ledger_receipts;");
        foreach (var table in ReceiptTables) Assert.Equal(0L, db.Scalar($"SELECT COUNT(*) FROM {table}"));
        Assert.Equal(2L, db.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(3L, db.Scalar("SELECT COUNT(*) FROM ledger_categories"));
    }

    [Fact]
    public async Task EfRoundTripKeepsDecimalPrecisionExemptVersusZeroLocalSecondsAndParentRevision()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection(); var tree = InsertTree(connection, seed);
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var receipt = await context.Set<LedgerReceipt>().SingleAsync();
        var originalRevision = receipt.Revision;
        receipt.TransactionAt = new DateTime(2026, 11, 10, 23, 59, 58, DateTimeKind.Unspecified);
        var detail = await context.Set<LedgerReceiptDetail>().SingleAsync();
        const decimal exact = 8.123456789012345678901234567m;
        detail.DiscountRate = exact; detail.TaxRate = exact;
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        Assert.Equal(exact, (await context.Set<LedgerReceiptDetail>().SingleAsync()).TaxRate);
        Assert.Equal(exact, (await context.Set<LedgerReceiptDetail>().SingleAsync()).DiscountRate);
        Assert.Equal("text", db.Scalar("SELECT typeof(tax_rate) FROM ledger_receipt_details"));
        Assert.Equal("text", db.Scalar("SELECT typeof(discount_rate) FROM ledger_receipt_details"));
        Assert.Equal("2026-11-10T23:59:58", db.Scalar("SELECT transaction_at FROM ledger_receipts"));
        Assert.Equal(DateTimeKind.Unspecified, (await context.Set<LedgerReceipt>().SingleAsync()).TransactionAt.Kind);
        InsertDetail(connection, seed.Ledger, Id(), tree.Group, "0");
        InsertDetail(connection, seed.Ledger, Id(), tree.Group, null);
        var rates = await context.Set<LedgerReceiptDetail>().AsNoTracking().Select(row => row.TaxRate).ToArrayAsync();
        Assert.Contains(0m, rates); Assert.Contains(null, rates); Assert.Contains(exact, rates);
        Assert.Equal(originalRevision, (await context.Set<LedgerReceipt>().SingleAsync()).Revision);
    }

    [Fact]
    public async Task ReinitializationAndConcurrentInitializationPreserveAllExistingRowsAndRevisions()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using (var connection = db.OpenForeignKeyConnection()) InsertTree(connection, seed);
        var before = db.Snapshot(db.Tables());
        await Task.WhenAll(db.InitializeAsync(), db.InitializeAsync());
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData("DROP INDEX ix_ledger_receipts_transaction")]
    [InlineData("DROP INDEX ux_ledger_receipt_groups_category")]
    [InlineData("DROP INDEX ix_ledger_receipt_details_order")]
    [InlineData("DROP TABLE ledger_receipt_detail_people")]
    [InlineData("PRAGMA writable_schema=ON; UPDATE sqlite_master SET sql=replace(sql, 'ON DELETE RESTRICT', 'ON DELETE CASCADE') WHERE name='ledger_receipts'; PRAGMA writable_schema=OFF;")]
    [InlineData("PRAGMA writable_schema=ON; UPDATE sqlite_master SET sql=replace(sql, 'display_order >= 0', 'display_order >= -1') WHERE name='ledger_receipt_details'; PRAGMA writable_schema=OFF;")]
    public async Task MissingOrChangedReceiptStructureIsRefusedWithoutRepairOnInitializationAndNormalUse(string damage)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using (var connection = db.OpenForeignKeyConnection()) InsertTree(connection, seed);
        db.Execute(damage); var before = db.Snapshot(db.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.EnsureReadyAsync(scope.ServiceProvider.GetRequiredService<ApiDbContext>(), default));
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task PreviousDatabaseWithoutReceiptTablesIsRejectedAndPreserved()
    {
        using var db = new AuthDatabaseFixture(); await SeedAsync(db);
        db.Execute("DROP TABLE ledger_receipt_detail_people; DROP TABLE ledger_receipt_details; DROP TABLE ledger_receipt_groups; DROP TABLE ledger_receipts;");
        var before = db.Snapshot(db.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task ReinitializationRejectsBrokenReceiptReferencesWithoutRepairingSavedRows()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using (var connection = db.OpenForeignKeyConnection()) InsertTree(connection, seed);
        db.Execute("UPDATE ledger_receipt_details SET group_id='missing'"); // 制約無効の外部接続による破損を再現。
        var before = db.Snapshot(db.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task EfParentRevisionPreventsStaleOverwriteWithoutChangingChildIdsOrMasterVersions()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using (var connection = db.OpenForeignKeyConnection()) InsertTree(connection, seed);
        await using var services = db.CreateServices();
        await using var first = services.CreateAsyncScope(); await using var second = services.CreateAsyncScope();
        var context = first.ServiceProvider.GetRequiredService<ApiDbContext>();
        var stale = second.ServiceProvider.GetRequiredService<ApiDbContext>();
        var winner = await context.Set<LedgerReceipt>().SingleAsync();
        var loser = await stale.Set<LedgerReceipt>().SingleAsync();
        var children = db.Snapshot(["ledger_receipt_groups", "ledger_receipt_details", "ledger_receipt_detail_people", "ledgers", "ledger_categories", "ledger_people", "ledger_payment_methods"]);
        winner.Memo = "保存済み"; winner.Revision = Guid.NewGuid().ToString("N");
        await context.SaveChangesAsync();
        loser.Memo = "古い編集"; loser.Revision = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal("保存済み", db.Scalar("SELECT memo FROM ledger_receipts"));
        Assert.Equal(winner.Revision, db.Scalar("SELECT revision FROM ledger_receipts"));
        Assert.Equal(children, db.Snapshot(["ledger_receipt_groups", "ledger_receipt_details", "ledger_receipt_detail_people", "ledgers", "ledger_categories", "ledger_people", "ledger_payment_methods"]));
    }

    [Fact]
    public async Task ForeignKeyViolationRollsBackReceiptTreeAndLeavesExistingDataUnchanged()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var before = db.Snapshot(db.Tables());
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ledger_receipts(ledger_id,receipt_id,transaction_at,shop_name,payment_method_id,tax_inclusion_mode,tax_rounding_mode,total_amount,revision,created_by_auth_user_id,created_at_utc,updated_by_auth_user_id,updated_at_utc)
                VALUES($ledger,$receipt,'2026-11-10T12:34:56','店舗',$payment,0,'floor',100,$revision,$user,'2026-11-10 03:34:56',$user,'2026-11-10 03:34:56');
                INSERT INTO ledger_receipt_groups(ledger_id,group_id,receipt_id,category_id) VALUES($ledger,$group,$receipt,$category);
                INSERT INTO ledger_receipt_details(ledger_id,detail_id,group_id,display_order,name,unit_price,quantity,unit_discount_amount,discount_rate,expense_type,occurrence_type)
                VALUES($ledger,$detail,$group,0,'明細',100,1,0,'0','Variable','Irregular');
                INSERT INTO ledger_receipt_detail_people(ledger_id,detail_id,person_id) VALUES($ledger,$detail,'missing');
                """;
            foreach (var (name, value) in new[] { ("$ledger", seed.Ledger), ("$receipt", Id()), ("$payment", seed.Payment), ("$revision", Id()),
                ("$user", seed.User), ("$group", Id()), ("$category", seed.Category), ("$detail", Id()) })
                command.Parameters.AddWithValue(name, value);
            Constraint(() => command.ExecuteNonQuery(), 787);
            command.CommandText = "SELECT COUNT(*) FROM ledger_receipts"; command.Parameters.Clear();
            Assert.Equal(1L, command.ExecuteScalar());
            transaction.Rollback();
        }
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task KeysetIndexSupportsBinaryDescendingOrderIncludingEqualTimestampsWithoutTemporarySort()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var ids = Enumerable.Range(0, 4).Select(_ => Id()).Order(StringComparer.Ordinal).ToArray();
        foreach (var id in ids.Reverse()) InsertReceipt(connection, seed, seed.Ledger, id);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT receipt_id FROM ledger_receipts WHERE ledger_id=$ledger ORDER BY transaction_at DESC,receipt_id COLLATE BINARY DESC LIMIT 2";
        command.Parameters.AddWithValue("$ledger", seed.Ledger);
        var found = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) found.Add(reader.GetString(0));
        Assert.Equal(ids.Reverse().Take(2), found);
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        var plan = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) plan.Add(reader.GetString(3));
        Assert.Contains(plan, line => line.Contains("ix_ledger_receipts_transaction", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));
    }

    private sealed record Seed(string Ledger, string OtherLedger, string User, string Editor, string Person, string Payment, string Category,
        string OtherPerson, string OtherPayment, string OtherCategory);

    private static async Task<Seed> SeedAsync(AuthDatabaseFixture db)
    {
        await db.InitializeAsync();
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var user = new AuthUser { Id = Id(), UserName = Id() }; var editor = new AuthUser { Id = Id(), UserName = Id() };
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
        Assert.True((await users.CreateAsync(user)).Succeeded); Assert.True((await users.CreateAsync(editor)).Succeeded);
        var seed = new Seed(Id(), Id(), user.Id, editor.Id, Id(), Id(), Id(), Id(), Id(), Id());
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        foreach (var ledger in new[] { seed.Ledger, seed.OtherLedger })
        {
            context.Add(new Ledger { Id = ledger, Name = "家計簿", CreatedByAuthUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, Revision = Id() });
            context.Add(new LedgerPerson { LedgerId = ledger, Id = seed.Person, Name = "本人", Revision = Id(), PaymentOrderRevision = Id() });
            context.Add(new LedgerPaymentMethod { LedgerId = ledger, Id = seed.Payment, OwnerPersonId = seed.Person, Name = "現金", Revision = Id() });
            context.Add(new LedgerCategory { LedgerId = ledger, Id = seed.Category, Name = "食費", IncomeExpense = IncomeExpenseKind.Expense, TaxCategoryCode = "Standard", Revision = Id() });
        }
        context.Add(new LedgerPerson { LedgerId = seed.OtherLedger, Id = seed.OtherPerson, Name = "別人物", Revision = Id(), PaymentOrderRevision = Id() });
        context.Add(new LedgerPaymentMethod { LedgerId = seed.OtherLedger, Id = seed.OtherPayment, OwnerPersonId = seed.OtherPerson, Name = "別口座", Revision = Id() });
        context.Add(new LedgerCategory { LedgerId = seed.OtherLedger, Id = seed.OtherCategory, Name = "別科目", IncomeExpense = IncomeExpenseKind.Expense, TaxCategoryCode = "Standard", Revision = Id() });
        await context.SaveChangesAsync(); return seed;
    }

    private static (string Receipt, string Group, string Detail) InsertTree(SqliteConnection connection, Seed seed)
    {
        var receipt = Id(); var group = Id(); var detail = Id();
        InsertReceipt(connection, seed, seed.Ledger, receipt); InsertGroup(connection, seed.Ledger, group, receipt, seed.Category);
        InsertDetail(connection, seed.Ledger, detail, group); InsertPerson(connection, seed.Ledger, detail, seed.Person);
        return (receipt, group, detail);
    }

    private static void InsertReceipt(SqliteConnection connection, Seed seed, string ledger, string id, string? payment = null) => Execute(connection, """
        INSERT INTO ledger_receipts(ledger_id,receipt_id,transaction_at,shop_name,payment_method_id,tax_inclusion_mode,tax_rounding_mode,total_amount,revision,created_by_auth_user_id,created_at_utc,updated_by_auth_user_id,updated_at_utc)
        VALUES($ledger,$id,'2026-11-10T12:34:56','店舗',$payment,0,'floor',100,$revision,$user,'2026-11-10 03:34:56',$editor,'2026-11-10 03:34:56')
        """, ("$ledger", ledger), ("$id", id), ("$payment", payment ?? seed.Payment), ("$revision", Guid.NewGuid().ToString("N")), ("$user", seed.User), ("$editor", seed.Editor));
    private static void InsertGroup(SqliteConnection connection, string ledger, string id, string receipt, string category) => Execute(connection,
        "INSERT INTO ledger_receipt_groups(ledger_id,group_id,receipt_id,category_id) VALUES($ledger,$id,$receipt,$category)",
        ("$ledger", ledger), ("$id", id), ("$receipt", receipt), ("$category", category));
    private static void InsertDetail(SqliteConnection connection, string ledger, string id, string group, string? tax = "10") => Execute(connection, """
        INSERT INTO ledger_receipt_details(ledger_id,detail_id,group_id,display_order,name,unit_price,quantity,unit_discount_amount,discount_rate,tax_rate,expense_type,occurrence_type)
        VALUES($ledger,$id,$group,0,'明細',100,1,0,'0',$tax,'Variable','Irregular')
        """, ("$ledger", ledger), ("$id", id), ("$group", group), ("$tax", tax));
    private static void InsertPerson(SqliteConnection connection, string ledger, string detail, string person) => Execute(connection,
        "INSERT INTO ledger_receipt_detail_people(ledger_id,detail_id,person_id) VALUES($ledger,$detail,$person)",
        ("$ledger", ledger), ("$detail", detail), ("$person", person));
    private static string Id() => Guid.CreateVersion7().ToString("N");
    private static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
    private static void Constraint(Action operation, int? extended = null)
    {
        var error = Assert.Throws<SqliteException>(operation); Assert.Equal(19, error.SqliteErrorCode);
        if (extended is not null) Assert.Equal(extended, error.SqliteExtendedErrorCode);
    }
}
