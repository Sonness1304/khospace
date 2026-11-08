using System.Data;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace WarehouseRent.Api;

/// <summary>PostgreSQL key-ring adapter. Not registered in production until provider/bootstrap parity is accepted.</summary>
public sealed class PostgresDataProtectionRepository : IXmlRepository
{
    private readonly string connection;
    public PostgresDataProtectionRepository(string connectionString)
    {
        try {
            var cs=new NpgsqlConnectionStringBuilder(connectionString) { Timeout=3, CommandTimeout=3, MaxPoolSize=5, MinPoolSize=0, IncludeErrorDetail=false, LogParameters=false };
            connection=cs.ConnectionString;
        } catch(Exception error) when(error is not OutOfMemoryException) { throw new InvalidOperationException("PostgreSQL key store configuration is invalid."); }
    }
    public const string SchemaSql="""
        CREATE TABLE khospace."DataProtectionKeys" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "FriendlyName" varchar(200) NOT NULL,
            "Xml" text NOT NULL,
            "CreatedAt" timestamptz NOT NULL DEFAULT clock_timestamp(),
            CONSTRAINT "CK_DataProtectionKeys_Bounds" CHECK (char_length("FriendlyName") BETWEEN 1 AND 200 AND octet_length("Xml") BETWEEN 1 AND 65536));
        CREATE FUNCTION khospace.reject_key_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION USING ERRCODE='23514', MESSAGE='Data protection key records are append-only.'; END $$;
        CREATE TRIGGER "TR_DataProtectionKeys_AppendOnly" BEFORE UPDATE OR DELETE ON khospace."DataProtectionKeys"
        FOR EACH ROW EXECUTE FUNCTION khospace.reject_key_mutation();
        """;
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        try {
            using var sql=new NpgsqlConnection(connection); sql.Open();
            using var cmd=new NpgsqlCommand("SELECT \"Xml\" FROM khospace.\"DataProtectionKeys\" ORDER BY \"CreatedAt\",\"Id\" LIMIT 1025",sql) { CommandTimeout=3 };
            using var reader=cmd.ExecuteReader();var result=new List<XElement>();
            while(reader.Read()) {
                if(result.Count>=1024)throw new InvalidOperationException();
                var text=reader.GetString(0);if(text.Length is <1 or >32768)throw new InvalidOperationException();
                using var xml=XmlReader.Create(new StringReader(text),CloudKeyEncryption.SafeXml());var element=XElement.Load(xml);
                SqlDataProtectionRepository.ValidateElement(element);result.Add(element);
            }
            return result;
        } catch(Exception error) when(error is not OutOfMemoryException) { throw new InvalidOperationException("PostgreSQL key store is unavailable or invalid."); }
    }
    public void StoreElement(XElement element,string friendlyName)
    {
        try {
            SqlDataProtectionRepository.ValidateElement(element);
            var text=element.ToString(SaveOptions.DisableFormatting);
            if(text.Length is <1 or >32768||string.IsNullOrEmpty(friendlyName)||friendlyName.Length>200)throw new InvalidOperationException();
            using var sql=new NpgsqlConnection(connection);sql.Open();
            using var cmd=new NpgsqlCommand("INSERT INTO khospace.\"DataProtectionKeys\" (\"Id\",\"FriendlyName\",\"Xml\") VALUES (@id,@name,@xml)",sql) {CommandTimeout=3};
            cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("name",friendlyName);cmd.Parameters.AddWithValue("xml",text);cmd.ExecuteNonQuery();
        } catch(Exception error) when(error is not OutOfMemoryException) { throw new InvalidOperationException("PostgreSQL key store write failed."); }
    }
    public void Validate(CloudKeyEncryption encryption)
    {
        foreach(var element in GetAllElements())SqlDataProtectionRepository.ValidateElement(element,encryption);
    }
}
