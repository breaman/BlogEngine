using BlogEngine.Data.Interfaces;
using BlogEngine.Shared.DTOs;
using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Data.Models;

public class User : IdentityUser<int>, IEntityBase
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime MemberSince { get; set; }
    
    public UserDto ToDto()
    {
        return new UserDto()
        {
            UserId = Id,
            FirstName = FirstName,
            LastName = LastName
        };
    }
}