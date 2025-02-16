namespace Cryptic_Domain.Exeptions;

public class InvalidDbOperations : Exception
{
    public InvalidDbOperations(string message) : base(message) { }
}