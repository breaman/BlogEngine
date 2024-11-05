using BlogEngine.Data.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Data.Models;

public class Role : IdentityRole<int>, IEntityBase
{
}