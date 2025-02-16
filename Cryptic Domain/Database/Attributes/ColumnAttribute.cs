namespace Cryptic_Domain.Database.Attributes;

public class ColumnAttribute : Attribute
{
    public ColumnAttribute(string name, NpgsqlTypes.NpgsqlDbType dataType)
    {
        Name = name;
        DataType = dataType;
    }

    public string Name { get; private set; }
    public NpgsqlTypes.NpgsqlDbType DataType { get; private set; }
}