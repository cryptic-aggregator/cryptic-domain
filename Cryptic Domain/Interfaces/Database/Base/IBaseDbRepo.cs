namespace Cryptic_Domain.Interfaces.Database.Base;

public interface IBaseDbRepo<T> where T : IDatabaseTable
{
    public string FullTablePath { get; }

    public Task<T?> GetByIdAsync(int id);
    public Task<T?> GetByIdAsync(Guid id);
    public Task<List<T>> GetByIdsAsync(Guid[] ids);
    public Task<List<T>> GetByIdsAsync(int[] ids);
    public Task<T?> GetByIdAsync(Guid id, int userId);

    public Task DeleteAsync(int id);
    public Task DeleteAsync(Guid id);
}