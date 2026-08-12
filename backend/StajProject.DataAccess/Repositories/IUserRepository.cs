using StajProject.Entities;

namespace StajProject.DataAccess.Repositories;

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User> AddAsync(User user);
    Task<bool> AnyAsync();
}
