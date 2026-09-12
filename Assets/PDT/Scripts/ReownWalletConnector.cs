using System;
using Reown.AppKit.Unity;
using UnityEngine;

[DisallowMultipleComponent]
public class ReownWalletConnector : MonoBehaviour
{
    [Header("Reown project")]
    [SerializeField] private string projectID;
    [SerializeField] private string applicationName = "PDT Anniversary Car";
    [SerializeField] private string applicationDescription =
        "Connect a wallet to unlock verified digital twin vehicles.";
    [SerializeField] private string applicationURL =
        "https://github.com/jerom-26/PDT-Anniversary-Car";
    [SerializeField] private string applicationIconURL =
        "https://raw.githubusercontent.com/reown-com/reown-dotnet/develop/media/appkit-icon.png";

    [Header("Connection")]
    [SerializeField] private bool resumeSessionOnStart = true;
    [SerializeField] private bool openModalWhenNoSession;

    public bool IsInitialized { get; private set; }
    public bool IsConnected =>
        PDTVerificationPolicy.IsAddress(ConnectedAddress);
    public bool IsDisconnecting { get; private set; }
    public string ConnectedAddress { get; private set; }
    public string ConnectedChain { get; private set; }
    public int ContextVersion { get; private set; }
    public event Action WalletContextChanged;

    public event Action WalletInitialized;
    public event Action<string> WalletConnected;
    public event Action WalletDisconnected;
    public event Action WalletDisconnectCompleted;
    public event Action<string> WalletError;

    private bool eventsSubscribed;
    private bool isDestroyed;

    private async void Start()
    {
        if (string.IsNullOrWhiteSpace(projectID))
        {
            ReportWalletError(
                "ReownWalletConnector requires a Reown Project ID in the Inspector."
            );
            return;
        }

        try
        {
            if (!AppKit.IsInitialized)
            {
                await AppKit.InitializeAsync(CreateConfig());
            }

            if (isDestroyed)
            {
                return;
            }

            IsInitialized = true;
            SubscribeToAppKitEvents();

            bool sessionAvailable = AppKit.IsAccountConnected;

            if (!sessionAvailable && resumeSessionOnStart)
            {
                sessionAvailable =
                    await AppKit.ConnectorController.TryResumeSessionAsync();
            }

            if (isDestroyed)
            {
                return;
            }

            if (sessionAvailable)
            {
                if (
                    !TrySetConnectedAddress(
                        AppKit.Account.Address,
                        AppKit.Account.ChainId
                    )
                )
                {
                    return;
                }
            }
            else if (openModalWhenNoSession)
            {
                OpenWalletModal();
            }

            Debug.Log("Reown AppKit initialized for Polygon Amoy.");
            WalletInitialized?.Invoke();
        }
        catch (Exception exception)
        {
            if (isDestroyed)
            {
                return;
            }

            IsInitialized = false;
            ClearConnectionState();
            ReportWalletError(
                $"Reown AppKit initialization failed: {exception.Message}"
            );
        }
    }

    private void OnDestroy()
    {
        isDestroyed = true;
        UnsubscribeFromAppKitEvents();
    }

    public void OpenWalletModal()
    {
        if (!AppKit.IsInitialized)
        {
            ReportWalletError("Reown AppKit is not initialized yet.");
            return;
        }

        try
        {
            AppKit.OpenModal();
        }
        catch (Exception exception)
        {
            ReportWalletError(
                $"Wallet selection could not be opened: {exception.Message}"
            );
        }
    }

    public void OpenAccountModal()
    {
        if (!AppKit.IsInitialized || !AppKit.IsAccountConnected)
        {
            ReportWalletError(
                "Connect a wallet before opening the account view."
            );
            return;
        }

        try
        {
            AppKit.OpenModal(ViewType.Account);
        }
        catch (Exception exception)
        {
            ReportWalletError(
                $"Wallet account view could not be opened: {exception.Message}"
            );
        }
    }

    public async void DisconnectWallet()
    {
        if (IsDisconnecting)
        {
            return;
        }

        if (!AppKit.IsInitialized || !AppKit.IsAccountConnected)
        {
            ClearConnectionState();
            ReportWalletError("There is no connected wallet to disconnect.");
            return;
        }

        IsDisconnecting = true;
        // Revoke local authorization immediately. A slow or failed remote
        // disconnect must never leave old wallet entitlements active.
        ClearConnectionState();

        try
        {
            await AppKit.DisconnectAsync();
        }
        catch (Exception exception)
        {
            ReportWalletError(
                $"Wallet disconnect failed: {exception.Message}"
            );
        }
        finally
        {
            IsDisconnecting = false;

            if (!isDestroyed)
            {
                WalletDisconnectCompleted?.Invoke();
            }
        }
    }

    private AppKitConfig CreateConfig()
    {
        return new AppKitConfig(
            projectID.Trim(),
            new Metadata(
                applicationName,
                applicationDescription,
                applicationURL,
                applicationIconURL
            )
        )
        {
            enableEmail = false,
            enableOnramp = false,
            socials = Array.Empty<SocialLogin>(),
            supportedChains = new[] { CreatePolygonAmoyChain() }
        };
    }

    private static Chain CreatePolygonAmoyChain()
    {
        return new Chain(
            ChainConstants.Namespaces.Evm,
            "80002",
            "Polygon Amoy",
            new Currency("Polygon Ecosystem Token", "POL", 18),
            new BlockExplorer(
                "PolygonScan Amoy",
                "https://amoy.polygonscan.com"
            ),
            "https://polygon-amoy.drpc.org",
            true,
            ChainConstants.Chains.Polygon.ImageUrl
        );
    }

    private void SubscribeToAppKitEvents()
    {
        if (eventsSubscribed)
        {
            return;
        }

        if (AppKit.ConnectorController == null)
        {
            throw new InvalidOperationException(
                "Reown connector controller is unavailable after initialization."
            );
        }

        AppKit.AccountConnected += HandleAccountConnected;
        AppKit.AccountChanged += HandleAccountChanged;
        AppKit.AccountDisconnected += HandleAccountDisconnected;
        AppKit.ConnectorController.ChainChanged += HandleChainChanged;
        eventsSubscribed = true;
    }

    private void UnsubscribeFromAppKitEvents()
    {
        if (!eventsSubscribed)
        {
            return;
        }

        if (AppKit.ConnectorController != null)
        {
            AppKit.AccountConnected -= HandleAccountConnected;
            AppKit.AccountChanged -= HandleAccountChanged;
            AppKit.AccountDisconnected -= HandleAccountDisconnected;
            AppKit.ConnectorController.ChainChanged -= HandleChainChanged;
        }

        eventsSubscribed = false;
    }

    private void HandleAccountConnected(
        object sender,
        Connector.AccountConnectedEventArgs eventArgs
    )
    {
        if (eventArgs == null)
        {
            RejectInvalidWalletContext(
                "Reown returned an invalid wallet connection event."
            );
            return;
        }

        TrySetConnectedAddress(
            eventArgs.Account.Address,
            eventArgs.Account.ChainId
        );
    }

    private void HandleAccountChanged(
        object sender,
        Connector.AccountChangedEventArgs eventArgs
    )
    {
        if (eventArgs == null)
        {
            RejectInvalidWalletContext(
                "Reown returned an invalid wallet account-change event."
            );
            return;
        }

        TrySetConnectedAddress(
            eventArgs.Account.Address,
            eventArgs.Account.ChainId
        );
    }

    private void HandleAccountDisconnected(
        object sender,
        Connector.AccountDisconnectedEventArgs eventArgs
    )
    {
        ClearConnectionState();
    }

    private void HandleChainChanged(object sender, Connector.ChainChangedEventArgs eventArgs)
    {
        if (!IsConnected)
        {
            return;
        }

        string chain = eventArgs?.ChainId?.Trim();

        if (string.IsNullOrWhiteSpace(chain))
        {
            RejectInvalidWalletContext(
                "Reown returned an empty wallet network."
            );
            return;
        }

        if (
            string.Equals(
                ConnectedChain,
                chain,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return;
        }

        ConnectedChain = chain;
        ContextVersion++;
        WalletContextChanged?.Invoke();
    }

    private bool TrySetConnectedAddress(string address, string chain)
    {
        address = address?.Trim();
        chain = chain?.Trim();

        if (!PDTVerificationPolicy.IsAddress(address))
        {
            RejectInvalidWalletContext(
                "Reown returned an invalid EVM wallet address."
            );
            return false;
        }

        if (string.IsNullOrWhiteSpace(chain))
        {
            RejectInvalidWalletContext(
                "Reown returned an empty wallet network."
            );
            return false;
        }

        if (
            string.Equals(
                ConnectedAddress,
                address,
                StringComparison.OrdinalIgnoreCase
            ) && string.Equals(ConnectedChain, chain, StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        ConnectedAddress = address;
        ConnectedChain = chain;
        ContextVersion++;
        WalletConnected?.Invoke(ConnectedAddress);
        WalletContextChanged?.Invoke();
        Debug.Log($"Wallet connected: {ConnectedAddress}");
        return true;
    }

    private void RejectInvalidWalletContext(string message)
    {
        ClearConnectionState();
        ReportWalletError(message);
    }

    private void ClearConnectionState()
    {
        bool wasConnected = IsConnected;
        bool hadContext =
            wasConnected || !string.IsNullOrWhiteSpace(ConnectedChain);

        ConnectedAddress = null;
        ConnectedChain = null;

        if (!hadContext)
        {
            return;
        }

        ContextVersion++;
        WalletContextChanged?.Invoke();

        if (wasConnected)
        {
            WalletDisconnected?.Invoke();
            Debug.Log("Wallet disconnected.");
        }
    }

    private void ReportWalletError(string message)
    {
        WalletError?.Invoke(message);
        Debug.LogError(message);
    }
}
