namespace BoardingHouse.Application.Common;

public static class PasswordHasher
{
    private const int WorkFactor = 12;

    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    public static bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
