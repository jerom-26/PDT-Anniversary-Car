// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

/// @notice Entitlement boundary required by future PDT NFT collections.
/// @dev Implementations should revert for nonexistent token IDs.
interface IPDTEntitlement
{
    function entitlementKeyOf(
        uint256 tokenId
    ) external view returns (bytes32);
}

/// @notice Canonical keys are protocol identifiers, not metadata Asset IDs.
library PDTEntitlementKeys
{
    bytes32 internal constant PDT_VEHICLE_DREAM_MOBILE_80TH =
        bytes32("PDT_VEHICLE_DREAM_MOBILE_80TH");
}
