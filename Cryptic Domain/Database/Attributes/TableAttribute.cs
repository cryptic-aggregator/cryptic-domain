namespace Cryptic_Domain.Database.Attributes;

public class TableAttribute : Attribute
{
    public TableAttribute(string name)
    {
        TableName = name;
    }
    
    public string TableName { get; private set; }
}