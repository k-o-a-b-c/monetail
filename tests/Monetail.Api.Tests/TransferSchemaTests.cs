using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Features.Monetail.Ledgers;
using Monetail.Api.Features.Monetail.Transfers;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>既存の本番DI/認証/家計簿fixtureを共用し、振替の新規DB定義だけを検証する。</summary>
public sealed partial class ReceiptSchemaTests
{
    [Fact]
    public async Task TransferFreshDatabaseHasThirteenTypedColumnsAndRequiredMetadata()
    {
        using var db = new AuthDatabaseFixture(createFile: false); await db.InitializeAsync();
        Assert.Equal(0L, db.Scalar("SELECT count(*) FROM ledger_transfers"));
        var columns = TransferRows(db, "PRAGMA table_info(ledger_transfers)");
        Assert.Equal(13, columns.Count);
        Assert.Equal(new[] { "amount", "created_at_utc", "created_by_auth_user_id", "from_payment_method_id", "ledger_id", "memo", "name", "revision", "to_payment_method_id", "transaction_at", "transfer_id", "updated_at_utc", "updated_by_auth_user_id" }, columns.Select(c => c[1]).Order());
        foreach (var column in columns)
        {
            Assert.Equal(column[1] == "amount" ? "INTEGER" : "TEXT", column[2]);
            Assert.Equal(column[1] == "memo" ? "0" : "1", column[3]); Assert.Null(column[4]);
            Assert.Equal(column[1] == "ledger_id" ? "1" : column[1] == "transfer_id" ? "2" : "0", column[5]);
        }
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await DatabaseInitializer.EnsureReadyAsync(context, default);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LedgerTransfer))!;
        Assert.Equal(13, entity.GetProperties().Count()); Assert.DoesNotContain(entity.GetProperties(), p => p.IsShadowProperty());
        Assert.Equal(new[] { "LedgerId", "Id" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(5, entity.GetForeignKeys().Count()); Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.True(entity.FindProperty(nameof(LedgerTransfer.Revision))!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, entity.FindProperty(nameof(LedgerTransfer.Id))!.ValueGenerated);
        Assert.Equal(4, entity.GetCheckConstraints().Count());
        Assert.DoesNotContain(db.Tables(), name => name.Contains("Migration", StringComparison.Ordinal));
        Assert.Equal(0L, db.Scalar("SELECT count(*) FROM tax_rate_history"));
    }

    [Fact]
    public async Task TransferEfRoundTripPreservesLocalSecondsUtcAuditAndOriginalStrings()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db);
        var row = NewTransfer(seed); var otherTables = db.Tables().Except(["ledger_transfers"]).ToArray(); var before = db.Snapshot(otherTables);
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); context.Add(row); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var actual = await context.Set<LedgerTransfer>().SingleAsync();
        Assert.Equal("2026-11-15T12:34:56", db.Scalar("SELECT transaction_at FROM ledger_transfers"));
        Assert.Equal(DateTimeKind.Unspecified, actual.TransactionAt.Kind); Assert.Equal(row.TransactionAt.Ticks, actual.TransactionAt.Ticks);
        Assert.Equal(row.CreatedAtUtc.Ticks, actual.CreatedAtUtc.Ticks); Assert.Equal(row.UpdatedAtUtc.Ticks, actual.UpdatedAtUtc.Ticks);
        Assert.Equal(row.Id, actual.Id); Assert.Equal(row.Revision, actual.Revision); Assert.Equal("  振替  ", actual.Name); Assert.Null(actual.Memo);
        Assert.Equal(before, db.Snapshot(otherTables));
        var ownerCount = db.Scalar("SELECT count(DISTINCT owner_person_id) FROM ledger_payment_methods WHERE ledger_id='" + seed.Core.Ledger + "' AND payment_method_id IN ('" + row.FromPaymentMethodId + "','" + row.ToPaymentMethodId + "')");
        Assert.Equal(2L, ownerCount);
    }

    [Fact]
    public async Task TransferCompositeKeyIsLedgerScopedAndNamesAreNotUnique()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db);
        var row = NewTransfer(seed); await SaveTransferAsync(db, row);
        await SaveTransferAsync(db, NewTransfer(seed, seed.Core.OtherLedger, row.Id));
        await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables());
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveTransferAsync(db, NewTransfer(seed, id: row.Id)));
        Assert.Equal(before, db.Snapshot(db.Tables())); Assert.Equal(3L, db.Scalar("SELECT count(*) FROM ledger_transfers"));
    }

    [Theory]
    [InlineData("ledger_id")]
    [InlineData("transfer_id")]
    [InlineData("transaction_at")]
    [InlineData("name")]
    [InlineData("from_payment_method_id")]
    [InlineData("to_payment_method_id")]
    [InlineData("amount")]
    [InlineData("revision")]
    [InlineData("created_by_auth_user_id")]
    [InlineData("created_at_utc")]
    [InlineData("updated_by_auth_user_id")]
    [InlineData("updated_at_utc")]
    public async Task TransferRequiredColumnsRejectNullWithoutChangingData(string column)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        Constraint(() => Execute(connection, $"UPDATE ledger_transfers SET {column}=NULL"), 1299);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("1.5")]
    [InlineData("'1+2'")]
    [InlineData("'abc'")]
    [InlineData("X'01'")]
    public async Task TransferAmountRejectsNonPositiveOutOfRangeAndNonIntegerStorage(string expression)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        Constraint(() => Execute(connection, $"UPDATE ledger_transfers SET amount={expression}"), 275);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task TransferAmountAcceptsInt32PositiveBoundaries(int amount)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); var row = NewTransfer(seed); row.Amount = amount;
        await SaveTransferAsync(db, row); Assert.Equal((long)amount, db.Scalar("SELECT amount FROM ledger_transfers"));
        Assert.Equal("integer", db.Scalar("SELECT typeof(amount) FROM ledger_transfers"));
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "   ")]
    [InlineData("name", "overlong")]
    [InlineData("memo", "")]
    [InlineData("memo", "overlong")]
    [InlineData("memo", "a\nb")]
    [InlineData("memo", "a\rb")]
    [InlineData("memo", "a\r\nb")]
    public async Task TransferBasicStringChecksRejectInvalidValuesWithoutChangingData(string column, string value)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        if (value == "overlong") value = new string('あ', 101);
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        Constraint(() => Execute(connection, $"UPDATE ledger_transfers SET {column}=$value", ("$value", value)), 275);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task TransferStringsPreserveBoundariesWhitespaceAndDocumentedSqliteUnicodeLimitations()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); var row = NewTransfer(seed);
        row.Name = new string('あ', 100); row.Memo = new string('い', 100); await SaveTransferAsync(db, row);
        Assert.Equal(row.Name, db.Scalar("SELECT name FROM ledger_transfers")); Assert.Equal(row.Memo, db.Scalar("SELECT memo FROM ledger_transfers"));
        using var connection = db.OpenForeignKeyConnection();
        Execute(connection, "UPDATE ledger_transfers SET name=$name,memo=$memo", ("$name", "  名前  "), ("$memo", " "));
        Assert.Equal("  名前  ", db.Scalar("SELECT name FROM ledger_transfers")); Assert.Equal(" ", db.Scalar("SELECT memo FROM ledger_transfers"));
        // DBはコードポイント/ASCII空白/CR・LFだけの基本検証。APIの厳密なUTF-16検証は後続工程。
        var surrogatePairs = string.Concat(Enumerable.Repeat("😀", 51)); Assert.Equal(102, surrogatePairs.Length);
        Execute(connection, "UPDATE ledger_transfers SET name=$name,memo=$memo", ("$name", surrogatePairs), ("$memo", "a\u2028b"));
        Assert.Equal(surrogatePairs, db.Scalar("SELECT name FROM ledger_transfers")); Assert.Equal("a\u2028b", db.Scalar("SELECT memo FROM ledger_transfers"));
        Execute(connection, "UPDATE ledger_transfers SET name=$name,memo=NULL", ("$name", "\u3000"));
        Assert.Equal("\u3000", db.Scalar("SELECT name FROM ledger_transfers")); Assert.Equal(DBNull.Value, db.Scalar("SELECT memo FROM ledger_transfers"));
    }

    [Theory]
    [InlineData("ledger_id", false)]
    [InlineData("from_payment_method_id", false)]
    [InlineData("to_payment_method_id", false)]
    [InlineData("created_by_auth_user_id", false)]
    [InlineData("updated_by_auth_user_id", false)]
    [InlineData("from_payment_method_id", true)]
    [InlineData("to_payment_method_id", true)]
    public async Task TransferForeignKeysRejectMissingOrOtherLedgerReferences(string column, bool otherLedgerOnly)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        Constraint(() => Execute(connection, $"UPDATE ledger_transfers SET {column}=$value", ("$value", otherLedgerOnly ? seed.Core.OtherPayment : Id())), 787);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task TransferSamePaymentMethodIsRejected()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        Constraint(() => Execute(connection, "UPDATE ledger_transfers SET to_payment_method_id=from_payment_method_id"), 275);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("creator")]
    [InlineData("updater")]
    public async Task TransferReferencesRestrictDeletionAndReleaseAfterTransferDeletion(string target)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        using var connection = db.OpenForeignKeyConnection(); var before = db.Snapshot(db.Tables());
        var sql = target is "from" or "to" ? "DELETE FROM ledger_payment_methods WHERE ledger_id=$ledger AND payment_method_id=$id" : "DELETE FROM AspNetUsers WHERE Id=$id";
        var id = target switch { "from" => seed.Core.Payment, "to" => seed.ToPayment, "creator" => seed.Core.Editor, _ => seed.AuditUser };
        Constraint(() => Execute(connection, sql, ("$ledger", seed.Core.Ledger), ("$id", id)), 1811);
        Assert.Equal(before, db.Snapshot(db.Tables()));
        Execute(connection, "DELETE FROM ledger_transfers");
        Execute(connection, sql, ("$ledger", seed.Core.Ledger), ("$id", id));
        Assert.Equal(0L, db.Scalar("SELECT count(*) FROM ledger_transfers"));
    }

    [Fact]
    public async Task TransferForeignKeyDefinitionsAndListIndexMatchContractWithoutExtraSearchIndexes()
    {
        using var db = new AuthDatabaseFixture(); await db.InitializeAsync();
        var foreignKeys = TransferRows(db, "PRAGMA foreign_key_list(ledger_transfers)");
        Assert.Equal(7, foreignKeys.Count); Assert.Equal(5, foreignKeys.Select(r => r[0]).Distinct().Count());
        foreach (var paymentKey in foreignKeys.Where(r => r[2] == "ledger_payment_methods").GroupBy(r => r[0]))
        {
            var columns = paymentKey.OrderBy(r => int.Parse(r[1]!, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(2, columns.Length); Assert.Equal("ledger_id", columns[0][3]); Assert.Equal("ledger_id", columns[0][4]);
            Assert.Contains(columns[1][3], new[] { "from_payment_method_id", "to_payment_method_id" }); Assert.Equal("payment_method_id", columns[1][4]);
        }
        Assert.All(foreignKeys, r => Assert.Equal("RESTRICT", r[6]));
        Assert.Equal(new[] { "AspNetUsers:created_by_auth_user_id:Id", "AspNetUsers:updated_by_auth_user_id:Id", "ledger_payment_methods:from_payment_method_id:payment_method_id", "ledger_payment_methods:ledger_id:ledger_id", "ledger_payment_methods:ledger_id:ledger_id", "ledger_payment_methods:to_payment_method_id:payment_method_id", "ledgers:ledger_id:id" }, foreignKeys.Select(r => $"{r[2]}:{r[3]}:{r[4]}").Order());
        var index = TransferRows(db, "PRAGMA index_xinfo(ix_ledger_transfers_transaction)").Where(r => r[5] == "1").ToArray();
        Assert.Equal(new[] { "ledger_id", "transaction_at", "transfer_id" }, index.Select(r => r[2]));
        Assert.All(index, r => { Assert.Equal("0", r[3]); Assert.Equal("BINARY", r[4]); });
        // EF既定のFK用索引4本と、確定済み一覧索引1本。追加の検索索引/一意制約は設けない。
        var indexes = TransferRows(db, "PRAGMA index_list(ledger_transfers)");
        Assert.Single(indexes, r => r[3] == "pk"); Assert.Equal(5, indexes.Count(r => r[3] == "c"));
        Assert.All(indexes.Where(r => r[3] == "c"), r => Assert.Equal("0", r[2]));
    }

    [Fact]
    public async Task TransferListIndexSupportsDescendingKeysetWithoutTemporarySort()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db);
        var ids = Enumerable.Range(0, 4).Select(_ => Id()).Order(StringComparer.Ordinal).ToArray();
        foreach (var id in ids) await SaveTransferAsync(db, NewTransfer(seed, id: id));
        var older = NewTransfer(seed); older.TransactionAt = older.TransactionAt.AddSeconds(-1); await SaveTransferAsync(db, older);
        await SaveTransferAsync(db, NewTransfer(seed, seed.Core.OtherLedger));
        using var connection = db.OpenForeignKeyConnection(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT transfer_id FROM ledger_transfers WHERE ledger_id=$ledger AND (transaction_at<$time OR (transaction_at=$time AND transfer_id COLLATE BINARY<$id)) ORDER BY transaction_at DESC,transfer_id COLLATE BINARY DESC LIMIT 3";
        command.Parameters.AddWithValue("$ledger", seed.Core.Ledger); command.Parameters.AddWithValue("$time", "2026-11-15T12:34:56"); command.Parameters.AddWithValue("$id", ids[^1]);
        var found = new List<string>(); using (var reader = command.ExecuteReader()) while (reader.Read()) found.Add(reader.GetString(0));
        Assert.Equal(ids.Reverse().Skip(1), found);
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        var plan = new List<string>(); using (var reader = command.ExecuteReader()) while (reader.Read()) plan.Add(reader.GetString(3));
        Assert.Contains(plan, line => line.Contains("ix_ledger_transfers_transaction", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, line => line.Contains("TEMP B-TREE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TransferReinitializationIsNoOpIncludingConcurrentCallsAndExistingReceiptAuthData()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        using (var connection = db.OpenForeignKeyConnection()) InsertTree(connection, seed.Core);
        var before = db.Snapshot(db.Tables()); await Task.WhenAll(db.InitializeAsync(), db.InitializeAsync());
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Theory]
    [InlineData("DROP TABLE ledger_transfers")]
    [InlineData("DROP INDEX ix_ledger_transfers_transaction")]
    public async Task TransferMissingSchemaIsRejectedWithoutAutomaticRepair(string damage)
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db);
        db.Execute(damage); var before = db.Snapshot(db.Tables());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.EnsureReadyAsync(scope.ServiceProvider.GetRequiredService<ApiDbContext>(), default));
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task TransferConstraintFailureCanRollBackWholeDatabaseTransaction()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE ledger_transfers SET memo='変更'; UPDATE ledger_transfers SET amount=0";
            Constraint(() => command.ExecuteNonQuery(), 275);
            command.CommandText = "SELECT memo FROM ledger_transfers"; Assert.Equal("変更", command.ExecuteScalar());
            transaction.Rollback();
        }
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task TransferLedgerReferencePreventsCascadeDeletion()
    {
        using var db = new AuthDatabaseFixture(); var seed = await SeedTransferAsync(db); await SaveTransferAsync(db, NewTransfer(seed));
        var before = db.Snapshot(db.Tables()); using var connection = db.OpenForeignKeyConnection();
        Constraint(() => Execute(connection, "DELETE FROM ledgers WHERE id=$ledger", ("$ledger", seed.Core.Ledger)), 1811);
        Assert.Equal(before, db.Snapshot(db.Tables()));
    }

    [Fact]
    public async Task TransferPreimplementationDatabaseIsRejectedByNormalApiWithoutChangingData()
    {
        await using var app = await LoginTestApp.CreateAsync(); using var client = app.Client(); await LoginTestApp.CsrfAsync(client);
        app.Database.Execute("DROP TABLE ledger_transfers"); var before = app.Database.Snapshot(app.Database.Tables());
        using var response = await client.PostAsync("/auth/login/options", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(before, app.Database.Snapshot(app.Database.Tables()));
    }

    private sealed record TransferSeed(Seed Core, string ToPayment, string AuditUser);
    private static async Task<TransferSeed> SeedTransferAsync(AuthDatabaseFixture db)
    {
        var seed = await SeedAsync(db); var target = Id(); var owner = Id();
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>(); var audit = new AuthUser { Id = Id(), UserName = Id() };
        Assert.True((await users.CreateAsync(audit)).Succeeded);
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        context.Add(new LedgerPerson { LedgerId = seed.Ledger, Id = owner, Name = "別所有者", Revision = Id(), PaymentOrderRevision = Id() });
        context.Add(new LedgerPaymentMethod { LedgerId = seed.Ledger, Id = target, OwnerPersonId = owner, Name = "振替先", Revision = Id() });
        context.Add(new LedgerPaymentMethod { LedgerId = seed.OtherLedger, Id = target, OwnerPersonId = seed.OtherPerson, Name = "振替先", Revision = Id() });
        await context.SaveChangesAsync(); return new(seed, target, audit.Id);
    }
    private static LedgerTransfer NewTransfer(TransferSeed seed, string? ledger = null, string? id = null) => new()
    {
        LedgerId = ledger ?? seed.Core.Ledger, Id = id ?? Guid.CreateVersion7().ToString("N"), TransactionAt = new(2026, 11, 15, 12, 34, 56, DateTimeKind.Unspecified),
        Name = "  振替  ", FromPaymentMethodId = seed.Core.Payment, ToPaymentMethodId = seed.ToPayment, Amount = 1, Revision = Guid.NewGuid().ToString("N"),
        CreatedByAuthUserId = seed.Core.Editor, UpdatedByAuthUserId = seed.AuditUser,
        CreatedAtUtc = new(2026, 11, 15, 3, 34, 56, DateTimeKind.Utc), UpdatedAtUtc = new(2026, 11, 15, 3, 34, 57, DateTimeKind.Utc)
    };
    private static async Task SaveTransferAsync(AuthDatabaseFixture db, LedgerTransfer row)
    {
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>(); context.Add(row); await context.SaveChangesAsync();
    }
    private static List<string?[]> TransferRows(AuthDatabaseFixture db, string sql)
    {
        using var connection = db.OpenForeignKeyConnection(); using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = command.ExecuteReader(); var rows = new List<string?[]>();
        while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)).ToArray());
        return rows;
    }
}
