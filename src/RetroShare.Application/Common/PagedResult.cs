namespace RetroShare.Application.Common;

/// <summary>Standard envelope for paginated collections.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long Total { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    public bool HasNext => Page < TotalPages;

    public bool HasPrevious => Page > 1;

    public static PagedResult<T> Create(IReadOnlyList<T> items, long total, int page, int pageSize) =>
        new() { Items = items, Page = page, PageSize = pageSize, Total = total };
}
