using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class WalletStatusUI : MonoBehaviour
{
    private static readonly Color PanelColor =
        new Color(0.035f, 0.047f, 0.071f, 0.94f);
    private static readonly Color PrimaryTextColor =
        new Color(0.94f, 0.96f, 1f, 1f);
    private static readonly Color SecondaryTextColor =
        new Color(0.64f, 0.70f, 0.80f, 1f);
    private static readonly Color ReadyColor =
        new Color(0.42f, 0.72f, 1f, 1f);
    private static readonly Color SuccessColor =
        new Color(0.38f, 0.88f, 0.58f, 1f);
    private static readonly Color NoticeColor =
        new Color(1f, 0.72f, 0.30f, 1f);
    private static readonly Color ErrorColor =
        new Color(1f, 0.40f, 0.40f, 1f);
    private static readonly Color ButtonColor =
        new Color(0.10f, 0.31f, 0.58f, 1f);
    private static readonly Color SecondaryButtonColor =
        new Color(0.16f, 0.19f, 0.27f, 1f);
    private static readonly Color DangerButtonColor =
        new Color(0.48f, 0.14f, 0.18f, 1f);

    [Header("Wallet flow")]
    [SerializeField] private ReownWalletConnector walletConnector;
    [SerializeField] private ERC721OwnershipReader ownershipReader;
    [SerializeField] private VerifiedVehicleUnlockCoordinator unlockCoordinator;
    [SerializeField] private OwnedVehicleRegistry ownedVehicleRegistry;

    private GameObject canvasRoot;
    private TextMeshProUGUI walletText;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI verifiedNFTText;
    private TextMeshProUGUI vehicleText;
    private Button primaryWalletButton;
    private TextMeshProUGUI primaryWalletButtonText;
    private Button refreshButton;
    private Button disconnectButton;
    private bool eventsSubscribed;
    private bool scanInProgress;
    private bool entitlementInProgress;
    private int verifiedNFTCount;

    private void Awake()
    {
        BuildUI();
        EnsureEventSystem();
    }

    private void OnEnable()
    {
        SubscribeToEvents();

        if (canvasRoot != null)
        {
            canvasRoot.SetActive(true);
        }
    }

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            SetStatus(
                "Wallet UI is missing a required component reference.",
                ErrorColor
            );
            SetAllButtonsInteractable(false);
            return;
        }

        scanInProgress = ownershipReader.IsScanning;
        verifiedNFTCount = ownershipReader.VerifiedTokens.Count;
        UpdateVerifiedNFTText();

        if (ownedVehicleRegistry.UnlockedVehicles.Count > 0)
        {
            UpdateVehicleText(
                ownedVehicleRegistry.UnlockedVehicles[0]
            );
        }

        if (walletConnector.IsConnected)
        {
            UpdateWalletAddress(walletConnector.ConnectedAddress);

            if (scanInProgress)
            {
                SetStatus(
                    "Checking official PDT NFT ownership...",
                    ReadyColor
                );
            }
            else
            {
                SetStatus("Wallet connected.", SuccessColor);
            }
        }
        else if (walletConnector.IsInitialized)
        {
            ShowDisconnectedState("Ready to connect a wallet.");
        }
        else
        {
            ShowDisconnectedState("Initializing wallet connection...");
        }

        UpdateButtonStates();
    }

    private void OnDisable()
    {
        UnsubscribeFromEvents();

        if (canvasRoot != null)
        {
            canvasRoot.SetActive(false);
        }
    }

    private void SubscribeToEvents()
    {
        if (eventsSubscribed || !HasRequiredReferences())
        {
            return;
        }

        walletConnector.WalletInitialized += HandleWalletInitialized;
        walletConnector.WalletConnected += HandleWalletConnected;
        walletConnector.WalletDisconnected += HandleWalletDisconnected;
        walletConnector.WalletDisconnectCompleted +=
            HandleWalletDisconnectCompleted;
        walletConnector.WalletError += HandleWalletError;

        ownershipReader.OwnershipScanStarted += HandleOwnershipScanStarted;
        ownershipReader.TokenVerified += HandleTokenVerified;
        ownershipReader.OwnershipScanCompleted +=
            HandleOwnershipScanCompleted;
        ownershipReader.OwnershipScanFailed += HandleOwnershipScanFailed;
        ownershipReader.OwnershipCleared += HandleOwnershipCleared;

        unlockCoordinator.EntitlementResolutionStarted +=
            HandleEntitlementResolutionStarted;
        unlockCoordinator.EntitlementResolutionCompleted +=
            HandleEntitlementResolutionCompleted;
        unlockCoordinator.EntitlementResolutionFailed +=
            HandleEntitlementResolutionFailed;

        ownedVehicleRegistry.VehicleUnlocked += HandleVehicleUnlocked;
        ownedVehicleRegistry.RegistryCleared += HandleRegistryCleared;

        eventsSubscribed = true;
    }

    private void UnsubscribeFromEvents()
    {
        if (!eventsSubscribed)
        {
            return;
        }

        walletConnector.WalletInitialized -= HandleWalletInitialized;
        walletConnector.WalletConnected -= HandleWalletConnected;
        walletConnector.WalletDisconnected -= HandleWalletDisconnected;
        walletConnector.WalletDisconnectCompleted -=
            HandleWalletDisconnectCompleted;
        walletConnector.WalletError -= HandleWalletError;

        ownershipReader.OwnershipScanStarted -= HandleOwnershipScanStarted;
        ownershipReader.TokenVerified -= HandleTokenVerified;
        ownershipReader.OwnershipScanCompleted -=
            HandleOwnershipScanCompleted;
        ownershipReader.OwnershipScanFailed -= HandleOwnershipScanFailed;
        ownershipReader.OwnershipCleared -= HandleOwnershipCleared;

        unlockCoordinator.EntitlementResolutionStarted -=
            HandleEntitlementResolutionStarted;
        unlockCoordinator.EntitlementResolutionCompleted -=
            HandleEntitlementResolutionCompleted;
        unlockCoordinator.EntitlementResolutionFailed -=
            HandleEntitlementResolutionFailed;

        ownedVehicleRegistry.VehicleUnlocked -= HandleVehicleUnlocked;
        ownedVehicleRegistry.RegistryCleared -= HandleRegistryCleared;

        eventsSubscribed = false;
    }

    private void HandleWalletInitialized()
    {
        if (walletConnector.IsConnected)
        {
            UpdateWalletAddress(walletConnector.ConnectedAddress);
            SetStatus("Wallet connected.", SuccessColor);
        }
        else
        {
            ShowDisconnectedState("Ready to connect a wallet.");
        }

        UpdateButtonStates();
    }

    private void HandleWalletConnected(string address)
    {
        scanInProgress = true;
        entitlementInProgress = false;
        verifiedNFTCount = 0;
        UpdateWalletAddress(address);
        UpdateVerifiedNFTText();
        SetVehicleLocked();
        SetStatus(
            "Wallet connected. Checking official PDT NFTs...",
            ReadyColor
        );
        UpdateButtonStates();
    }

    private void HandleWalletDisconnected()
    {
        scanInProgress = false;
        entitlementInProgress = false;
        verifiedNFTCount = 0;
        ShowDisconnectedState("Wallet disconnected.");
        UpdateButtonStates();
    }

    private void HandleWalletDisconnectCompleted()
    {
        if (!walletConnector.IsConnected)
        {
            ShowDisconnectedState("Wallet disconnected.");
        }

        UpdateButtonStates();
    }

    private void HandleWalletError(string message)
    {
        scanInProgress = false;
        entitlementInProgress = false;
        SetStatus(message, ErrorColor);
        UpdateButtonStates();
    }

    private void HandleOwnershipScanStarted()
    {
        scanInProgress = true;
        entitlementInProgress = false;
        verifiedNFTCount = 0;
        UpdateVerifiedNFTText();
        SetVehicleLocked();
        SetStatus(
            "Checking official PDT NFT ownership...",
            ReadyColor
        );
        UpdateButtonStates();
    }

    private void HandleTokenVerified(VerifiedNFT verifiedToken)
    {
        verifiedNFTCount++;
        UpdateVerifiedNFTText();
    }

    private void HandleOwnershipScanCompleted(
        IReadOnlyList<VerifiedNFT> verifiedTokens
    )
    {
        scanInProgress = false;
        verifiedNFTCount = verifiedTokens?.Count ?? 0;
        UpdateVerifiedNFTText();

        if (verifiedNFTCount == 0)
        {
            entitlementInProgress = false;
            SetVehicleLocked();
            SetStatus(
                "No PDT vehicle NFT was found in this wallet.",
                NoticeColor
            );
        }
        else
        {
            entitlementInProgress = true;
            SetStatus(
                "Ownership verified. Resolving vehicle entitlement...",
                ReadyColor
            );
        }

        UpdateButtonStates();
    }

    private void HandleOwnershipScanFailed(string message)
    {
        scanInProgress = false;
        entitlementInProgress = false;
        verifiedNFTCount = 0;
        UpdateVerifiedNFTText();
        SetVehicleLocked();
        SetStatus(message, ErrorColor);
        UpdateButtonStates();
    }

    private void HandleOwnershipCleared()
    {
        verifiedNFTCount = 0;
        UpdateVerifiedNFTText();
        SetVehicleLocked();

        if (scanInProgress)
        {
            return;
        }

        if (!walletConnector.IsConnected)
        {
            ShowDisconnectedState("Wallet disconnected.");
        }
    }

    private void HandleEntitlementResolutionStarted()
    {
        entitlementInProgress = true;
        SetStatus(
            "Resolving verified vehicle entitlement...",
            ReadyColor
        );
        UpdateButtonStates();
    }

    private void HandleEntitlementResolutionCompleted(int vehicleCount)
    {
        entitlementInProgress = false;
        string vehicleWord = vehicleCount == 1 ? "vehicle" : "vehicles";
        SetStatus(
            $"Ready — {vehicleCount} {vehicleWord} unlocked.",
            SuccessColor
        );
        UpdateButtonStates();
    }

    private void HandleEntitlementResolutionFailed(string message)
    {
        entitlementInProgress = false;
        SetStatus(message, ErrorColor);
        UpdateButtonStates();
    }

    private void HandleVehicleUnlocked(VehicleData vehicleData)
    {
        UpdateVehicleText(vehicleData);
    }

    private void HandleRegistryCleared()
    {
        SetVehicleLocked();
    }

    private void HandlePrimaryWalletButtonClicked()
    {
        if (walletConnector.IsConnected)
        {
            walletConnector.OpenAccountModal();
            return;
        }

        SetStatus("Choose a wallet to connect.", ReadyColor);
        walletConnector.OpenWalletModal();
    }

    private void HandleRefreshButtonClicked()
    {
        ownershipReader.RefreshOwnership();
    }

    private void HandleDisconnectButtonClicked()
    {
        SetStatus("Disconnecting wallet...", NoticeColor);
        walletConnector.DisconnectWallet();
        UpdateButtonStates();
    }

    private void ShowDisconnectedState(string message)
    {
        walletText.text = "Wallet: Not connected";
        verifiedNFTText.text = "Verified PDT NFTs: —";
        SetVehicleLocked();
        SetStatus(message, SecondaryTextColor);
    }

    private void UpdateWalletAddress(string address)
    {
        walletText.text = "Wallet: " + ShortenAddress(address);
    }

    private void UpdateVerifiedNFTText()
    {
        verifiedNFTText.text =
            $"Verified PDT NFTs: {verifiedNFTCount}";
    }

    private void UpdateVehicleText(VehicleData vehicleData)
    {
        if (vehicleData == null)
        {
            SetVehicleLocked();
            return;
        }

        vehicleText.text =
            $"Vehicle: {vehicleData.DisplayName}\n" +
            $"Entitlement: {vehicleData.EntitlementKey}";
        vehicleText.color = SuccessColor;
    }

    private void SetVehicleLocked()
    {
        vehicleText.text = "Vehicle: Locked";
        vehicleText.color = SecondaryTextColor;
    }

    private void SetStatus(string message, Color color)
    {
        statusText.text = "Status: " + message;
        statusText.color = color;
    }

    private void UpdateButtonStates()
    {
        if (!HasRequiredReferences())
        {
            SetAllButtonsInteractable(false);
            return;
        }

        bool isConnected = walletConnector.IsConnected;
        bool isBusy =
            scanInProgress ||
            entitlementInProgress ||
            walletConnector.IsDisconnecting;

        primaryWalletButton.interactable =
            walletConnector.IsInitialized &&
            !walletConnector.IsDisconnecting;
        primaryWalletButtonText.text = isConnected
            ? "Wallet Account"
            : "Connect Wallet";
        refreshButton.interactable = isConnected && !isBusy;
        disconnectButton.interactable =
            isConnected && !walletConnector.IsDisconnecting;
    }

    private void SetAllButtonsInteractable(bool isInteractable)
    {
        primaryWalletButton.interactable = isInteractable;
        refreshButton.interactable = isInteractable;
        disconnectButton.interactable = isInteractable;
    }

    private bool HasRequiredReferences()
    {
        return
            walletConnector != null &&
            ownershipReader != null &&
            unlockCoordinator != null &&
            ownedVehicleRegistry != null;
    }

    private static string ShortenAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length <= 12)
        {
            return address ?? "Not connected";
        }

        return address.Substring(0, 6) + "..." +
            address.Substring(address.Length - 4);
    }

    private void BuildUI()
    {
        canvasRoot = new GameObject(
            "PDT Wallet UI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasRoot.transform.SetParent(transform, false);

        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler canvasScaler = canvasRoot.GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasScaler.matchWidthOrHeight = 0.5f;

        GameObject panel = CreateUIObject("Wallet Panel", canvasRoot.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(24f, -24f);
        panelRect.sizeDelta = new Vector2(470f, 350f);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = false;

        VerticalLayoutGroup panelLayout =
            panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 7f;
        panelLayout.childAlignment = TextAnchor.UpperLeft;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(
            "Title",
            panel.transform,
            "PDT ANNIVERSARY CAR",
            24f,
            34f,
            PrimaryTextColor
        );
        title.fontStyle = FontStyles.Bold;

        CreateText(
            "Network",
            panel.transform,
            "Network: Polygon Amoy Testnet",
            15f,
            24f,
            ReadyColor
        );

        walletText = CreateText(
            "Wallet",
            panel.transform,
            "Wallet: Not connected",
            16f,
            27f,
            PrimaryTextColor
        );

        statusText = CreateText(
            "Status",
            panel.transform,
            "Status: Initializing wallet connection...",
            16f,
            48f,
            SecondaryTextColor
        );

        verifiedNFTText = CreateText(
            "Verified NFTs",
            panel.transform,
            "Verified PDT NFTs: —",
            16f,
            27f,
            PrimaryTextColor
        );

        vehicleText = CreateText(
            "Vehicle",
            panel.transform,
            "Vehicle: Locked",
            15f,
            48f,
            SecondaryTextColor
        );

        GameObject buttonRow = CreateUIObject(
            "Wallet Actions",
            panel.transform
        );
        LayoutElement buttonRowLayout = buttonRow.AddComponent<LayoutElement>();
        buttonRowLayout.preferredHeight = 44f;

        HorizontalLayoutGroup horizontalLayout =
            buttonRow.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.spacing = 8f;
        horizontalLayout.childAlignment = TextAnchor.MiddleCenter;
        horizontalLayout.childControlWidth = true;
        horizontalLayout.childControlHeight = true;
        horizontalLayout.childForceExpandWidth = true;
        horizontalLayout.childForceExpandHeight = true;

        primaryWalletButton = CreateButton(
            "Primary Wallet Button",
            buttonRow.transform,
            "Connect Wallet",
            ButtonColor,
            out primaryWalletButtonText
        );
        refreshButton = CreateButton(
            "Refresh Button",
            buttonRow.transform,
            "Check Again",
            SecondaryButtonColor,
            out _
        );
        disconnectButton = CreateButton(
            "Disconnect Button",
            buttonRow.transform,
            "Disconnect",
            DangerButtonColor,
            out _
        );

        primaryWalletButton.onClick.AddListener(
            HandlePrimaryWalletButtonClicked
        );
        refreshButton.onClick.AddListener(HandleRefreshButtonClicked);
        disconnectButton.onClick.AddListener(HandleDisconnectButtonClicked);
    }

    private static TextMeshProUGUI CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        float preferredHeight,
        Color color
    )
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        LayoutElement layoutElement =
            textObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = preferredHeight;

        return text;
    }

    private static Button CreateButton(
        string objectName,
        Transform parent,
        string label,
        Color backgroundColor,
        out TextMeshProUGUI labelText
    )
    {
        GameObject buttonObject = CreateUIObject(objectName, parent);
        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = backgroundColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.65f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        GameObject labelObject = CreateUIObject("Label", buttonObject.transform);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 2f);
        labelRect.offsetMax = new Vector2(-6f, -2f);

        labelText = labelObject.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 14f;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = PrimaryTextColor;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
        {
            labelText.font = TMP_Settings.defaultFontAsset;
        }

        return button;
    }

    private static GameObject CreateUIObject(
        string objectName,
        Transform parent
    )
    {
        GameObject uiObject = new GameObject(
            objectName,
            typeof(RectTransform)
        );
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject(
            "PDT UI EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule)
        );
        eventSystem.transform.SetParent(transform, false);
    }
}
