using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;

[Serializable]
public sealed class VerifiedNFT
{
    public string tokenID;
    public string metadataURI;

    public VerifiedNFT(BigInteger tokenID, string metadataURI)
    {
        this.tokenID = tokenID.ToString();
        this.metadataURI = metadataURI;
    }
}

public class ERC721OwnershipReader : MonoBehaviour
{
    private const string BalanceOfABI =
        "function balanceOf(address owner) view returns (uint256)";
    private const string OwnerOfABI =
        "function ownerOf(uint256 tokenId) view returns (address)";
    private const string TokenURIABI =
        "function tokenURI(uint256 tokenId) view returns (string)";

    [Header("Wallet")]
    [SerializeField] private ReownWalletConnector walletConnector;
    [SerializeField] private bool scanWhenWalletConnects = true;

    [Header("Polygon Amoy ERC-721")]
    [SerializeField] private string contractAddress =
        "0x021Ae9C7E520B1EdFdE488A7Df3EEd9BfC5786F3";

    [Header("Token discovery")]
    [Tooltip("DigitalTwins.sol starts minting sequential token IDs at 0.")]
    [SerializeField] private int firstTokenID;
    [Tooltip("Safety limit for this non-enumerable prototype contract.")]
    [SerializeField] private int maximumTokenIDToScan = 1000;

    private readonly List<VerifiedNFT> verifiedTokens =
        new List<VerifiedNFT>();
    private int scanGeneration;

    public IReadOnlyList<VerifiedNFT> VerifiedTokens => verifiedTokens;
    public bool IsScanning { get; private set; }

    public event Action<VerifiedNFT> TokenVerified;
    public event Action<IReadOnlyList<VerifiedNFT>> OwnershipScanCompleted;
    public event Action<string> OwnershipScanFailed;
    public event Action OwnershipCleared;

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
        IsScanning = false;
        ClearVerifiedTokens();

        if (walletConnector == null)
        {
            return;
        }

        walletConnector.WalletConnected -= HandleWalletConnected;
        walletConnector.WalletDisconnected -= HandleWalletDisconnected;
    }

    public async void RefreshOwnership()
    {
        if (walletConnector == null)
        {
            ReportFailure("ERC721OwnershipReader has no wallet connector assigned.");
            return;
        }

        if (!walletConnector.IsConnected)
        {
            ReportFailure("Connect a wallet before scanning NFT ownership.");
            return;
        }

        int generation = ++scanGeneration;
        await ScanOwnershipAsync(walletConnector.ConnectedAddress, generation);
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
        IsScanning = false;
        ClearVerifiedTokens();
    }

    private async Task ScanOwnershipAsync(
        string walletAddress,
        int generation
    )
    {
        if (!IsValidEVMAddress(contractAddress))
        {
            ReportFailure("The configured NFT contract address is invalid.");
            return;
        }

        if (!IsValidEVMAddress(walletAddress))
        {
            ReportFailure("Reown returned an invalid wallet address.");
            return;
        }

        if (firstTokenID < 0 || maximumTokenIDToScan < firstTokenID)
        {
            ReportFailure("The token discovery range is invalid.");
            return;
        }

        IsScanning = true;
        ClearVerifiedTokens();

        try
        {
            BigInteger expectedBalance =
                await AppKit.Evm.ReadContractAsync<BigInteger>(
                    contractAddress,
                    BalanceOfABI,
                    "balanceOf",
                    new object[] { walletAddress }
                );

            if (generation != scanGeneration)
            {
                return;
            }

            Debug.Log(
                $"Wallet owns {expectedBalance} NFT(s) from the PDT collection."
            );

            if (expectedBalance == BigInteger.Zero)
            {
                CompleteScan(generation);
                return;
            }

            for (
                int tokenID = firstTokenID;
                tokenID <= maximumTokenIDToScan;
                tokenID++
            )
            {
                if (
                    generation != scanGeneration ||
                    verifiedTokens.Count >= expectedBalance
                )
                {
                    break;
                }

                BigInteger blockchainTokenID = new BigInteger(tokenID);
                string ownerAddress;

                try
                {
                    ownerAddress = await AppKit.Evm.ReadContractAsync<string>(
                        contractAddress,
                        OwnerOfABI,
                        "ownerOf",
                        new object[] { blockchainTokenID }
                    );
                }
                catch
                {
                    // ownerOf reverts for token IDs that have not been minted.
                    continue;
                }

                if (
                    !string.Equals(
                        ownerAddress,
                        walletAddress,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                string metadataURI =
                    await AppKit.Evm.ReadContractAsync<string>(
                        contractAddress,
                        TokenURIABI,
                        "tokenURI",
                        new object[] { blockchainTokenID }
                    );

                if (generation != scanGeneration)
                {
                    return;
                }

                VerifiedNFT verifiedNFT = new VerifiedNFT(
                    blockchainTokenID,
                    metadataURI
                );

                verifiedTokens.Add(verifiedNFT);
                TokenVerified?.Invoke(verifiedNFT);

                Debug.Log(
                    $"Verified owned NFT token {verifiedNFT.tokenID}: " +
                    verifiedNFT.metadataURI
                );
            }

            if (verifiedTokens.Count < expectedBalance)
            {
                ReportFailure(
                    $"The wallet balance is {expectedBalance}, but only " +
                    $"{verifiedTokens.Count} owned token(s) were found through " +
                    $"token ID {maximumTokenIDToScan}. Increase the scan limit."
                );
                return;
            }

            CompleteScan(generation);
        }
        catch (Exception exception)
        {
            if (generation == scanGeneration)
            {
                ReportFailure(
                    $"NFT ownership scan failed: {exception.Message}"
                );
            }
        }
        finally
        {
            if (generation == scanGeneration)
            {
                IsScanning = false;
            }
        }
    }

    private void CompleteScan(int generation)
    {
        if (generation != scanGeneration)
        {
            return;
        }

        IsScanning = false;
        OwnershipScanCompleted?.Invoke(verifiedTokens);

        Debug.Log(
            $"NFT ownership scan completed with {verifiedTokens.Count} " +
            "verified token(s)."
        );
    }

    private void ReportFailure(string message)
    {
        IsScanning = false;
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
