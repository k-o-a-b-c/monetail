using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>支払方法マスタの物理制約、所有者不変性、既存人物の更新版移行を確認する。</summary>
public sealed class PaymentMethodSchemaTests
{
    [Fact]
    public async Task PaymentMethodsEnforceOwnerNamePeriodAndLedgerForeignKeys()
    {
        using var db = new AuthDatabaseFixture();
        await db.MigrateAsync();
        var accountId = await AdminIdAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var ledger = NewId();
        var otherLedger = NewId();
        var first = NewId();
        var second = NewId();
        var outsider = NewId();
        SeedLedger(connection, ledger, accountId);
        SeedLedger(connection, otherLedger, accountId);
        SeedPerson(connection, ledger, first, "父");
        SeedPerson(connection, ledger, second, "母");
        SeedPerson(connection, otherLedger, outsider, "別家計簿の人");

        InsertPayment(connection, ledger, first, NewId(), "現金");
        InsertPayment(connection, ledger, second, NewId(), "現金"); // Different owners may use the same name.
        InsertPayment(connection, otherLedger, outsider, NewId(), "現金");

        AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "現金"));
        AssertConstraint(() => InsertPayment(connection, ledger, outsider, NewId(), "別家計簿の支払方法"));
        AssertConstraint(() => InsertPayment(connection, ledger, "00000000000000000000000000000000", NewId(), "所有者不在"));
        AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "負数順位", order: -1));
        AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "逆転期間",
            from: "2026-12-31", to: "2026-01-01"));
        AssertConstraint(() => Execute(connection, "DELETE FROM ledger_people WHERE ledger_id=@ledger AND person_id=@person",
            ("@ledger", ledger), ("@person", first)));

        Assert.Equal(3L, Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM ledger_payment_methods")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='trg_ledger_payment_methods_owner_immutable'")));
    }

    [Fact]
    public async Task OwnerChangeIsRejectedButNormalEditsAndDeleteRecreateWork()
    {
        using var db = new AuthDatabaseFixture();
        await db.MigrateAsync();
        var accountId = await AdminIdAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var ledger = NewId();
        var first = NewId();
        var second = NewId();
        var original = NewId();
        SeedLedger(connection, ledger, accountId);
        SeedPerson(connection, ledger, first, "父");
        SeedPerson(connection, ledger, second, "母");
        InsertPayment(connection, ledger, first, original, "旧名");

        AssertConstraint(() => Execute(connection,
            "UPDATE ledger_payment_methods SET owner_person_id=@owner WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@owner", second), ("@ledger", ledger), ("@id", original)));

        Execute(connection, "UPDATE ledger_payment_methods SET owner_person_id=@owner WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@owner", first), ("@ledger", ledger), ("@id", original));
        Execute(connection, "UPDATE ledger_payment_methods SET name='更新名', valid_to='2026-12-31', display_order=3 WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original));
        Assert.Equal("更新名", Scalar(connection,
            "SELECT name FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original)));

        using (var tx = connection.BeginTransaction())
        {
            using var valid = connection.CreateCommand();
            valid.Transaction = tx;
            valid.CommandText = "UPDATE ledger_payment_methods SET name='ロールバック対象' WHERE ledger_id=@ledger AND payment_method_id=@id";
            valid.Parameters.AddWithValue("@ledger", ledger);
            valid.Parameters.AddWithValue("@id", original);
            valid.ExecuteNonQuery();

            using var invalid = connection.CreateCommand();
            invalid.Transaction = tx;
            invalid.CommandText = "UPDATE ledger_payment_methods SET owner_person_id=@owner WHERE ledger_id=@ledger AND payment_method_id=@id";
            invalid.Parameters.AddWithValue("@owner", second);
            invalid.Parameters.AddWithValue("@ledger", ledger);
            invalid.Parameters.AddWithValue("@id", original);
            AssertConstraint(() => invalid.ExecuteNonQuery());
            tx.Rollback(); // RAISE(ABORT) cancels only the failed SQL statement.
        }
        Assert.Equal("更新名", Scalar(connection,
            "SELECT name FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original)));

        Execute(connection, "DELETE FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original));
        var recreated = NewId();
        InsertPayment(connection, ledger, second, recreated, "更新名");
        Assert.NotEqual(original, recreated);
        Assert.Equal(second, Scalar(connection,
            "SELECT owner_person_id FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", recreated)));
    }

    [Fact]
    public async Task ExistingPeopleReceiveDistinctUuidOrderRevisionsDuringUpgrade()
    {
        using var db = new AuthDatabaseFixture();
        string accountId;
        await using (var services = db.CreateServices())
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            await context.GetService<IMigrator>().MigrateAsync("20261104020000_AddAccountAdminNote");
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var existing = new AuthUser { Id = NewId(), UserName = "upgrade-account" };
            Assert.True((await users.CreateAsync(existing)).Succeeded);
            accountId = existing.Id;
        }

        var ledger = NewId();
        var first = NewId();
        var second = NewId();
        using (var connection = db.OpenForeignKeyConnection())
        {
            SeedLedger(connection, ledger, accountId);
            // Old schema deliberately lacks payment_order_revision.
            Execute(connection,
                "INSERT INTO ledger_people(ledger_id,person_id,name,valid_from,valid_to,display_order,revision) VALUES(@ledger,@person,'父','2020-04-01','2026-09-30',3,@rev)",
                ("@ledger", ledger), ("@person", first), ("@rev", NewId()));
            Execute(connection,
                "INSERT INTO ledger_people(ledger_id,person_id,name,valid_from,valid_to,display_order,revision) VALUES(@ledger,@person,'母',NULL,'2027-03-31',7,@rev)",
                ("@ledger", ledger), ("@person", second), ("@rev", NewId()));
        }

        ExistingPerson[] peopleBefore;
        using (var connection = db.OpenForeignKeyConnection()) peopleBefore = ExistingPeople(connection);
        var unchangedTables = db.Tables().Where(table => table is not "ledger_people" and not ApiDbContext.HistoryTable).ToArray();
        var otherDataBefore = db.Snapshot(unchangedTables);
        await db.MigrateAsync();
        Assert.Equal(otherDataBefore, db.Snapshot(unchangedTables));
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(peopleBefore, ExistingPeople(connection));
            var firstToken = (string)Scalar(connection, "SELECT payment_order_revision FROM ledger_people WHERE person_id=@person",
                ("@person", first))!;
            var secondToken = (string)Scalar(connection, "SELECT payment_order_revision FROM ledger_people WHERE person_id=@person",
                ("@person", second))!;
            Assert.True(Guid.TryParseExact(firstToken, "N", out _));
            Assert.True(Guid.TryParseExact(secondToken, "N", out _));
            Assert.NotEqual(firstToken, secondToken);
            Assert.NotEqual(Guid.Empty.ToString("N"), firstToken);
            InsertPayment(connection, ledger, first, NewId(), "カード");
        }
        await db.MigrateAsync();
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(peopleBefore, ExistingPeople(connection));
            Assert.Equal(otherDataBefore, db.Snapshot(unchangedTables));
            Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM ledger_payment_methods")));
            Assert.Equal(2L, Convert.ToInt64(Scalar(connection,
                "SELECT COUNT(DISTINCT payment_order_revision) FROM ledger_people")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpDownUpPreservesExistingPeopleAndConstraintsButDownPermanentlyDropsPaymentData(bool hasPaymentData)
    {
        using var db = new AuthDatabaseFixture();
        const string previous = "20261104020000_AddAccountAdminNote";
        await MigrateToAsync(db, previous);
        var account = NewId();
        await using (var services = db.CreateServices())
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
            var user = new AuthUser { Id = account, UserName = account, DisplayName = "移行前アカウント", IsEnabled = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            Assert.True((await users.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo([1, 2, 3], [4, 5, 6], DateTimeOffset.UtcNow,
                0, ["internal"], true, false, false, [], []))).Succeeded);
        }
        var ledger = NewId(); var otherLedger = NewId();
        var first = NewId(); var second = NewId(); var outsider = NewId();
        using (var connection = db.OpenForeignKeyConnection())
        {
            SeedLedger(connection, ledger, account); SeedLedger(connection, otherLedger, account);
            foreach (var (home, id, name, from, to, order) in new[]
            {
                (ledger, first, "父", "2020-04-01", "2026-09-30", 3),
                (ledger, second, "母", (string?)null, "2027-03-31", 9),
                (otherLedger, outsider, "別の人物", "2025-01-01", (string?)null, 2)
            })
                Execute(connection, "INSERT INTO ledger_people(ledger_id,person_id,name,valid_from,valid_to,display_order,revision) VALUES(@ledger,@id,@name,@from,@to,@order,@revision)",
                    ("@ledger", home), ("@id", id), ("@name", name), ("@from", from), ("@to", to), ("@order", order), ("@revision", NewId()));
        }
        var oldTables = db.Tables().Where(table => table != ApiDbContext.HistoryTable).ToArray();
        var before = db.Snapshot(oldTables);
        var unchangedTables = oldTables.Where(table => table != "ledger_people").ToArray();
        var otherDataBefore = db.Snapshot(unchangedTables);
        ExistingPerson[] peopleBefore;
        using (var connection = db.OpenForeignKeyConnection()) peopleBefore = ExistingPeople(connection);
        await db.MigrateAsync();
        Dictionary<string, string> firstVersions;
        var paymentId = NewId();
        string upgradedSchema;
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(peopleBefore, ExistingPeople(connection));
            firstVersions = PaymentVersions(connection);
            upgradedSchema = Schema(connection);
            if (hasPaymentData) InsertPayment(connection, ledger, first, paymentId, "移行前のカード");
            Assert.Equal(hasPaymentData ? 1L : 0L, Scalar(connection, "SELECT COUNT(*) FROM ledger_payment_methods"));
        }
        await MigrateToAsync(db, previous);
        Assert.Equal(before, db.Snapshot(oldTables)); // 人物のDDL・全旧列と他の全テーブルを照合する。
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(peopleBefore, ExistingPeople(connection));
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('ledger_people') WHERE name='payment_order_revision'"));
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_schema WHERE name IN ('ledger_payment_methods','trg_ledger_payment_methods_owner_immutable')"));
            AssertConstraint(() => Execute(connection, "DELETE FROM ledgers WHERE id=@id", ("@id", ledger)));
        }
        await db.MigrateAsync();
        Dictionary<string, string> secondVersions;
        using (var connection = db.OpenForeignKeyConnection())
        {
            Assert.Equal(peopleBefore, ExistingPeople(connection));
            Assert.Equal(otherDataBefore, db.Snapshot(unchangedTables));
            Assert.Equal(upgradedSchema, Schema(connection)); // FK・索引・CHECK・トリガーも復元される。
            secondVersions = PaymentVersions(connection);
            Assert.All(firstVersions, row => Assert.NotEqual(row.Value, secondVersions[row.Key]));
            // Downは表を削除する。支払方法が存在していても復元されず、支払方法順序版も再生成される。
            // 実データがある運用環境では、この往復はバックアップ復元の代わりにならない。
            Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM ledger_payment_methods"));
            InsertPayment(connection, ledger, first, NewId(), "現金");
            InsertPayment(connection, ledger, second, NewId(), "現金");
            AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "現金"));
            AssertConstraint(() => InsertPayment(connection, ledger, outsider, NewId(), "他家計簿"));
            AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "負数", order: -1));
            AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "逆転", from: "2026-12-31", to: "2026-01-01"));
            AssertConstraint(() => Execute(connection, "UPDATE ledger_payment_methods SET owner_person_id=@id WHERE owner_person_id=@old", ("@id", second), ("@old", first)));
            AssertConstraint(() => Execute(connection, "DELETE FROM ledger_people WHERE person_id=@id", ("@id", first)));
            using var check = connection.CreateCommand(); check.CommandText = "PRAGMA foreign_key_check";
            using var reader = check.ExecuteReader(); Assert.False(reader.Read());
        }
        await db.MigrateAsync();
        using (var connection = db.OpenForeignKeyConnection())
            Assert.Equal(secondVersions.OrderBy(row => row.Key), PaymentVersions(connection).OrderBy(row => row.Key));
        await using var modelServices = db.CreateServices(); await using var modelScope = modelServices.CreateAsyncScope();
        Assert.False(modelScope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.HasPendingModelChanges());
    }

    private static async Task MigrateToAsync(AuthDatabaseFixture db, string target)
    {
        await using var services = db.CreateServices(); await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiDbContext>().GetService<IMigrator>().MigrateAsync(target);
    }
    private sealed record ExistingPerson(string Ledger, string Id, string Name, string? From, string? To, long Order, string Revision);
    private static ExistingPerson[] ExistingPeople(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ledger_id,person_id,name,valid_from,valid_to,display_order,revision FROM ledger_people ORDER BY ledger_id,person_id";
        using var reader = command.ExecuteReader(); var rows = new List<ExistingPerson>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5), reader.GetString(6)));
        return rows.ToArray();
    }
    private static Dictionary<string, string> PaymentVersions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT ledger_id,person_id,payment_order_revision FROM ledger_people";
        using var reader = command.ExecuteReader(); var result = new Dictionary<string, string>();
        while (reader.Read())
        {
            var value = reader.GetString(2);
            Assert.True(Guid.TryParseExact(value, "N", out var id)); Assert.NotEqual(Guid.Empty, id);
            Assert.Equal('4', value[12]); Assert.Contains(value[16], "89ab");
            result.Add(reader.GetString(0) + ":" + reader.GetString(1), value);
        }
        Assert.Equal(result.Count, result.Values.Distinct().Count()); return result;
    }
    private static string Schema(SqliteConnection connection) => (string)Scalar(connection,
        "SELECT group_concat(sql, char(10)) FROM (SELECT sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' AND sql IS NOT NULL ORDER BY type,name)")!;

    private static async Task<string> AdminIdAsync(AuthDatabaseFixture db)
    {
        await using var services = db.CreateServices();
        return (await services.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync())!.AuthUserId;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void SeedLedger(SqliteConnection connection, string id, string accountId) =>
        Execute(connection, """
            INSERT INTO ledgers(id,name,created_by_auth_user_id,created_at_utc,revision,people_order_revision)
            VALUES(@ledger,'家計簿',@account,'2026-11-05',@revision,@order_revision)
            """, ("@ledger", id), ("@account", accountId), ("@revision", NewId()), ("@order_revision", NewId()));

    private static void SeedPerson(SqliteConnection connection, string ledger, string id, string name) =>
        Execute(connection, """
            INSERT INTO ledger_people(ledger_id,person_id,name,display_order,revision,payment_order_revision)
            VALUES(@ledger,@id,@name,0,@revision,@payment_revision)
            """, ("@ledger", ledger), ("@id", id), ("@name", name),
            ("@revision", NewId()), ("@payment_revision", NewId()));

    private static void InsertPayment(SqliteConnection connection, string ledger, string owner, string id,
        string name, int order = 0, string? from = null, string? to = null) =>
        Execute(connection, """
            INSERT INTO ledger_payment_methods
                (ledger_id,payment_method_id,owner_person_id,name,display_order,valid_from,valid_to,revision)
            VALUES(@ledger,@id,@owner,@name,@order,@from,@to,@revision)
            """,
            ("@ledger", ledger), ("@id", id), ("@owner", owner), ("@name", name),
            ("@order", order), ("@from", from), ("@to", to), ("@revision", NewId()));

    private static void Execute(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteScalar();
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void AssertConstraint(Action action) =>
        Assert.Equal(19, Assert.Throws<SqliteException>(action).SqliteErrorCode);
}
