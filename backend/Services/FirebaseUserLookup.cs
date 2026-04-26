using FirebaseAdmin.Auth;

namespace FinanceManagement.API.Services;

public sealed class FirebaseUserLookup : IFirebaseUserLookup
{
    public async Task<FirebaseUserInfo?> LookupAsync(string emailOrUid, CancellationToken ct)
    {
        try
        {
            // Crude detection: emails contain '@'
            UserRecord record = emailOrUid.Contains('@')
                ? await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(emailOrUid, ct)
                : await FirebaseAuth.DefaultInstance.GetUserAsync(emailOrUid, ct);
            return new FirebaseUserInfo(record.DisplayName, record.Email);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            return null;
        }
    }
}
