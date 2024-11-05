using BlogEngine.Data.Models;
using Microsoft.AspNetCore.Identity;

namespace BlogEngine.Server.Components.Email;

public interface IEnhancedEmailSender<TUser> : IEmailSender<TUser> where TUser : class
{
    Task SendCongratulationsEmail(User user, string email);
}