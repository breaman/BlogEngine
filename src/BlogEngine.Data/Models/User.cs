using System.ComponentModel.DataAnnotations;
using BlogEngine.Shared.Common;
using BlogEngine.Data.Interfaces;

using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Data.Models;

public class User : IdentityUser<int>, IEntityBase
{
    [MaxLength(FieldLengths.PersonName)]
    public string? FirstName { get; set; }
    [MaxLength(FieldLengths.PersonName)]
    public string? LastName { get; set; }
    public DateTimeOffset MemberSince { get; set; }

    /// <summary>
    /// Name shown for the account in the UI (for example the admin created by <c>/setup</c>); falls back to
    /// <see cref="FirstName"/> and then the user name when empty.
    /// </summary>
    [MaxLength(FieldLengths.PersonName)]
    public string? DisplayName { get; set; }
}