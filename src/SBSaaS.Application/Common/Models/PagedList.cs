namespace SBSaaS.Application.Common.Models;

/// <summary>
/// Represents a generic paged list response.
/// </summary>
public record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

