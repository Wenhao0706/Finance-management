using FirebaseAdmin.Auth;

namespace FinanceManagement.API.Services;

public sealed class FirebaseUserLookup : IFirebaseUserLookup
{
    public async Task<FirebaseUserInfo?> LookupAsync(string email, CancellationToken ct)
    {
        try
        {
            var record = await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email, ct);
            return new FirebaseUserInfo(record.DisplayName);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            return null;
        }
    }
}
