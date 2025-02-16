using Npgsql;

namespace Cryptic_Domain.Database.Interfaces;

public interface IDatabaseConnectionService
{
    public NpgsqlConnection GetDbConnection();
}