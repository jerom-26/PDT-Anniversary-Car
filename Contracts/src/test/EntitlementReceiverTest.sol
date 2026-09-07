// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;
import {IERC721Receiver} from "@openzeppelin/contracts/token/ERC721/IERC721Receiver.sol";
import {PDTToken} from "../PDTToken.sol";

contract EntitlementReceiverTest is IERC721Receiver {
    bytes32 public observedKey;
    bool public burnOnReceive;
    constructor(bool burn_) { burnOnReceive = burn_; }
    function onERC721Received(address, address, uint256 id, bytes calldata) external returns (bytes4) {
        observedKey = PDTToken(msg.sender).entitlementKeyOf(id);
        if (burnOnReceive) PDTToken(msg.sender).burn(id);
        return IERC721Receiver.onERC721Received.selector;
    }
}
