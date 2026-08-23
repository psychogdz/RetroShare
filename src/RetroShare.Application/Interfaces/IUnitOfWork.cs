namespace RetroShare.Application.Interfaces;

/// <summary>Commits pending changes tracked by the repositories.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
