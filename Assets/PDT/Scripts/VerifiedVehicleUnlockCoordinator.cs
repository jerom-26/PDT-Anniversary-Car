using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class VerifiedVehicleUnlockCoordinator : MonoBehaviour
{
    [Header("Verified ownership")]
    [SerializeField] private ERC721OwnershipReader ownershipReader;

    [Header("Entitlement resolution")]
    [Tooltip(
        "Must implement ITokenEntitlementService. Discovery alone never " +
        "authorizes a vehicle."
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
            return;
        }

        HandleOwnershipCleared();
        ReportEntitlementFailure(
            "Cannot verify vehicle access without an ownership reader."
        );
    }

    public event Action EntitlementResolutionStarted;
    public event Action<int> EntitlementResolutionCompleted;
    public event Action<string> EntitlementResolutionFailed;

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
        HandleOwnershipCleared();

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
            HandleOwnershipCleared();
            ReportEntitlementFailure(
                "Cannot unlock vehicles because the verified wallet flow " +
                "is missing a component reference."
            );
            return;
        }

        if (!TryResolveEntitlementService(out string entitlementError))
        {
            HandleOwnershipCleared();
            ReportEntitlementFailure(entitlementError);
            return;
        }

        StopEntitlementResolution();
        ownedVehicleRegistry.Clear();
        vehicleSpawner.Despawn();
        HasUnavailableResults = ownershipReader.HasUnavailableResults;

        if (verifiedTokens == null || verifiedTokens.Count == 0)
        {
            Debug.Log(ownershipReader.HasUnavailableResults
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
        ClearAuthorization(true);
    }

    private IEnumerator ResolveVerifiedEntitlements(
        IReadOnlyList<VerifiedNFT> verifiedTokens
    )
    {
        int generation = ownershipReader.ScanGeneration;
        var resolved = new List<TokenEntitlement>();
        var uniqueTokens = new HashSet<TokenReference>();

        foreach (VerifiedNFT verifiedToken in verifiedTokens)
        {
            if (
                verifiedToken?.TokenReference == null ||
                !uniqueTokens.Add(verifiedToken.TokenReference)
            )
            {
                if (verifiedToken?.TokenReference == null)
                {
                    Debug.LogError(
                        "Ownership verification returned an invalid token " +
                        "reference."
                    );
                }

                continue;
            }

            TokenEntitlement resolvedEntitlement = null;
            string resolutionError = null;

            yield return tokenEntitlementService
                .ResolveVerifiedTokenEntitlement(
                    verifiedToken.TokenReference,
                    entitlement => resolvedEntitlement = entitlement,
                    error => resolutionError = error
                );

            if (generation != ownershipReader.ScanGeneration || !ownershipReader.IsVerificationContextCurrent)
            {
                ClearAuthorization(false);
                yield break;
            }
            if (tokenEntitlementServiceSource is ReownEntitlementKeyService direct && direct.LastVerificationUnavailable)
                HasUnavailableResults = true;

            if (!string.IsNullOrWhiteSpace(resolutionError))
            {
                Debug.LogError(
                    $"Token {verifiedToken.TokenID} entitlement failed: " +
                    resolutionError
                );
                continue;
            }

            if (resolvedEntitlement == null)
            {
                Debug.LogError(
                    $"Token {verifiedToken.TokenID} returned no " +
                    "entitlement."
                );
                continue;
            }

            if (!verifiedToken.TokenReference.Equals(resolvedEntitlement.TokenReference))
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
        if (generation != ownershipReader.ScanGeneration || !ownershipReader.IsVerificationContextCurrent) yield break;
        foreach (TokenEntitlement entitlement in resolved)
            ownedVehicleRegistry.TryRegisterResolvedEntitlement(entitlement, out _);

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
                    ownedVehicleRegistry.UnlockedVehicles[0]
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

    private void ClearAuthorization(bool stopResolution)
    {
        if (stopResolution)
        {
            StopEntitlementResolution();
        }
        else
        {
            IsResolving = false;
            entitlementResolutionCoroutine = null;
        }

        if (ownedVehicleRegistry != null)
        {
            ownedVehicleRegistry.Clear();
        }

        if (vehicleSpawner != null)
        {
            vehicleSpawner.Despawn();
        }

        HasUnavailableResults = false;
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
