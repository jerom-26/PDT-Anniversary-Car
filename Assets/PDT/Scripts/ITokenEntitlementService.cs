using System;
using System.Collections;

public interface ITokenEntitlementService
{
    IEnumerator ResolveVerifiedTokenEntitlement(
        TokenReference verifiedToken,
        Action<TokenEntitlement> onResolved,
        Action<string> onError
    );
}
