using Cryptic_Domain.Database.Config.Interfaces;
using Cryptic_Domain.Database.Interfaces;
using Cryptic_Domain.Interfaces;
using Npgsql;

namespace Cryptic_Domain.Services;

public class DatabaseConnectionService : IDatabaseConnectionService
{
    public DatabaseConnectionService(IDatabaseConfiguration config)
    {
        _connStr = config.ConnString;
    }

    public NpgsqlConnection GetDbConnection()
    {
        return new NpgsqlConnection(_connStr);
    }

    private readonly string _connStr;
}