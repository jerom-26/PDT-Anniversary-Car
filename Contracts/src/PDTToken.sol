// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {ERC721Upgradeable} from "@openzeppelin/contracts-upgradeable/token/ERC721/ERC721Upgradeable.sol";
import {ERC721BurnableUpgradeable} from "@openzeppelin/contracts-upgradeable/token/ERC721/extensions/ERC721BurnableUpgradeable.sol";
import {AccessControlUpgradeable} from "@openzeppelin/contracts-upgradeable/access/AccessControlUpgradeable.sol";
import {UUPSUpgradeable} from "@openzeppelin/contracts-upgradeable/proxy/utils/UUPSUpgradeable.sol";
import {IPDTEntitlement} from "../IPDTEntitlement.sol";

/// @notice Official PDT digital entitlements. No physical authentication claim.
contract PDTToken is ERC721BurnableUpgradeable, AccessControlUpgradeable, UUPSUpgradeable, IPDTEntitlement {
    bytes32 public constant PDT_ADMIN = keccak256("PDT_ADMIN");
    bytes32 public constant MINTER = keccak256("MINTER");
    bytes32 public constant ENTITLEMENT_ADMIN = keccak256("ENTITLEMENT_ADMIN");
    bytes32 public constant UPGRADE_ADMIN = keccak256("UPGRADE_ADMIN");

    uint256 private _nextTokenId;
    mapping(bytes32 => bool) private _approvedKeys;
    mapping(uint256 => bytes32) private _entitlements;

    error InvalidAdmin();
    error InvalidEntitlementKey(bytes32 key);
    error EntitlementKeyNotApproved(bytes32 key);

    event EntitlementKeyApprovalChanged(bytes32 indexed key, bool approved);
    event EntitlementChanged(uint256 indexed tokenId, bytes32 indexed oldKey, bytes32 indexed newKey);

    /// @custom:oz-upgrades-unsafe-allow constructor
    constructor() {
        _disableInitializers();
    }

    function initialize(string memory name_, string memory symbol_, address admin) public initializer {
        if (admin == address(0)) revert InvalidAdmin();
        __ERC721_init(name_, symbol_);
        __ERC721Burnable_init();
        __AccessControl_init();
        __UUPSUpgradeable_init();
        _setRoleAdmin(PDT_ADMIN, PDT_ADMIN);
        _setRoleAdmin(DEFAULT_ADMIN_ROLE, PDT_ADMIN);
        _setRoleAdmin(MINTER, PDT_ADMIN);
        _setRoleAdmin(ENTITLEMENT_ADMIN, PDT_ADMIN);
        _setRoleAdmin(UPGRADE_ADMIN, PDT_ADMIN);
        _grantRole(PDT_ADMIN, admin);
    }

    function isEntitlementKeyApproved(bytes32 key) external view returns (bool) {
        return _approvedKeys[key];
    }

    function setEntitlementKeyApproved(bytes32 key, bool approved) external onlyRole(ENTITLEMENT_ADMIN) {
        _validateKey(key);
        _approvedKeys[key] = approved;
        emit EntitlementKeyApprovalChanged(key, approved);
    }

    function mint(address to, bytes32 key) external onlyRole(MINTER) returns (uint256 tokenId) {
        _requireApprovedKey(key);
        tokenId = _nextTokenId++;
        _entitlements[tokenId] = key;
        emit EntitlementChanged(tokenId, bytes32(0), key);
        _safeMint(to, tokenId);
    }

    function entitlementKeyOf(uint256 tokenId) external view returns (bytes32) {
        _requireOwned(tokenId);
        return _entitlements[tokenId];
    }

    function setEntitlement(uint256 tokenId, bytes32 key) external onlyRole(ENTITLEMENT_ADMIN) {
        _requireOwned(tokenId);
        _requireApprovedKey(key);
        bytes32 oldKey = _entitlements[tokenId];
        _entitlements[tokenId] = key;
        emit EntitlementChanged(tokenId, oldKey, key);
    }

    function nextTokenId() external view returns (uint256) {
        return _nextTokenId;
    }

    function _update(address to, uint256 tokenId, address auth) internal override returns (address) {
        address from = super._update(to, tokenId, auth);
        if (to == address(0)) delete _entitlements[tokenId];
        return from;
    }

    function _requireApprovedKey(bytes32 key) private view {
        _validateKey(key);
        if (!_approvedKeys[key]) revert EntitlementKeyNotApproved(key);
    }

    function _validateKey(bytes32 key) private pure {
        if (key[0] != 0x50 || key[1] != 0x44 || key[2] != 0x54 || key[3] != 0x5f || key[4] == 0)
            revert InvalidEntitlementKey(key);
        bool padding;
        for (uint256 i; i < 32; ++i) {
            uint8 c = uint8(key[i]);
            if (c == 0) { padding = true; continue; }
            if (padding || !((c >= 65 && c <= 90) || (c >= 48 && c <= 57) || c == 95))
                revert InvalidEntitlementKey(key);
        }
    }

    function _authorizeUpgrade(address) internal override onlyRole(UPGRADE_ADMIN) {}

    function supportsInterface(bytes4 interfaceId) public view override(ERC721Upgradeable, AccessControlUpgradeable) returns (bool) {
        return interfaceId == type(IPDTEntitlement).interfaceId || super.supportsInterface(interfaceId);
    }
}
