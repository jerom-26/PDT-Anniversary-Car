using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ApprovedPDTCollection", menuName = "PDT/Approved Collection")]
public sealed class ApprovedPDTCollection : ScriptableObject
{
    [SerializeField] private string chain = "eip155:80002";
    [SerializeField] private string proxyAddress;
    public string Chain => chain?.Trim();
    public string ProxyAddress => proxyAddress?.Trim();
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Chain) &&
        PDTVerificationPolicy.IsAddress(ProxyAddress);

    public bool Contains(TokenReference token) =>
        token != null &&
        IsConfigured &&
        string.Equals(token.Chain, Chain, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(token.Collection, ProxyAddress, StringComparison.OrdinalIgnoreCase);
}
