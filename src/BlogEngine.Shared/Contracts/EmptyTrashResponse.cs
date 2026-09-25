namespace BlogEngine.Shared.Contracts;

/// <summary>Response of <c>POST /api/admin/posts/empty-trash</c> (design 6.8, O6).</summary>
/// <param name="Deleted">How many posts were deleted permanently.</param>
public sealed record EmptyTrashResponse(int Deleted);
