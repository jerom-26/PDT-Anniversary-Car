using System;
using System.Collections;
using System.Collections.Generic;

public interface ITokenDiscoveryService
{
    IEnumerator DiscoverOwnedTokens(
        string ownerAddress,
        string chain,
        string collection,
        Action<IReadOnlyList<TokenReference>> onDiscovered,
        Action<string> onError
    );
}
