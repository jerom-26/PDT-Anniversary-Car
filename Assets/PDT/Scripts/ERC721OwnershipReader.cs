using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;

[Serializable]
public sealed class VerifiedNFT
{
    public TokenReference tokenReference;
    public string tokenID;

    public VerifiedNFT(TokenReference tokenReference)
    {
        this.tokenReference = tokenReference;
        tokenID = tokenReference.TokenID;
    }
}

public class ERC721OwnershipReader : MonoBehaviour
{
    private const string BalanceOfABI =
        "function balanceOf(address owner) view returns (uint256)";
    private const string OwnerOfABI =
        "function ownerOf(uint256 tokenId) view returns (address)";
    [Header("Wallet")]
    [SerializeField] private ReownWalletConnector walletConnector;
    [SerializeField] private bool scanWhenWalletConnects = true;

    [Header("Approved PDT collection")]
    [Tooltip("CAIP-2 chain identifier for Polygon Amoy.")]
    [SerializeField] private string chain = "eip155:80002";
    [SerializeField] private string contractAddress =
        "0x021Ae9C7E520B1EdFdE488A7Df3EEd9BfC5786F3";

    [Header("Indexed token discovery")]
    [Tooltip("Must implement ITokenDiscoveryService.")]
    [SerializeField] private MonoBehaviour tokenDiscoveryServiceSource;

    private readonly List<VerifiedNFT> verifiedTokens =
        new List<VerifiedNFT>();
    private ITokenDiscoveryService tokenDiscoveryService;
    private Coroutine ownershipScanCoroutine;
    private int scanGeneration;

    public IReadOnlyList<VerifiedNFT> VerifiedTokens => verifiedTokens;
    public bool IsScanning { get; private set; }

    public event Action<VerifiedNFT> TokenVerified;
    public event Action<IReadOnlyList<VerifiedNFT>> OwnershipScanCompleted;
    public event Action<string> OwnershipScanFailed;
    public event Action OwnershipCleared;

    private void Awake()
    {
        TryResolveTokenDiscoveryService(out _);
    }

    private void OnEnable()
    {
        if (walletConnector == null)
        {
            return;
        }

        walletConnector.WalletConnected += HandleWalletConnected;
        walletConnector.WalletDisconnected += HandleWalletDisconnected;
    }

    private void Start()
    {
        if (!TryResolveTokenDiscoveryService(out string discoveryError))
        {
            Debug.LogError(discoveryError);
            return;
        }

        if (
            scanWhenWalletConnects &&
            walletConnector != null &&
            walletConnector.IsConnected
        )
        {
            RefreshOwnership();
        }
    }

    private void OnDisable()
    {
        scanGeneration++;
        StopOwnershipScan();
        ClearVerifiedTokens();

        if (walletConnector == null)
        {
            return;
        }

        walletConnector.WalletConnected -= HandleWalletConnected;
        walletConnector.WalletDisconnected -= HandleWalletDisconnected;
    }

    public void RefreshOwnership()
    {
        if (walletConnector == null)
        {
            ReportFailure(
                "ERC721OwnershipReader has no wallet connector assigned."
            );
            return;
        }

        if (!walletConnector.IsConnected)
        {
            ReportFailure("Connect a wallet before scanning NFT ownership.");
            return;
        }

        if (!TryResolveTokenDiscoveryService(out string discoveryError))
        {
            ReportFailure(discoveryError);
            return;
        }

        StopOwnershipScan();
        int generation = ++scanGeneration;
        ownershipScanCoroutine = StartCoroutine(
            ScanOwnership(
                walletConnector.ConnectedAddress,
                tokenDiscoveryService,
                generation
            )
        );
    }

    private void HandleWalletConnected(string walletAddress)
    {
        if (!scanWhenWalletConnects)
        {
            return;
        }

        RefreshOwnership();
    }

    private void HandleWalletDisconnected()
    {
        scanGeneration++;
        StopOwnershipScan();
        ClearVerifiedTokens();
    }

    private IEnumerator ScanOwnership(
        string walletAddress,
        ITokenDiscoveryService discoveryService,
        int generation
    )
    {
        if (string.IsNullOrWhiteSpace(chain))
        {
            ReportFailureForGeneration(
                "The approved PDT chain is not configured.",
                generation
            );
            yield break;
        }

        if (!IsValidEVMAddress(contractAddress))
        {
            ReportFailureForGeneration(
                "The configured NFT contract address is invalid.",
                generation
            );
            yield break;
        }

        if (!IsValidEVMAddress(walletAddress))
        {
            ReportFailureForGeneration(
                "Reown returned an invalid wallet address.",
                generation
            );
            yield break;
        }

        IsScanning = true;
        ClearVerifiedTokens();

        if (
            !TryStartTask(
                () => AppKit.Evm.ReadContractAsync<BigInteger>(
                    contractAddress,
                    BalanceOfABI,
                    "balanceOf",
                    new object[] { walletAddress }
                ),
                out Task<BigInteger> balanceTask,
                out string balanceStartError
            )
        )
        {
            ReportFailureForGeneration(
                "NFT balance check failed: " + balanceStartError,
                generation
            );
            yield break;
        }

        while (!balanceTask.IsCompleted)
        {
            if (generation != scanGeneration)
            {
                yield break;
            }

            yield return null;
        }

        if (generation != scanGeneration)
        {
            yield break;
        }

        if (
            !TryGetTaskResult(
                balanceTask,
                out BigInteger expectedBalance,
                out string balanceError
            )
        )
        {
            ReportFailureForGeneration(
                "NFT balance check failed: " + balanceError,
                generation
            );
            yield break;
        }

        Debug.Log(
            $"Wallet owns {expectedBalance} NFT(s) from the PDT collection."
        );

        if (expectedBalance == BigInteger.Zero)
        {
            CompleteScan(generation);
            yield break;
        }

        IReadOnlyList<TokenReference> discoveredTokens = null;
        string indexedDiscoveryError = null;

        yield return discoveryService.DiscoverOwnedTokens(
            walletAddress,
            chain,
            contractAddress,
            tokens => discoveredTokens = tokens,
            error => indexedDiscoveryError = error
        );

        if (generation != scanGeneration)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(indexedDiscoveryError))
        {
            ReportFailureForGeneration(
                "Indexed NFT discovery failed: " + indexedDiscoveryError,
                generation
            );
            yield break;
        }

        if (discoveredTokens == null)
        {
            ReportFailureForGeneration(
                "Indexed NFT discovery returned no result.",
                generation
            );
            yield break;
        }

        List<TokenReference> approvedCandidates =
            BuildApprovedCandidateList(discoveredTokens);

        foreach (TokenReference candidate in approvedCandidates)
        {
            if (generation != scanGeneration)
            {
                yield break;
            }

            if (
                !BigInteger.TryParse(
                    candidate.TokenID,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out BigInteger blockchainTokenID
                ) ||
                blockchainTokenID < BigInteger.Zero
            )
            {
                Debug.LogWarning(
                    $"Ignored invalid indexed token ID '{candidate.TokenID}'."
                );
                continue;
            }

            if (
                !TryStartTask(
                    () => AppKit.Evm.ReadContractAsync<string>(
                        contractAddress,
                        OwnerOfABI,
                        "ownerOf",
                        new object[] { blockchainTokenID }
                    ),
                    out Task<string> ownerTask,
                    out string ownerStartError
                )
            )
            {
                ReportFailureForGeneration(
                    $"On-chain ownership verification failed for token " +
                    $"{candidate.TokenID}: {ownerStartError}",
                    generation
                );
                yield break;
            }

            while (!ownerTask.IsCompleted)
            {
                if (generation != scanGeneration)
                {
                    yield break;
                }

                yield return null;
            }

            if (
                !TryGetTaskResult(
                    ownerTask,
                    out string ownerAddress,
                    out string ownerError
                )
            )
            {
                ReportFailureForGeneration(
                    $"On-chain ownership verification failed for token " +
                    $"{candidate.TokenID}: {ownerError}",
                    generation
                );
                yield break;
            }

            if (
                !string.Equals(
                    ownerAddress,
                    walletAddress,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Debug.LogWarning(
                    $"Ignored stale indexed token {candidate.TokenID}; " +
                    "ownerOf does not match the connected wallet."
                );
                continue;
            }

            VerifiedNFT verifiedNFT = new VerifiedNFT(candidate);

            verifiedTokens.Add(verifiedNFT);
            TokenVerified?.Invoke(verifiedNFT);

            Debug.Log(
                $"Verified indexed PDT NFT token {verifiedNFT.tokenID} " +
                "through ownerOf."
            );
        }

        if (new BigInteger(verifiedTokens.Count) != expectedBalance)
        {
            ReportFailureForGeneration(
                $"The official contract reports {expectedBalance} owned " +
                $"token(s), but indexed discovery produced " +
                $"{verifiedTokens.Count} verified token(s). The indexer may " +
                "still be synchronizing.",
                generation
            );
            yield break;
        }

        CompleteScan(generation);
    }

    private List<TokenReference> BuildApprovedCandidateList(
        IReadOnlyList<TokenReference> discoveredTokens
    )
    {
        string approvedChain = chain.Trim().ToLowerInvariant();
        string approvedCollection = contractAddress.Trim().ToLowerInvariant();
        List<TokenReference> approvedCandidates =
            new List<TokenReference>();
        HashSet<TokenReference> uniqueCandidates =
            new HashSet<TokenReference>();

        foreach (TokenReference discoveredToken in discoveredTokens)
        {
            if (discoveredToken == null)
            {
                continue;
            }

            if (
                !string.Equals(
                    discoveredToken.Chain,
                    approvedChain,
                    StringComparison.OrdinalIgnoreCase
                ) ||
                !string.Equals(
                    discoveredToken.Collection,
                    approvedCollection,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Debug.LogWarning(
                    "Ignored an indexed token outside the approved PDT " +
                    "chain or collection."
                );
                continue;
            }

            if (
                !BigInteger.TryParse(
                    discoveredToken.TokenID,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out BigInteger tokenID
                ) ||
                tokenID < BigInteger.Zero
            )
            {
                Debug.LogWarning(
                    $"Ignored invalid indexed token ID " +
                    $"'{discoveredToken.TokenID}'."
                );
                continue;
            }

            TokenReference normalizedReference = new TokenReference(
                approvedChain,
                approvedCollection,
                tokenID.ToString(CultureInfo.InvariantCulture)
            );

            if (uniqueCandidates.Add(normalizedReference))
            {
                approvedCandidates.Add(normalizedReference);
            }
        }

        return approvedCandidates;
    }

    private bool TryResolveTokenDiscoveryService(out string errorMessage)
    {
        tokenDiscoveryService =
            tokenDiscoveryServiceSource as ITokenDiscoveryService;

        if (tokenDiscoveryService != null)
        {
            errorMessage = null;
            return true;
        }

        errorMessage =
            "ERC721OwnershipReader requires a component that implements " +
            "ITokenDiscoveryService.";
        return false;
    }

    private void StopOwnershipScan()
    {
        if (ownershipScanCoroutine != null)
        {
            StopCoroutine(ownershipScanCoroutine);
            ownershipScanCoroutine = null;
        }

        IsScanning = false;
    }

    private void CompleteScan(int generation)
    {
        if (generation != scanGeneration)
        {
            return;
        }

        ownershipScanCoroutine = null;
        IsScanning = false;
        OwnershipScanCompleted?.Invoke(verifiedTokens);

        Debug.Log(
            $"NFT ownership scan completed with {verifiedTokens.Count} " +
            "verified token(s)."
        );
    }

    private void ReportFailureForGeneration(
        string message,
        int generation
    )
    {
        if (generation == scanGeneration)
        {
            ReportFailure(message);
        }
    }

    private void ReportFailure(string message)
    {
        ownershipScanCoroutine = null;
        IsScanning = false;
        ClearVerifiedTokens();
        OwnershipScanFailed?.Invoke(message);
        Debug.LogError(message);
    }

    private void ClearVerifiedTokens()
    {
        if (verifiedTokens.Count == 0)
        {
            return;
        }

        verifiedTokens.Clear();
        OwnershipCleared?.Invoke();
    }

    private static bool TryGetTaskResult<T>(
        Task<T> task,
        out T result,
        out string errorMessage
    )
    {
        if (task.IsCanceled)
        {
            result = default;
            errorMessage = "The blockchain request was cancelled.";
            return false;
        }

        if (task.IsFaulted)
        {
            result = default;
            errorMessage = task.Exception
                ?.GetBaseException()
                .Message ?? "The blockchain request failed.";
            return false;
        }

        result = task.Result;
        errorMessage = null;
        return true;
    }

    private static bool TryStartTask<T>(
        Func<Task<T>> taskFactory,
        out Task<T> task,
        out string errorMessage
    )
    {
        try
        {
            task = taskFactory();
            errorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            task = null;
            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool IsValidEVMAddress(string address)
    {
        if (
            string.IsNullOrWhiteSpace(address) ||
            address.Length != 42 ||
            !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        for (int index = 2; index < address.Length; index++)
        {
            char character = address[index];
            bool isHexadecimal =
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F');

            if (!isHexadecimal)
            {
                return false;
            }
        }

        return true;
    }
}
