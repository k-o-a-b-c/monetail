using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Taxes;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>科目と共通税区分・履歴のDB制約、移行、既存データ保持を検証する。</summary>
public sealed class CategoryTaxSchemaTests
{
    private const string Previous = "20261105010000_AddLedgerPaymentMethods";

    [Fact]
    public async Task FreshDatabaseSeedsOnlyFourSharedTaxCategoriesAndHasNoModelDrift()
    {
        using var db = new AuthDatabaseFixture();
        var (ledger, other) = await SeedAsync(db);
        await using var services = db.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(new[] { "Exempt", "ReducedFood", "ReducedNewspaper", "Standard" },
            await context.Set<TaxCategory>().OrderBy(row => row.Code).Select(row => row.Code).ToArrayAsync());
        Assert.Empty(await context.Set<LedgerCategory>().ToArrayAsync());
        Assert.Empty(await context.Set<TaxRateHistory>().ToArrayAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var first = await context.Set<Ledger>().SingleAsync(row => row.Id == ledger);
        var second = await context.Set<Ledger>().SingleAsync(row => row.Id == other);
        AssertUuid(first.CategoryOrderRevision);
        AssertUuid(second.CategoryOrderRevision);
        Assert.NotEqual(first.CategoryOrderRevision, second.CategoryOrderRevision);
        Assert.False(context.Model.FindEntityType(typeof(Ledger))!.FindProperty(nameof(Ledger.CategoryOrderRevision))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(LedgerCategory))!.FindProperty(nameof(LedgerCategory.Revision))!.IsConcurrencyToken);
        Assert.Null(context.Model.FindEntityType(typeof(TaxRateHistory))!.FindProperty("LedgerId"));
        await db.MigrateAsync();
        Assert.Equal(4, await context.Set<TaxCategory>().CountAsync());
    }

    [Fact]
    public async Task CategoryIdentityNameUniquenessAndForeignKeysAreLedgerScopedIncludingDisabledRows()
    {
        using var db = new AuthDatabaseFixture();
        var (ledger, other) = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var id = Id();
        InsertCategory(connection, ledger, id, "食費", enabled: false);
        InsertCategory(connection, other, id, "食費"); // 同じID・同じ名前でも別家計簿なら独立。
        Constraint(() => InsertCategory(connection, ledger, Id(), "食費"));
        Constraint(() => InsertCategory(connection, ledger, id, "別の名前"));
        Constraint(() => InsertCategory(connection, Id(), Id(), "不存在の家計簿"));
        Constraint(() => InsertCategory(connection, ledger, Id(), "不明税区分", tax: "Unknown"));
        InsertCategory(connection, ledger, Id(), "Food");
        InsertCategory(connection, ledger, Id(), "food"); // BINARY比較を維持する。
        Constraint(() => Execute(connection, "DELETE FROM tax_categories WHERE code='Standard'"));
        Constraint(() => Execute(connection, "DELETE FROM ledgers WHERE id=@id", ("@id", ledger)));
        Assert.Equal(4L, Scalar(connection, "SELECT COUNT(*) FROM ledger_categories"));
    }

    [Fact]
    public async Task FutureCompositeReferenceBlocksCrossLedgerUseAndDeletionButAllowsRenameAndReenable()
    {
        using var db = new AuthDatabaseFixture();
        var (ledger, other) = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var id = Id();
        InsertCategory(connection, ledger, id, "旧名", order: 7);
        // 後続テーブルの複合FKだけを模擬する。レシート機能の仮実装ではない。
        Execute(connection, """
            CREATE TABLE category_reference_probe(ledger_id TEXT NOT NULL, category_id TEXT NOT NULL,
                FOREIGN KEY(ledger_id,category_id) REFERENCES ledger_categories(ledger_id,category_id) ON DELETE RESTRICT)
            """);
        Constraint(() => Execute(connection, "INSERT INTO category_reference_probe VALUES(@ledger,@id)", ("@ledger", other), ("@id", id)));
        Execute(connection, "INSERT INTO category_reference_probe VALUES(@ledger,@id)", ("@ledger", ledger), ("@id", id));
        Constraint(() => Execute(connection, "DELETE FROM ledger_categories WHERE ledger_id=@ledger AND category_id=@id", ("@ledger", ledger), ("@id", id)));
        Execute(connection, "UPDATE ledger_categories SET name='新名', is_enabled=0 WHERE ledger_id=@ledger AND category_id=@id", ("@ledger", ledger), ("@id", id));
        Assert.Equal("新名", Scalar(connection, "SELECT c.name FROM category_reference_probe r JOIN ledger_categories c USING(ledger_id,category_id)"));
        Execute(connection, "UPDATE ledger_categories SET is_enabled=1 WHERE ledger_id=@ledger AND category_id=@id", ("@ledger", ledger), ("@id", id));
        Assert.Equal(7L, Scalar(connection, "SELECT display_order FROM ledger_categories WHERE category_id=@id", ("@id", id)));
        Execute(connection, "DELETE FROM category_reference_probe");
        Assert.Equal(1, Execute(connection, "DELETE FROM ledger_categories WHERE ledger_id=@ledger AND category_id=@id", ("@ledger", ledger), ("@id", id)));
    }

    [Theory]
    [InlineData("Income", "NotApplicable", "Regular", 1)]
    [InlineData("Income", "NotApplicable", "Regular", 12)]
    [InlineData("Income", "NotApplicable", "Irregular", null)]
    [InlineData("Income", "NotApplicable", "Extraordinary", null)]
    [InlineData("Expense", "Fixed", "Regular", 1)]
    [InlineData("Expense", "Variable", "Regular", 12)]
    [InlineData("Expense", "Fixed", "Irregular", null)]
    [InlineData("Expense", "Variable", "Extraordinary", null)]
    public async Task ValidCategoryDefaultsAreAccepted(string income, string cost, string occurrence, int? months)
    {
        using var db = new AuthDatabaseFixture();
        var (ledger, _) = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        InsertCategory(connection, ledger, Id(), "科目", income: income, cost: cost, occurrence: occurrence, months: months);
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM ledger_categories"));
    }

    [Theory]
    [InlineData("Income", "Fixed", "Irregular", null)]
    [InlineData("Income", "Variable", "Irregular", null)]
    [InlineData("Expense", "NotApplicable", "Irregular", null)]
    [InlineData("Unknown", "NotApplicable", "Irregular", null)]
    [InlineData("Expense", "Unknown", "Irregular", null)]
    [InlineData("Expense", "Fixed", "Unknown", null)]
    [InlineData("Expense", "Fixed", "Regular", null)]
    [InlineData("Expense", "Fixed", "Regular", 0)]
    [InlineData("Expense", "Fixed", "Regular", 2)]
    [InlineData("Expense", "Fixed", "Regular", -1)]
    [InlineData("Expense", "Fixed", "Irregular", 1)]
    [InlineData("Expense", "Fixed", "Extraordinary", 12)]
    public async Task InvalidCategoryDefaultsAreRejectedIncludingNullCheckBoundaries(string income, string cost, string occurrence, int? months)
    {
        using var db = new AuthDatabaseFixture();
        var (ledger, _) = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        Constraint(() => InsertCategory(connection, ledger, Id(), "科目", income: income, cost: cost, occurrence: occurrence, months: months));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM ledger_categories"));
    }

    [Fact]
    public async Task NameUsesDotNetTrimAndUtf16UnitsAndDatabaseRejectsEmptyLongOrInvalidState()
    {
        var category = new LedgerCategory { Name = "　 食 費\t　" };
        Assert.Equal("食 費", category.Name);
        category.Name = string.Concat(Enumerable.Repeat("😀", 50));
        Assert.Equal(100, category.Name.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => category.Name = string.Concat(Enumerable.Repeat("😀", 51)));
        Assert.Throws<ArgumentOutOfRangeException>(() => category.Name = new string('a', 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => category.Name = "\t　 ");
        Assert.Throws<ArgumentNullException>(() => category.Name = null!);
        category.Name = new string('a', 100);
        using var db = new AuthDatabaseFixture();
        var (ledger, _) = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        InsertCategory(connection, ledger, Id(), category.Name);
        Constraint(() => InsertCategory(connection, ledger, Id(), ""));
        Constraint(() => InsertCategory(connection, ledger, Id(), new string('a', 101)));
        Constraint(() => InsertCategory(connection, ledger, Id(), "負数順", order: -1));
        Constraint(() => Execute(connection, "UPDATE ledger_categories SET is_enabled=2"));
        Constraint(() => Execute(connection, "UPDATE ledger_categories SET name=NULL"));
    }

    [Fact]
    public async Task EfNameNormalizationPersistsAndRowConcurrencyIsIndependentOfLedgerVersions()
    {
        using var db = new AuthDatabaseFixture();
        var (ledger, _) = await SeedAsync(db);
        await using var services = db.CreateServices();
        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var parent = await first.Set<Ledger>().SingleAsync(row => row.Id == ledger);
        var before = (parent.Revision, parent.PeopleOrderRevision, parent.CategoryOrderRevision);
        var category = new LedgerCategory { LedgerId = ledger, Id = Id(), Name = "　食費　", TaxCategoryCode = TaxCategory.Standard,
            IncomeExpense = IncomeExpenseKind.Expense, Cost = CategoryCostKind.Fixed, Occurrence = CategoryOccurrenceKind.Irregular, Revision = Id() };
        first.Add(category);
        await first.SaveChangesAsync();
        var stale = await second.Set<LedgerCategory>().SingleAsync();
        category.Name = "生活費"; category.Revision = Id(); await first.SaveChangesAsync();
        stale.IsEnabled = false; stale.Revision = Id();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await second.Entry(stale).ReloadAsync();
        Assert.Equal("生活費", stale.Name); Assert.True(stale.IsEnabled);
        await first.Entry(parent).ReloadAsync();
        Assert.Equal(before, (parent.Revision, parent.PeopleOrderRevision, parent.CategoryOrderRevision));
    }

    [Theory]
    [InlineData("2026-04-01", "2026-09-30")]
    [InlineData("2026-04-01", "2026-04-01")]
    [InlineData("2026-06-01", "2026-06-02")]
    [InlineData("2026-01-01", "2026-12-31")]
    [InlineData("2026-03-31", "2026-04-01")]
    [InlineData("2026-09-30", "2026-10-01")]
    [InlineData("2026-06-01", null)]
    [InlineData("2020-01-01", null)]
    public async Task EveryOverlappingInclusivePeriodIsRejected(string from, string? to)
    {
        using var db = new AuthDatabaseFixture();
        await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        InsertRate(connection, Id(), TaxCategory.Standard, "2026-04-01", "2026-09-30", "10");
        Constraint(() => InsertRate(connection, Id(), TaxCategory.Standard, from, to, "8"));
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM tax_rate_history"));
    }

    [Fact]
    public async Task AdjacentPeriodsIndependentFoodNewspaperAndGapsDoNotCreateFallbackRates()
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        InsertRate(connection, Id(), TaxCategory.ReducedFood, "2026-04-01", "2026-09-30", "8");
        InsertRate(connection, Id(), TaxCategory.ReducedFood, "2026-10-01", "2027-03-31", "1"); // 仮定の試験データだけ。
        InsertRate(connection, Id(), TaxCategory.ReducedFood, "2027-04-01", null, "8");
        InsertRate(connection, Id(), TaxCategory.ReducedNewspaper, "2026-04-01", null, "8");
        foreach (var day in new[] { "2026-04-01", "2026-09-30", "2026-10-01", "2027-04-01" })
            Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM tax_rate_history WHERE tax_category_code='ReducedFood' AND valid_from<=@day AND (valid_to IS NULL OR @day<=valid_to)", ("@day", day)));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM tax_rate_history WHERE tax_category_code='ReducedFood' AND valid_from<='2026-03-31'"));
        Constraint(() => InsertRate(connection, Id(), TaxCategory.ReducedFood, "2027-05-01", null, "8"));
        Assert.Equal("8", Scalar(connection, "SELECT rate FROM tax_rate_history WHERE tax_category_code='ReducedNewspaper'"));
    }

    [Theory]
    [InlineData("2026-02-29", "2026-03-01")]
    [InlineData("2026-04-31", null)]
    [InlineData("2026-13-01", null)]
    [InlineData("2026-00-01", null)]
    [InlineData("2026-01-00", null)]
    [InlineData("2026-4-01", null)]
    [InlineData("0000-01-01", null)]
    [InlineData("invalid", null)]
    [InlineData("2026-04-01", "2026-03-31")]
    [InlineData("2026-04-01", "invalid")]
    [InlineData("2026-04-01", "2026-04-31")]
    public async Task HistoryRejectsInvalidDatesAndReversedPeriods(string from, string? to)
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        Constraint(() => InsertRate(connection, Id(), TaxCategory.Standard, from, to, "10"));
    }

    [Fact]
    public async Task LeapDaySingleDayAndEarliestLatestDateAreAccepted()
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        InsertRate(connection, Id(), TaxCategory.Standard, "0001-01-01", "0001-01-01", "0");
        InsertRate(connection, Id(), TaxCategory.Standard, "2024-02-29", "2024-02-29", "8");
        InsertRate(connection, Id(), TaxCategory.Standard, "9999-12-31", null, "10");
        Assert.Equal(3L, Scalar(connection, "SELECT COUNT(*) FROM tax_rate_history"));
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("ReducedFood")]
    [InlineData("ReducedNewspaper")]
    public async Task TaxableZeroIsStoredButTaxableNullIsRejected(string code)
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        InsertRate(connection, Id(), code, "2026-01-01", "2026-01-01", "0");
        Constraint(() => InsertRate(connection, Id(), code, "2026-01-02", null, null));
        Assert.Equal("0", Scalar(connection, "SELECT rate FROM tax_rate_history"));
    }

    [Fact]
    public async Task ExemptUsesNullAndUnknownCodesAndNullStartAreRejected()
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        InsertRate(connection, Id(), TaxCategory.Exempt, "2026-01-01", null, null);
        Assert.Equal(DBNull.Value, Scalar(connection, "SELECT rate FROM tax_rate_history"));
        Constraint(() => InsertRate(connection, Id(), TaxCategory.Exempt, "2025-01-01", "2025-12-31", "0"));
        Constraint(() => InsertRate(connection, Id(), "Unknown", "2026-01-01", null, "8"));
        Constraint(() => InsertRate(connection, Id(), TaxCategory.Standard, null!, null, "8"));
        Constraint(() => Execute(connection, "DELETE FROM tax_categories WHERE code='Exempt'"));
    }

    [Fact]
    public async Task DecimalRateRoundTripsExactlyWithoutRealConversion()
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        const decimal rate = 8.123456789012345678901234567m;
        context.Add(new TaxRateHistory { Id = Id(), TaxCategoryCode = TaxCategory.Standard,
            ValidFrom = new DateOnly(2024, 2, 29), Rate = rate, Revision = Id() });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        Assert.Equal(rate, (await context.Set<TaxRateHistory>().SingleAsync()).Rate);
        using var connection = db.OpenForeignKeyConnection();
        Assert.Equal("text", Scalar(connection, "SELECT typeof(rate) FROM tax_rate_history"));
    }

    [Fact]
    public async Task UpdateChecksNewPeriodAndCategoryButDoesNotConflictWithItsOwnRow()
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var connection = db.OpenForeignKeyConnection();
        var first = Id(); var second = Id(); var other = Id();
        InsertRate(connection, first, TaxCategory.Standard, "2026-01-01", "2026-06-30", "8");
        InsertRate(connection, second, TaxCategory.Standard, "2026-07-01", null, "10");
        InsertRate(connection, other, TaxCategory.ReducedFood, "2026-07-01", null, "8");
        Execute(connection, "UPDATE tax_rate_history SET rate='9' WHERE id=@id", ("@id", first));
        Constraint(() => Execute(connection, "UPDATE tax_rate_history SET valid_to='2026-07-01' WHERE id=@id", ("@id", first)));
        Constraint(() => Execute(connection, "UPDATE tax_rate_history SET valid_from='2026-06-30' WHERE id=@id", ("@id", second)));
        Constraint(() => Execute(connection, "UPDATE tax_rate_history SET tax_category_code='Standard' WHERE id=@id", ("@id", other)));
        Assert.Equal("2026-06-30", Scalar(connection, "SELECT valid_to FROM tax_rate_history WHERE id=@id", ("@id", first)));
        Execute(connection, "DELETE FROM tax_rate_history WHERE id=@id", ("@id", second));
        Execute(connection, "UPDATE tax_rate_history SET valid_to=NULL WHERE id=@id", ("@id", first));
    }

    [Fact]
    public async Task ConcurrentOverlappingInsertsCannotBothSucceed()
    {
        using var db = new AuthDatabaseFixture(); await db.MigrateAsync();
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            using var connection = db.OpenForeignKeyConnection();
            start.Wait();
            try { InsertRate(connection, Id(), TaxCategory.Standard, "2026-01-01", null, "8"); return true; }
            catch (SqliteException error) when (error.SqliteErrorCode == 19) { return false; }
        })).ToArray();
        start.Set();
        Assert.Single(await Task.WhenAll(tasks), success => success);
        using var check = db.OpenForeignKeyConnection();
        Assert.Equal(1L, Scalar(check, "SELECT COUNT(*) FROM tax_rate_history"));
    }

    [Fact]
    public async Task FailedHistoryWriteCanRollBackCategoryAndIndependentOrderRevisionTogether()
    {
        using var db = new AuthDatabaseFixture(); var (ledger, _) = await SeedAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        InsertRate(connection, Id(), TaxCategory.Standard, "2026-01-01", null, "8");
        var before = Scalar(connection, "SELECT category_order_revision FROM ledgers WHERE id=@id", ("@id", ledger));
        using (var transaction = connection.BeginTransaction())
        {
            ExecuteIn(connection, transaction, "UPDATE ledgers SET category_order_revision=@revision WHERE id=@ledger", ("@revision", Id()), ("@ledger", ledger));
            ExecuteIn(connection, transaction, """
                INSERT INTO ledger_categories(ledger_id,category_id,name,is_enabled,display_order,tax_category_code,income_expense,cost,occurrence,revision)
                VALUES(@ledger,@id,'ロールバック対象',1,0,'Standard','Expense','Fixed','Irregular',@revision)
                """, ("@ledger", ledger), ("@id", Id()), ("@revision", Id()));
            Constraint(() => ExecuteIn(connection, transaction, """
                INSERT INTO tax_rate_history(id,tax_category_code,valid_from,rate,revision)
                VALUES(@id,'Standard','2026-01-01','10',@revision)
                """, ("@id", Id()), ("@revision", Id())));
            transaction.Rollback();
        }
        Assert.Equal(before, Scalar(connection, "SELECT category_order_revision FROM ledgers WHERE id=@id", ("@id", ledger)));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM ledger_categories"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDatabaseUpDownUpPreservesAuthPeoplePaymentsAndVersionsButDownLosesNewMasterData(bool withData)
    {
        using var db = new AuthDatabaseFixture();
        await MigrateToAsync(db, Previous);
        var account = Id(); var ledger = Id(); var other = Id();
        await using (var services = db.CreateServices())
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = new AuthUser { Id = account, UserName = account, DisplayName = "移行前", IsEnabled = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo([1, 2], [3, 4], DateTimeOffset.UtcNow,
                0, ["internal"], true, false, false, [], []))).Succeeded);
        }
        using (var connection = db.OpenForeignKeyConnection())
        {
            foreach (var id in new[] { ledger, other })
                Execute(connection, "INSERT INTO ledgers(id,name,created_by_auth_user_id,created_at_utc,revision,people_order_revision) VALUES(@id,'既存',@account,'2026-11-01',@revision,@order)",
                    ("@id", id), ("@account", account), ("@revision", Id()), ("@order", Id()));
            var person = Id();
            Execute(connection, "INSERT INTO ledger_people(ledger_id,person_id,name,valid_from,valid_to,display_order,revision,payment_order_revision) VALUES(@ledger,@id,'既存人物','2020-04-01','2026-09-30',7,@rev,@order)",
                ("@ledger", ledger), ("@id", person), ("@rev", Id()), ("@order", Id()));
            Execute(connection, "INSERT INTO ledger_payment_methods(ledger_id,payment_method_id,owner_person_id,name,display_order,revision) VALUES(@ledger,@id,@owner,'既存支払方法',3,@rev)",
                ("@ledger", ledger), ("@id", Id()), ("@owner", person), ("@rev", Id()));
        }
        var oldTables = db.Tables().Where(table => table != ApiDbContext.HistoryTable).ToArray();
        var fullBefore = db.Snapshot(oldTables);
        var unchanged = oldTables.Where(table => table != "ledgers").ToArray();
        var unchangedBefore = db.Snapshot(unchanged);
        Dictionary<string, object?> ledgerBefore;
        using (var connection = db.OpenForeignKeyConnection()) ledgerBefore = LedgerValues(connection);
        await db.MigrateAsync();
        Dictionary<string, string> versions;
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(ledgerBefore.OrderBy(row => row.Key), LedgerValues(connection).OrderBy(row => row.Key));
            versions = CategoryVersions(connection);
            if (withData)
            {
                InsertCategory(connection, ledger, Id(), "新科目");
                InsertRate(connection, Id(), TaxCategory.Standard, "2026-01-01", null, "10");
            }
        }
        Assert.Equal(unchangedBefore, db.Snapshot(unchanged));
        await db.MigrateAsync();
        using (var connection = db.OpenForeignKeyConnection())
            Assert.Equal(versions.OrderBy(row => row.Key), CategoryVersions(connection).OrderBy(row => row.Key));
        await MigrateToAsync(db, Previous);
        Assert.Equal(fullBefore, db.Snapshot(oldTables));
        using (var connection = db.OpenForeignKeyConnection())
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE name LIKE 'trg_tax_rate_history_%' OR name IN ('ledger_categories','tax_categories','tax_rate_history')"));
        await db.MigrateAsync();
        Assert.Equal(unchangedBefore, db.Snapshot(unchanged));
        using (var connection = db.OpenForeignKeyConnection())
        {
            var after = CategoryVersions(connection);
            Assert.All(versions, row => Assert.NotEqual(row.Value, after[row.Key]));
            Assert.Equal(ledgerBefore.OrderBy(row => row.Key), LedgerValues(connection).OrderBy(row => row.Key));
            // Downで新マスタは失われる。再Upしてもデータは復元されない。
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM ledger_categories"));
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM tax_rate_history"));
            InsertRate(connection, Id(), TaxCategory.Standard, "2026-01-01", null, "10");
            Constraint(() => InsertRate(connection, Id(), TaxCategory.Standard, "2026-01-01", null, "8"));
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
            using var reader = command.ExecuteReader(); Assert.False(reader.Read());
        }
    }

    [Fact]
    public async Task InitialApiDatabaseUpgradesThroughEntireChainWithoutChangingOriginalAuthPermissionsOrBusinessValues()
    {
        using var db = new AuthDatabaseFixture();
        await db.MigrateToAsync("20261027104007_InitializeApiDatabase");
        var account = Id(); var ledger = Id(); var person = Id();
        await using (var services = db.CreateServices())
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = new AuthUser { Id = account, UserName = account, DisplayName = "初期DBの本人", IsEnabled = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo([5, 6], [7, 8], DateTimeOffset.UtcNow,
                0, ["internal"], true, false, false, [], []))).Succeeded);
        }
        using (var connection = db.OpenForeignKeyConnection())
        {
            Execute(connection, "INSERT INTO ledgers(id,name,created_by_auth_user_id,created_at_utc,revision,people_order_revision) VALUES(@ledger,'初期DBの家計簿',@account,'2026-01-01',@revision,@order)",
                ("@ledger", ledger), ("@account", account), ("@revision", Id()), ("@order", Id()));
            Execute(connection, "INSERT INTO ledger_people(ledger_id,person_id,name,valid_from,valid_to,display_order,revision) VALUES(@ledger,@person,'初期DBの人物','2020-04-01','2026-09-30',7,@revision)",
                ("@ledger", ledger), ("@person", person), ("@revision", Id()));
            Execute(connection, "INSERT INTO ledger_members(ledger_id,auth_user_id,role,joined_at_utc,revision,person_id) VALUES(@ledger,@account,'Admin','2026-01-01',@revision,@person)",
                ("@ledger", ledger), ("@account", account), ("@person", person), ("@revision", Id()));
        }
        // 列が増える2表は旧列を全比較し、他表はDDL・全データをスナップショット比較する。
        var unchanged = db.Tables().Where(table => table is not "ledgers" and not "ledger_people" and not ApiDbContext.HistoryTable).ToArray();
        var before = db.Snapshot(unchanged);
        Dictionary<string, object?> ledgersBefore, peopleBefore;
        using (var connection = db.OpenForeignKeyConnection())
        {
            ledgersBefore = LedgerValues(connection);
            peopleBefore = OriginalPeopleValues(connection);
        }
        await db.MigrateAsync();
        Assert.Equal(before, db.Snapshot(unchanged));
        string version;
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(ledgersBefore.OrderBy(row => row.Key), LedgerValues(connection).OrderBy(row => row.Key));
            Assert.Equal(peopleBefore.OrderBy(row => row.Key), OriginalPeopleValues(connection).OrderBy(row => row.Key));
            version = (string)Scalar(connection, "SELECT category_order_revision FROM ledgers")!;
            AssertUuid(version);
            AssertUuid((string)Scalar(connection, "SELECT payment_order_revision FROM ledger_people")!);
            Assert.Equal(6L, Scalar(connection, "SELECT COUNT(*) FROM __MonetailMigrationsHistory"));
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM ledger_categories"));
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM tax_rate_history"));
        }
        await db.MigrateAsync();
        Assert.Equal(before, db.Snapshot(unchanged));
        using var after = db.OpenForeignKeyConnection();
        Assert.Equal(version, Scalar(after, "SELECT category_order_revision FROM ledgers"));
    }

    private static Dictionary<string, object?> OriginalPeopleValues(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ledger_id,person_id,name,valid_from,valid_to,display_order,revision FROM ledger_people ORDER BY ledger_id,person_id";
        using var reader = command.ExecuteReader(); var result = new Dictionary<string, object?>();
        while (reader.Read()) for (var index = 2; index < reader.FieldCount; index++)
            result[reader.GetString(0) + ":" + reader.GetString(1) + ":" + reader.GetName(index)] = reader.GetValue(index);
        return result;
    }

    private static string Id() => Guid.NewGuid().ToString("N");
    private static void AssertUuid(string value)
    {
        Assert.True(Guid.TryParseExact(value, "N", out var parsed)); Assert.NotEqual(Guid.Empty, parsed);
        Assert.Equal('4', value[12]); Assert.Contains(value[16], "89ab");
    }
    private static async Task<(string Ledger, string Other)> SeedAsync(AuthDatabaseFixture db)
    {
        await db.MigrateAsync();
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var user = new AuthUser { Id = Id(), UserName = Id() };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>().CreateAsync(user)).Succeeded);
        var first = new Ledger { Id = Id(), Name = "家計簿", CreatedByAuthUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, Revision = Id() };
        var second = new Ledger { Id = Id(), Name = "別家計簿", CreatedByAuthUserId = user.Id, CreatedAtUtc = DateTime.UtcNow, Revision = Id() };
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); context.AddRange(first, second);
        await context.SaveChangesAsync(); return (first.Id, second.Id);
    }
    private static async Task MigrateToAsync(AuthDatabaseFixture db, string target)
    {
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiDbContext>().GetService<IMigrator>().MigrateAsync(target);
    }
    private static int InsertCategory(SqliteConnection connection, string ledger, string id, string name, bool enabled = true,
        int order = 0, string tax = "Standard", string income = "Expense", string cost = "Variable", string occurrence = "Irregular", int? months = null) =>
        Execute(connection, """
            INSERT INTO ledger_categories(ledger_id,category_id,name,is_enabled,display_order,tax_category_code,income_expense,cost,occurrence,recurrence_months,revision)
            VALUES(@ledger,@id,@name,@enabled,@order,@tax,@income,@cost,@occurrence,@months,@revision)
            """, ("@ledger", ledger), ("@id", id), ("@name", name), ("@enabled", enabled), ("@order", order), ("@tax", tax),
            ("@income", income), ("@cost", cost), ("@occurrence", occurrence), ("@months", months), ("@revision", Id()));
    private static int InsertRate(SqliteConnection connection, string id, string tax, string from, string? to, string? rate) =>
        Execute(connection, "INSERT INTO tax_rate_history(id,tax_category_code,valid_from,valid_to,rate,revision) VALUES(@id,@tax,@from,@to,@rate,@revision)",
            ("@id", id), ("@tax", tax), ("@from", from), ("@to", to), ("@rate", rate), ("@revision", Id()));
    private static int Execute(SqliteConnection connection, string sql, params (string, object?)[] parameters) => ExecuteIn(connection, null, sql, parameters);
    private static int ExecuteIn(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteNonQuery();
    }
    private static object? Scalar(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteScalar();
    }
    private static void Constraint(Action action) => Assert.Equal(19, Assert.Throws<SqliteException>(action).SqliteErrorCode);
    private static Dictionary<string, string> CategoryVersions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,category_order_revision FROM ledgers ORDER BY id";
        using var reader = command.ExecuteReader(); var result = new Dictionary<string, string>();
        while (reader.Read()) { var version = reader.GetString(1); AssertUuid(version); result.Add(reader.GetString(0), version); }
        Assert.Equal(result.Count, result.Values.Distinct().Count()); return result;
    }
    private static Dictionary<string, object?> LedgerValues(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,name,created_by_auth_user_id,created_at_utc,revision,people_order_revision FROM ledgers ORDER BY id";
        using var reader = command.ExecuteReader(); var result = new Dictionary<string, object?>();
        while (reader.Read()) for (var index = 1; index < reader.FieldCount; index++) result[reader.GetString(0) + ":" + reader.GetName(index)] = reader.GetValue(index);
        return result;
    }
}
