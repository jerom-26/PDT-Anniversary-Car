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

        if (verifiedTokens == null || verifiedTokens.Count == 0)
        {
            Debug.Log("The connected wallet has no PDT vehicles to unlock.");
            return;
        }

        List<VerifiedNFT> tokenSnapshot =
            new List<VerifiedNFT>(verifiedTokens);

        EntitlementResolutionStarted?.Invoke();
        entitlementResolutionCoroutine = StartCoroutine(
            ResolveVerifiedEntitlements(tokenSnapshot)
        );
    }

    private void HandleOwnershipCleared()
    {
        StopEntitlementResolution();

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

            ownedVehicleRegistry.TryRegisterResolvedEntitlement(
                resolvedEntitlement,
                out _
            );
        }

        entitlementResolutionCoroutine = null;

        if (ownedVehicleRegistry.UnlockedVehicles.Count == 0)
        {
            ReportEntitlementFailure(
                "The wallet owns verified NFTs, but none provide a " +
                "supported vehicle entitlement."
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
        EntitlementResolutionFailed?.Invoke(message);
        Debug.LogError(message);
    }
}
