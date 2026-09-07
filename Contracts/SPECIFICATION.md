# PDT V2 contract specification — finalized

## Purpose and trust boundary

Official digital NFTs grant application content through approved chain + proxy
address, current `ownerOf(tokenId)`, and `entitlementKeyOf(tokenId)`. Unity maps
the resulting supported canonical key through VehicleCatalog. Discovery supplies
candidates only. Unity does not audit individual mint transactions: restricted
minting is enforced by the approved contract.

Physical uniqueness, possession, ownership and authenticity are outside this
milestone. Existing Amoy tokens 0 and 1 are legacy development fixtures and are
not migrated, modified or minted by this milestone.

## Permissions

- PDT_ADMIN grants/removes roles, including administrative roles. It receives no
  implicit mint, entitlement-edit, forced-burn or upgrade permission.
- MINTER mints to a nonzero recipient using an already approved key.
- ENTITLEMENT_ADMIN approves/disables keys and changes individual existing
  tokens to approved keys. Approval is for future assignment only.
- UPGRADE_ADMIN authorizes UUPS upgrades. PDT_ADMIN can grant this role and is
  therefore also within the upgrade trust boundary.
- NFT owners and approved ERC721 operators may burn. No administrative forced
  burn or dedicated revocation function exists.

Production may protect these authorities with multisig/timelocks. Administrative
role removal stops future privileged actions, not existing token entitlements.

## Key encoding and approval

Keys are readable ASCII directly encoded as bytes32, right-padded with zero
bytes, not hashes. V2 requires `PDT_`, at least one character after the prefix,
uppercase A-Z, digits and underscores, maximum 32 characters. No embedded zero
followed by nonzero text is allowed. Naming remains provisional; future format
changes require an intentional compatible migration.

Disabling a key prohibits future minting/assignment only. Existing tokens retain
it and entitlementKeyOf must still return it. Unity does not use the assignment
approval list as a revocation list. An approved key need not be supported by a
particular Unity build. Key renames require coordinated catalog updates.

## Operations and invariants

- initialize(name, symbol, admin): initializes the proxy exactly once; initially
  grants only PDT_ADMIN to the designated nonzero address.
- setEntitlementKeyApproved(key, approved): ENTITLEMENT_ADMIN only; validates
  encoding and emits EntitlementKeyApprovalChanged(key, approved).
- mint(to, key): MINTER only; safe ERC721 mint, approved key, automatically
  allocated never-used ID. Assignment is established before receiver callbacks.
- entitlementKeyOf(id): returns stored assignment for an existing token, even if
  the key was subsequently disabled; reverts for nonexistent/burned IDs.
- setEntitlement(id, key): ENTITLEMENT_ADMIN only; existing token, approved valid
  key; changes that token only and emits EntitlementChanged(id, oldKey, newKey).
- burn(id): owner or approved operator only. Destroys ownership and assignment;
  the ID allocation counter never rewinds and burned IDs are never reused.
- Standard ERC721 transfers and approvals apply. Ownership carries the current
  entitlement. Metadata is optional presentation data, never unlock authority.
- Multiple tokens may share a key; content unlocks once and stays available if
  at least one currently verified owned token still grants it.
- Authorized entitlement changes may change/remove available content despite
  there being no dedicated revocation feature.

No supply cap or physical scarcity claim is introduced. A mint creates one NFT.
The initial implementation starts token IDs at zero in the new proxy's separate
identity space. This does not reuse IDs from another contract.

## UUPS and governance

Unity approves the stable proxy address, not the implementation address. Only
UPGRADE_ADMIN may authorize upgrades. Every approved upgrade must preserve
ownership, balances, token/operator approvals, roles, entitlement assignments,
key approval configuration and token-ID history. Unity-facing compatibility must
be preserved or deliberately migrated. These are governance and validation
requirements; an authorized malicious upgrade can change contract rules.

Initialize the proxy atomically during deployment, lock the implementation
initializer, and restrict later reinitializers. Validate storage compatibility
and upgrade state preservation before deployment/upgrade. Never use a production
private key or assume a deployment account from a wallet screenshot.

## Unity verification

Reverify on wallet connection, account change, network change, manual Check Again
and before a new protected session/action. No constant polling. Capture wallet,
approved chain, approved proxy and verification generation; discard old-context
results. Verify current owner and entitlement before fresh authorization.

Reject unapproved chain/address combinations even if a copied contract returns a
recognized key. Never fall back to metadata when direct entitlementKeyOf fails.
The legacy HW-001 adapter is isolated from the new scene/path.

Candidates are independent: confirmed nonownership, nonexistent/burned tokens,
invalid keys or unsupported content reject that token, not other proven tokens.
Transport/API timeout means verification unavailable, not confirmed nonownership.
Fresh protected access needs successful verification. Already-running session
behavior during outages remains deferred; explicit new authorization never uses
an old successful result as proof of current ownership.

balanceOf is an advisory completeness check only. Count disagreement, an advisory
read failure, or stale discovery must not override individually proven results.
An incomplete discovery result must not be labelled proof that the wallet owns
no NFTs. Known candidates may be reverified when discovery is temporarily
unavailable, but cached identity never substitutes for current on-chain reads.

## Verification milestone

Implement and test the contract locally, including role isolation, disabled keys,
transfer/burn behavior, invalid encodings, receiver callbacks and upgrades. Test
Unity verification against invalid/stale/partial results. Then deploy to Amoy
using explicitly designated roles and recipient, mint one Dream Mobile NFT,
and verify in Unity. Production deployment is not part of this milestone.
