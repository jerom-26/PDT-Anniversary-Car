using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VerifiedVehicleUnlockCoordinator : MonoBehaviour
{
    [Header("Verified ownership")]
    [SerializeField] private ERC721OwnershipReader ownershipReader;

    [Header("Entitlement resolution")]
    [Tooltip(
        "Must implement ITokenEntitlementService. The current scene uses " +
        "the explicit legacy adapter for development tokens 0 and 1."
    )]
    [SerializeField] private MonoBehaviour tokenEntitlementServiceSource;
    [SerializeField] private OwnedVehicleRegistry ownedVehicleRegistry;
    [SerializeField] private VehicleSpawner vehicleSpawner;
    [SerializeField] private bool spawnFirstUnlockedVehicle = true;

    private ITokenEntitlementService tokenEntitlementService;
    private Coroutine entitlementResolutionCoroutine;
    public bool IsResolving { get; private set; }
    public bool HasUnavailableResults { get; private set; }

    // Entry point for every new protected driving session/action.
    public void BeginProtectedSession()
    {
        if (ownershipReader != null)
        {
            ownershipReader.RefreshOwnership();
        }
    }

    public event Action EntitlementResolutionStarted;
    public event Action<int> EntitlementResolutionCompleted;
    public event Action<string> EntitlementResolutionFailed;

    private void Awake()
    {
        TryResolveEntitlementService(out _);
    }

    private void OnEnable()
    {
        if (ownershipReader == null)
        {
            return;
        }

        ownershipReader.OwnershipScanCompleted +=
            HandleOwnershipScanCompleted;
        ownershipReader.OwnershipCleared += HandleOwnershipCleared;
    }

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            ReportEntitlementFailure(
                "VerifiedVehicleUnlockCoordinator is missing a component " +
                "reference."
            );
            return;
        }

        if (!TryResolveEntitlementService(out string entitlementError))
        {
            ReportEntitlementFailure(entitlementError);
        }
    }

    private void OnDisable()
    {
        StopEntitlementResolution();

        if (ownershipReader == null)
        {
            return;
        }

        ownershipReader.OwnershipScanCompleted -=
            HandleOwnershipScanCompleted;
        ownershipReader.OwnershipCleared -= HandleOwnershipCleared;
    }

    private void HandleOwnershipScanCompleted(
        IReadOnlyList<VerifiedNFT> verifiedTokens
    )
    {
        if (!HasRequiredReferences())
        {
            ReportEntitlementFailure(
                "Cannot unlock vehicles because the verified wallet flow " +
                "is missing a component reference."
            );
            return;
        }

        if (!TryResolveEntitlementService(out string entitlementError))
        {
            ReportEntitlementFailure(entitlementError);
            return;
        }

        StopEntitlementResolution();
        ownedVehicleRegistry.Clear();
        vehicleSpawner.Despawn();
        HasUnavailableResults = ownershipReader.HasUnavailableResults;

        if (verifiedTokens == null || verifiedTokens.Count == 0)
        {
            Debug.Log(HasUnavailableResults
                ? "Verification incomplete; no fresh vehicle access granted."
                : "No verified PDT vehicles found for this wallet.");
            return;
        }

        List<VerifiedNFT> tokenSnapshot =
            new List<VerifiedNFT>(verifiedTokens);

        IsResolving = true;
        EntitlementResolutionStarted?.Invoke();
        entitlementResolutionCoroutine = StartCoroutine(
            ResolveVerifiedEntitlements(tokenSnapshot)
        );
    }

    private void HandleOwnershipCleared()
    {
        StopEntitlementResolution();
        HasUnavailableResults = false;

        if (ownedVehicleRegistry != null)
        {
            ownedVehicleRegistry.Clear();
        }

        if (vehicleSpawner != null)
        {
            vehicleSpawner.Despawn();
        }
    }

    private IEnumerator ResolveVerifiedEntitlements(
        IReadOnlyList<VerifiedNFT> verifiedTokens
    )
    {
        int generation = ownershipReader.ScanGeneration;
        var resolved = new List<TokenEntitlement>();
        foreach (VerifiedNFT verifiedToken in verifiedTokens)
        {
            if (verifiedToken?.tokenReference == null)
            {
                Debug.LogError(
                    "Ownership verification returned an invalid token " +
                    "reference."
                );
                continue;
            }

            TokenEntitlement resolvedEntitlement = null;
            string resolutionError = null;

            yield return tokenEntitlementService
                .ResolveVerifiedTokenEntitlement(
                    verifiedToken.tokenReference,
                    entitlement => resolvedEntitlement = entitlement,
                    error => resolutionError = error
                );

            if (
                generation != ownershipReader.ScanGeneration ||
                !ownershipReader.IsVerificationContextCurrent
            )
            {
                HandleOwnershipCleared();
                yield break;
            }

            if (
                tokenEntitlementServiceSource is ReownEntitlementKeyService direct &&
                direct.LastVerificationUnavailable
            )
            {
                HasUnavailableResults = true;
            }

            if (!string.IsNullOrWhiteSpace(resolutionError))
            {
                Debug.LogError(
                    $"Token {verifiedToken.tokenID} entitlement failed: " +
                    resolutionError
                );
                continue;
            }

            if (resolvedEntitlement == null)
            {
                Debug.LogError(
                    $"Token {verifiedToken.tokenID} returned no " +
                    "entitlement."
                );
                continue;
            }

            if (
                !verifiedToken.tokenReference.Equals(
                    resolvedEntitlement.TokenReference
                )
            )
            {
                Debug.LogWarning(
                    "Rejected entitlement for a different token reference."
                );
                continue;
            }

            resolved.Add(resolvedEntitlement);
        }

        entitlementResolutionCoroutine = null;
        IsResolving = false;

        if (
            generation != ownershipReader.ScanGeneration ||
            !ownershipReader.IsVerificationContextCurrent
        )
        {
            yield break;
        }

        foreach (TokenEntitlement entitlement in resolved)
        {
            ownedVehicleRegistry.TryRegisterResolvedEntitlement(
                entitlement,
                out _
            );
        }

        if (ownedVehicleRegistry.UnlockedVehicles.Count == 0)
        {
            ReportEntitlementFailure(
                HasUnavailableResults
                    ? "Verification unavailable for some tokens; no supported vehicle could be authorized."
                    : "Verified tokens provide no supported vehicle entitlement."
            );
            yield break;
        }

        if (spawnFirstUnlockedVehicle)
        {
            if (
                !vehicleSpawner.TrySpawn(
                    ownedVehicleRegistry.UnlockedVehicles[0],
                    ownedVehicleRegistry
                )
            )
            {
                ReportEntitlementFailure(
                    "The verified vehicle could not be spawned."
                );
                yield break;
            }
        }

        int unlockedVehicleCount =
            ownedVehicleRegistry.UnlockedVehicles.Count;

        Debug.Log(
            $"Verified entitlements unlocked " +
            $"{unlockedVehicleCount} vehicle(s)."
        );
        EntitlementResolutionCompleted?.Invoke(unlockedVehicleCount);
    }

    private bool TryResolveEntitlementService(out string errorMessage)
    {
        tokenEntitlementService =
            tokenEntitlementServiceSource as ITokenEntitlementService;

        if (tokenEntitlementService != null)
        {
            errorMessage = null;
            return true;
        }

        errorMessage =
            "VerifiedVehicleUnlockCoordinator requires a component that " +
            "implements ITokenEntitlementService.";
        return false;
    }

    private void StopEntitlementResolution()
    {
        IsResolving = false;
        if (entitlementResolutionCoroutine == null)
        {
            return;
        }

        StopCoroutine(entitlementResolutionCoroutine);
        entitlementResolutionCoroutine = null;
    }

    private bool HasRequiredReferences()
    {
        return
            ownershipReader != null &&
            tokenEntitlementServiceSource != null &&
            ownedVehicleRegistry != null &&
            vehicleSpawner != null;
    }

    private void ReportEntitlementFailure(string message)
    {
        IsResolving = false;
        EntitlementResolutionFailed?.Invoke(message);
        Debug.LogError(message);
    }
}
