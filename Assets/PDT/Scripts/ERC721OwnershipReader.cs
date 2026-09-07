using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Reown.AppKit.Unity;
using UnityEngine;

[Serializable]
public sealed class VerifiedNFT
{
    public TokenReference tokenReference;
    public string tokenID;
    public VerifiedNFT(TokenReference reference) { tokenReference = reference; tokenID = reference.TokenID; }
}

public class ERC721OwnershipReader : MonoBehaviour
{
    private const string BalanceABI = "function balanceOf(address owner) view returns (uint256)";
    private const string OwnerABI = "function ownerOf(uint256 tokenId) view returns (address)";
    [SerializeField] private ReownWalletConnector walletConnector;
    [SerializeField] private bool scanWhenWalletConnects = true;
    [Header("V2 shared approved source")]
    [SerializeField] private ApprovedPDTCollection approvedSource;
    [Header("Legacy scene source")]
    [SerializeField] private string chain = "eip155:80002";
    [SerializeField] private string contractAddress = "0x021Ae9C7E520B1EdFdE488A7Df3EEd9BfC5786F3";
    [SerializeField] private MonoBehaviour tokenDiscoveryServiceSource;

    private readonly List<VerifiedNFT> verifiedTokens = new List<VerifiedNFT>();
    private readonly HashSet<TokenReference> knownCandidates = new HashSet<TokenReference>();
    private Coroutine scan;
    private int generation;
    private PDTReadContext context;
    private string sourceChain, sourceCollection;
    public IReadOnlyList<VerifiedNFT> VerifiedTokens => verifiedTokens;
    public bool IsScanning { get; private set; }
    public bool HasUnavailableResults { get; private set; }
    public int ScanGeneration => generation;
    public bool IsVerificationContextCurrent => context != null && context.IsCurrent &&
        string.Equals(sourceChain, ApprovedChain, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(sourceCollection, ApprovedCollection, StringComparison.OrdinalIgnoreCase);
    private string ApprovedChain => approvedSource != null ? approvedSource.Chain : chain?.Trim();
    private string ApprovedCollection => approvedSource != null ? approvedSource.ProxyAddress : contractAddress?.Trim();
    public event Action<VerifiedNFT> TokenVerified;
    public event Action OwnershipScanStarted;
    public event Action<IReadOnlyList<VerifiedNFT>> OwnershipScanCompleted;
    public event Action<string> OwnershipScanFailed;
    public event Action OwnershipCleared;

    private void OnEnable() { if (walletConnector != null) walletConnector.WalletContextChanged += HandleContextChanged; }
    private void Start() { if (scanWhenWalletConnects && walletConnector != null && walletConnector.IsConnected) RefreshOwnership(); }
    private void OnDisable()
    {
        if (walletConnector != null) walletConnector.WalletContextChanged -= HandleContextChanged;
        Invalidate();
    }
    private void HandleContextChanged()
    {
        knownCandidates.Clear();
        Invalidate();
        if (scanWhenWalletConnects && walletConnector.IsConnected) RefreshOwnership();
    }

    // New protected actions must request a refresh, never trust an old registry.
    public void RefreshOwnership()
    {
        Invalidate();
        HasUnavailableResults = false;
        if (walletConnector == null || !walletConnector.IsConnected) { Fail("Connect a wallet before verification."); return; }
        sourceChain = ApprovedChain;
        sourceCollection = ApprovedCollection;
        if (string.IsNullOrWhiteSpace(sourceChain) || !PDTVerificationPolicy.IsAddress(sourceCollection))
        { Fail("The approved PDT proxy has not been configured."); return; }
        if (!(tokenDiscoveryServiceSource is ITokenDiscoveryService discovery))
        { Fail("A token discovery service is required."); return; }
        context = new PDTReadContext(walletConnector, sourceChain);
        IsScanning = true;
        OwnershipScanStarted?.Invoke();
        scan = StartCoroutine(Scan(discovery, generation));
    }

    private IEnumerator Scan(ITokenDiscoveryService discovery, int currentGeneration)
    {
        // Reown finishes dispatching account/network events before the first read.
        yield return null;
        if (!Current(currentGeneration)) { Fail("Verification unavailable: select the approved PDT network."); yield break; }
        IReadOnlyList<TokenReference> discovered = null;
        string discoveryError = null;
        yield return discovery.DiscoverOwnedTokens(context.Address, sourceChain, sourceCollection,
            tokens => discovered = tokens, error => discoveryError = error);
        if (!Current(currentGeneration)) yield break;
        HasUnavailableResults = discoveryError != null || discovered == null;
        if (HasUnavailableResults) Debug.LogWarning("NFT discovery unavailable; only known identities can be reverified.");
        List<TokenReference> candidates = BuildCandidates(discovered);
        foreach (TokenReference token in candidates)
        {
            if (!Current(currentGeneration)) yield break;
            PDTVerificationPolicy.TryTokenId(token.TokenID, out BigInteger id);
            var owner = new PDTReadResult<string>();
            yield return PDTChainRead.Run(() => AppKit.Evm.ReadContractAsync<string>(
                sourceCollection, OwnerABI, "ownerOf", new object[] { id }), context, owner, id);
            if (!Current(currentGeneration)) yield break;
            if (owner.Status == PDTReadStatus.Nonexistent)
            { knownCandidates.Remove(token); Debug.Log($"Rejected nonexistent PDT token {id}."); continue; }
            if (owner.Status != PDTReadStatus.Success || !PDTVerificationPolicy.IsAddress(owner.Value))
            { HasUnavailableResults = true; Debug.LogWarning($"Ownership verification unavailable for token {id}."); continue; }
            if (!string.Equals(owner.Value, context.Address, StringComparison.OrdinalIgnoreCase))
            { knownCandidates.Remove(token); Debug.Log($"Rejected PDT token {id}: wallet is not its current owner."); continue; }
            var verified = new VerifiedNFT(token);
            verifiedTokens.Add(verified);
            TokenVerified?.Invoke(verified);
            Debug.Log($"Verified indexed PDT token {id} through ownerOf.");
        }
        // Advisory only; never discard individually proven tokens.
        var balance = new PDTReadResult<BigInteger>();
        yield return PDTChainRead.Run(() => AppKit.Evm.ReadContractAsync<BigInteger>(
            sourceCollection, BalanceABI, "balanceOf", new object[] { context.Address }), context, balance);
        if (!Current(currentGeneration)) yield break;
        if (balance.Status != PDTReadStatus.Success || balance.Value != verifiedTokens.Count)
        {
            HasUnavailableResults = true;
            Debug.LogWarning("Discovery completeness is uncertain; individually verified tokens remain usable.");
        }
        IsScanning = false;
        scan = null;
        OwnershipScanCompleted?.Invoke(new List<VerifiedNFT>(verifiedTokens));
        Debug.Log($"NFT scan completed with {verifiedTokens.Count} verified token(s).");
    }

    private List<TokenReference> BuildCandidates(IReadOnlyList<TokenReference> discovered)
    {
        // Cache identity only; ownership and entitlement are always read again.
        if (discovered != null)
            foreach (TokenReference token in discovered)
                if (token != null && string.Equals(token.Chain, sourceChain, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(token.Collection, sourceCollection, StringComparison.OrdinalIgnoreCase) &&
                    PDTVerificationPolicy.TryTokenId(token.TokenID, out BigInteger id))
                    knownCandidates.Add(new TokenReference(sourceChain.ToLowerInvariant(), sourceCollection.ToLowerInvariant(), id.ToString(CultureInfo.InvariantCulture)));
        var result = new List<TokenReference>();
        foreach (TokenReference token in knownCandidates)
            if (string.Equals(token.Chain, sourceChain, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(token.Collection, sourceCollection, StringComparison.OrdinalIgnoreCase)) result.Add(token);
        return result;
    }

    private bool Current(int expected)
    {
        if (expected != generation) return false;
        if (IsVerificationContextCurrent) return true;
        Invalidate();
        Fail("Verification unavailable: wallet or approved network context changed.");
        return false;
    }
    private void Invalidate()
    {
        generation++;
        if (scan != null) StopCoroutine(scan);
        scan = null;
        IsScanning = false;
        context = null;
        verifiedTokens.Clear();
        // Cancel downstream work even if the old ownership list was empty.
        OwnershipCleared?.Invoke();
    }
    private void Fail(string message)
    {
        IsScanning = false;
        HasUnavailableResults = true;
        OwnershipScanFailed?.Invoke(message);
        Debug.LogWarning(message);
    }
}
