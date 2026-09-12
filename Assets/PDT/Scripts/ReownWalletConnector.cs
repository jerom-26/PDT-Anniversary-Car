using System;
using Reown.AppKit.Unity;
using UnityEngine;

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
    public bool IsConnected => !string.IsNullOrWhiteSpace(ConnectedAddress);
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

    private async void Start()
    {
        if (string.IsNullOrWhiteSpace(projectID))
        {
            Debug.LogError(
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

            IsInitialized = true;
            SubscribeToAppKitEvents();

            bool sessionAvailable = AppKit.IsAccountConnected;

            if (!sessionAvailable && resumeSessionOnStart)
            {
                sessionAvailable =
                    await AppKit.ConnectorController.TryResumeSessionAsync();
            }

            if (sessionAvailable)
            {
                SetConnectedAddress(
                    AppKit.Account.Address,
                    AppKit.Account.ChainId
                );
            }
            else
            {
                ClearConnectedContext(false);

                if (openModalWhenNoSession)
                {
                    OpenWalletModal();
                }
            }

            Debug.Log("Reown AppKit initialized for Polygon Amoy.");
            WalletInitialized?.Invoke();
        }
        catch (Exception exception)
        {
            IsInitialized = false;
            ClearConnectedContext(false);
            ReportWalletError(
                $"Reown AppKit initialization failed: {exception.Message}"
            );
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromAppKitEvents();
    }

    public void OpenWalletModal()
    {
        if (!AppKit.IsInitialized)
        {
            ReportWalletError("Reown AppKit is not initialized yet.");
            return;
        }

        AppKit.OpenModal();
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

        AppKit.OpenModal(ViewType.Account);
    }

    public async void DisconnectWallet()
    {
        if (IsDisconnecting)
        {
            return;
        }

        if (!AppKit.IsInitialized || !AppKit.IsAccountConnected)
        {
            if (IsConnected)
            {
                ClearConnectedContext(true);
            }
            else
            {
                ReportWalletError("There is no connected wallet to disconnect.");
            }
            return;
        }

        IsDisconnecting = true;

        try
        {
            await AppKit.DisconnectAsync();

            // Do not depend solely on an SDK event to revoke local access.
            if (!AppKit.IsAccountConnected && IsConnected)
            {
                ClearConnectedContext(true);
            }
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
            WalletDisconnectCompleted?.Invoke();
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

        AppKit.AccountConnected += HandleAccountConnected;
        AppKit.AccountChanged += HandleAccountChanged;
        AppKit.AccountDisconnected += HandleAccountDisconnected;
        AppKit.ConnectorController.ChainChanged += HandleChainChanged;
        eventsSubscribed = true;
    }

    private void UnsubscribeFromAppKitEvents()
    {
        if (!eventsSubscribed || !AppKit.IsInitialized)
        {
            return;
        }

        AppKit.AccountConnected -= HandleAccountConnected;
        AppKit.AccountChanged -= HandleAccountChanged;
        AppKit.AccountDisconnected -= HandleAccountDisconnected;
        AppKit.ConnectorController.ChainChanged -= HandleChainChanged;
        eventsSubscribed = false;
    }

    private void HandleAccountConnected(
        object sender,
        Connector.AccountConnectedEventArgs eventArgs
    )
    {
        SetConnectedAddress(eventArgs.Account.Address, eventArgs.Account.ChainId);
    }

    private void HandleAccountChanged(
        object sender,
        Connector.AccountChangedEventArgs eventArgs
    )
    {
        SetConnectedAddress(eventArgs.Account.Address, eventArgs.Account.ChainId);
    }

    private void HandleAccountDisconnected(
        object sender,
        Connector.AccountDisconnectedEventArgs eventArgs
    )
    {
        ClearConnectedContext(true);
    }

    private void HandleChainChanged(
        object sender,
        Connector.ChainChangedEventArgs eventArgs
    )
    {
        string nextChain = eventArgs.ChainId;
        if (
            string.Equals(
                ConnectedChain,
                nextChain,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return;
        }

        ConnectedChain = nextChain;
        ContextVersion++;
        WalletContextChanged?.Invoke();
    }

    private void SetConnectedAddress(string address, string chain)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            ClearConnectedContext(true);
            ReportWalletError("Reown returned an empty wallet address.");
            return;
        }

        address = address.Trim();
        chain = chain?.Trim();

        if (
            string.Equals(
                ConnectedAddress,
                address,
                StringComparison.OrdinalIgnoreCase
            ) &&
            string.Equals(
                ConnectedChain,
                chain,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return;
        }

        ConnectedAddress = address;
        ConnectedChain = chain;
        ContextVersion++;
        WalletConnected?.Invoke(ConnectedAddress);
        WalletContextChanged?.Invoke();
        Debug.Log($"Wallet connected: {ConnectedAddress}");
    }

    private void ClearConnectedContext(bool notifyDisconnected)
    {
        bool hadContext =
            !string.IsNullOrWhiteSpace(ConnectedAddress) ||
            !string.IsNullOrWhiteSpace(ConnectedChain);

        if (!hadContext)
        {
            return;
        }

        ConnectedAddress = null;
        ConnectedChain = null;
        ContextVersion++;
        WalletContextChanged?.Invoke();

        if (notifyDisconnected)
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
