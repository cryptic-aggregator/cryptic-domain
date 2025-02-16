namespace Cryptic_Domain.Database.Config.Interfaces;

public interface IDatabaseConfiguration
{
    public string ConnString { get; }
    public string Schema { get; }
}