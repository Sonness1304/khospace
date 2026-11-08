using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WarehouseRent.Infrastructure.Persistence;

public static class PostgresRuntimePrivileges
{
    public static async Task ProvisionAsync(AppDbContext db,string role,CancellationToken ct,string? initialPassword=null)
    {
        if (!db.Database.IsNpgsql() || !Regex.IsMatch(role,"^(khospace_api|warehouse_fixture_role_[a-f0-9]{32})$"))
            throw new InvalidOperationException("An explicit application runtime role is required.");
        if(initialPassword is not null && initialPassword.Length is <20 or >128)throw new InvalidOperationException("Private initial role credentials must meet the bootstrap policy.");
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await PostgresMigrations.AssertProvisioningBaselineAsync(db,ct);
        var exists=await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM pg_roles WHERE rolname={role}").SingleAsync(ct);
        var identifier='"'+role+'"';
        // Identifiers cannot be SQL parameters: role is regex-allowlisted above, table names are quoted catalog values.
#pragma warning disable EF1003
        if(exists==0) await db.Database.ExecuteSqlRawAsync("CREATE ROLE "+identifier+" NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS",ct);
        var privileged=await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM pg_roles r WHERE r.rolname={role} AND (r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls OR EXISTS (SELECT 1 FROM pg_auth_members m LEFT JOIN pg_roles member ON member.oid=m.member WHERE m.member=r.oid OR m.roleid=r.oid AND member.rolname NOT IN ('postgres','supabase_admin') AND NOT member.rolsuper) OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relowner=r.oid AND n.nspname='khospace') OR EXISTS(SELECT 1 FROM pg_roles browser WHERE browser.rolname IN ('anon','authenticated','service_role') AND pg_has_role(browser.oid,r.oid,'MEMBER')))").SingleAsync(ct);
        if(privileged!=0)throw new InvalidOperationException("Existing privileged roles cannot be adopted.");
        await db.Database.ExecuteSqlRawAsync("REVOKE ALL ON SCHEMA khospace FROM "+identifier+"; GRANT USAGE ON SCHEMA khospace TO "+identifier+"; REVOKE ALL ON ALL TABLES IN SCHEMA khospace FROM "+identifier+"; REVOKE ALL ON ALL SEQUENCES IN SCHEMA khospace FROM "+identifier+"; REVOKE ALL ON ALL FUNCTIONS IN SCHEMA khospace FROM "+identifier,ct);
        var tables=await db.Database.SqlQuery<string>($"SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname='khospace'").ToListAsync(ct);
        foreach(var table in tables)
        {
            var quoted='"'+table.Replace("\"","\"\"")+ '"';
            var permissions=table=="MigrationHistory"?"SELECT":table=="DataProtectionKeys"?"SELECT,INSERT":"SELECT,INSERT,UPDATE,DELETE";
            await db.Database.ExecuteSqlRawAsync("GRANT "+permissions+" ON khospace."+quoted+" TO "+identifier,ct);
            await db.Database.ExecuteSqlRawAsync("DROP POLICY IF EXISTS khospace_runtime ON khospace."+quoted+"; CREATE POLICY khospace_runtime ON khospace."+quoted+" FOR ALL TO "+identifier+" USING (true) WITH CHECK (true)",ct);
        }
        await db.Database.ExecuteSqlRawAsync("GRANT USAGE ON SEQUENCE khospace.version_tokens TO "+identifier+"; GRANT EXECUTE ON FUNCTION khospace.set_version_token(),khospace.reject_key_mutation() TO "+identifier,ct);
        if(exists==0 && initialPassword is not null)
        {
            // ALTER ROLE cannot bind a password directly. Keep it parameterized in a transaction-local setting,
            // then quote it server-side; never concatenate or log the password in client command text.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('khospace.bootstrap_password',{initialPassword},true); SELECT set_config('khospace.bootstrap_role',{role},true)",ct);
            await db.Database.ExecuteSqlRawAsync("""
                SET LOCAL password_encryption='scram-sha-256';
                DO $activate$ BEGIN EXECUTE format('ALTER ROLE %I LOGIN PASSWORD %L',current_setting('khospace.bootstrap_role'),current_setting('khospace.bootstrap_password'));
                PERFORM set_config('khospace.bootstrap_password','',true); END $activate$;
                """,ct);
        }
        await PostgresMigrations.RecordProvisionedFingerprintAsync(db,ct);
        await transaction.CommitAsync(ct);
#pragma warning restore EF1003
    }

    public static async Task<bool> IsSafeRuntimeAsync(AppDbContext db,CancellationToken ct)
    {
        if(!db.Database.IsNpgsql())return false;
        return await db.Database.SqlQuery<bool>($"SELECT (NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolreplication AND NOT r.rolbypassrls AND NOT has_schema_privilege(current_user,'khospace','CREATE') AND NOT has_table_privilege(current_user,'khospace.\"MigrationHistory\"','INSERT,UPDATE,DELETE,TRUNCATE') AND NOT has_table_privilege(current_user,'khospace.\"DataProtectionKeys\"','UPDATE,DELETE,TRUNCATE') AND NOT EXISTS (SELECT 1 FROM pg_auth_members m LEFT JOIN pg_roles member ON member.oid=m.member WHERE m.member=r.oid OR m.roleid=r.oid AND member.rolname NOT IN ('postgres','supabase_admin') AND NOT member.rolsuper) AND NOT EXISTS (SELECT 1 FROM pg_roles browser WHERE browser.rolname IN ('anon','authenticated','service_role') AND pg_has_role(browser.oid,r.oid,'MEMBER'))) AS \"Value\" FROM pg_roles r WHERE r.rolname=current_user").SingleAsync(ct);
    }
}
