namespace Cryptic_Domain.Models.MassTransit;

public record NewWalletConnectedMessage
{
    public int WalletId { get; init; }
    
    public string WalletAddress { get; init; }

    public NewWalletConnectedMessage(int walletId, string walletAddress)
    {
        WalletId = walletId;
        WalletAddress = walletAddress;
    }
}