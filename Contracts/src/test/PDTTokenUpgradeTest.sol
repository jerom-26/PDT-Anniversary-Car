// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;
import {PDTToken} from "../PDTToken.sol";

/// @notice Test-only upgrade; never deployed as the milestone implementation.
/// @custom:oz-upgrades-unsafe-allow missing-initializer
contract PDTTokenUpgradeTest is PDTToken {
    uint256 public versionMarker;
    function initializeV2() external reinitializer(2) onlyRole(UPGRADE_ADMIN) {
        versionMarker = 2;
    }
}
