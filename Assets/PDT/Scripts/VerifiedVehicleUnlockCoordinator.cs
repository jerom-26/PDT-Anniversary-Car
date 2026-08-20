using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VerifiedVehicleUnlockCoordinator : MonoBehaviour
{
    [Header("Verified ownership")]
    [SerializeField] private ERC721OwnershipReader ownershipReader;

    [Header("V2 vehicle flow")]
    [SerializeField] private NFTMetadataReader metadataReader;
    [SerializeField] private OwnedVehicleRegistry ownedVehicleRegistry;
    [SerializeField] private VehicleSpawner vehicleSpawner;
    [SerializeField] private bool spawnFirstUnlockedVehicle = true;

    private Coroutine metadataLoadCoroutine;

    private void OnEnable()
    {
        if (ownershipReader == null)
        {
            return;
        }

        ownershipReader.OwnershipScanCompleted += HandleOwnershipScanCompleted;
        ownershipReader.OwnershipCleared += HandleOwnershipCleared;
    }

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            Debug.LogError(
                "VerifiedVehicleUnlockCoordinator is missing a V2 component " +
                "reference."
            );
        }
    }

    private void OnDisable()
    {
        StopMetadataLoading();

        if (ownershipReader == null)
        {
            return;
        }

        ownershipReader.OwnershipScanCompleted -= HandleOwnershipScanCompleted;
        ownershipReader.OwnershipCleared -= HandleOwnershipCleared;
    }

    private void HandleOwnershipScanCompleted(
        IReadOnlyList<VerifiedNFT> verifiedTokens
    )
    {
        if (!HasRequiredReferences())
        {
            Debug.LogError(
                "Cannot unlock vehicles because the verified wallet flow is " +
                "missing a component reference."
            );
            return;
        }

        StopMetadataLoading();
        ownedVehicleRegistry.Clear();
        vehicleSpawner.Despawn();

        if (verifiedTokens == null || verifiedTokens.Count == 0)
        {
            Debug.Log("The connected wallet has no PDT vehicles to unlock.");
            return;
        }

        List<VerifiedNFT> tokenSnapshot =
            new List<VerifiedNFT>(verifiedTokens);

        metadataLoadCoroutine = StartCoroutine(
            LoadVerifiedMetadata(tokenSnapshot)
        );
    }

    private void HandleOwnershipCleared()
    {
        StopMetadataLoading();

        if (ownedVehicleRegistry != null)
        {
            ownedVehicleRegistry.Clear();
        }

        if (vehicleSpawner != null)
        {
            vehicleSpawner.Despawn();
        }
    }

    private IEnumerator LoadVerifiedMetadata(
        IReadOnlyList<VerifiedNFT> verifiedTokens
    )
    {
        foreach (VerifiedNFT verifiedToken in verifiedTokens)
        {
            if (verifiedToken == null)
            {
                continue;
            }

            NFTMetadata loadedMetadata = null;
            string loadError = null;

            yield return metadataReader.LoadMetadata(
                verifiedToken.metadataURI,
                metadata => loadedMetadata = metadata,
                error => loadError = error
            );

            if (!string.IsNullOrWhiteSpace(loadError))
            {
                Debug.LogError(
                    $"Token {verifiedToken.tokenID} metadata failed: " +
                    loadError
                );
                continue;
            }

            if (loadedMetadata == null)
            {
                Debug.LogError(
                    $"Token {verifiedToken.tokenID} returned no metadata."
                );
                continue;
            }

            ownedVehicleRegistry.TryRegisterVerifiedMetadata(
                loadedMetadata,
                out _
            );
        }

        metadataLoadCoroutine = null;

        if (ownedVehicleRegistry.UnlockedVehicles.Count == 0)
        {
            Debug.LogWarning(
                "The wallet owns verified NFTs, but none match the vehicle " +
                "catalog."
            );
            yield break;
        }

        Debug.Log(
            $"Verified wallet flow unlocked " +
            $"{ownedVehicleRegistry.UnlockedVehicles.Count} vehicle(s)."
        );

        if (spawnFirstUnlockedVehicle)
        {
            vehicleSpawner.TrySpawn(
                ownedVehicleRegistry.UnlockedVehicles[0]
            );
        }
    }

    private void StopMetadataLoading()
    {
        if (metadataLoadCoroutine == null)
        {
            return;
        }

        StopCoroutine(metadataLoadCoroutine);
        metadataLoadCoroutine = null;
    }

    private bool HasRequiredReferences()
    {
        return
            ownershipReader != null &&
            metadataReader != null &&
            ownedVehicleRegistry != null &&
            vehicleSpawner != null;
    }
}
