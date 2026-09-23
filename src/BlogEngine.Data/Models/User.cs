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
}