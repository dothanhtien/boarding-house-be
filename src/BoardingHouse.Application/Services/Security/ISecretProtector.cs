namespace BoardingHouse.Application.Services.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
