const assert = require("node:assert/strict");
const { ethers, upgrades } = require("hardhat");
const key = text => ethers.hexlify(ethers.zeroPadBytes(ethers.toUtf8Bytes(text), 32));
const DREAM = key("PDT_VEHICLE_DREAM_MOBILE_80TH");
const OTHER = key("PDT_CAR_DREAM_MOBILE_80TH");

async function rejects(promise, name) {
  await assert.rejects(async () => { const result = await promise; if (result?.wait) await result.wait(); },
    error => error.message.includes(name));
}

describe("PDT V2 official entitlements", function () {
  let token, admin, minter, entitlementAdmin, upgrader, alice, bob, operator;
  beforeEach(async () => {
    [admin, minter, entitlementAdmin, upgrader, alice, bob, operator] = await ethers.getSigners();
    const factory = await ethers.getContractFactory("PDTToken");
    token = await upgrades.deployProxy(factory, ["PDT V2", "PDTV2", admin.address], { kind: "uups" });
    await token.waitForDeployment();
    for (const [role, account] of [["MINTER", minter], ["ENTITLEMENT_ADMIN", entitlementAdmin], ["UPGRADE_ADMIN", upgrader]])
      await (await token.grantRole(await token[role](), account.address)).wait();
    await (await token.connect(entitlementAdmin).setEntitlementKeyApproved(DREAM, true)).wait();
  });

  it("locks implementation and proxy initialization; rejects zero admin", async () => {
    await rejects(token.initialize("again", "X", bob.address), "InvalidInitialization");
    const implementation = await upgrades.erc1967.getImplementationAddress(await token.getAddress());
    await rejects(token.attach(implementation).initialize("bad", "B", bob.address), "InvalidInitialization");
    await rejects(upgrades.deployProxy(await ethers.getContractFactory("PDTToken"), ["PDT", "P", ethers.ZeroAddress], { kind: "uups" }), "InvalidAdmin");
  });

  it("separates operational permissions from administration", async () => {
    await rejects(token.mint(alice.address, DREAM), "AccessControlUnauthorizedAccount");
    await rejects(token.setEntitlementKeyApproved(OTHER, true), "AccessControlUnauthorizedAccount");
    await rejects(token.connect(minter).grantRole(await token.UPGRADE_ADMIN(), minter.address), "AccessControlUnauthorizedAccount");
    await rejects(token.connect(entitlementAdmin).mint(alice.address, DREAM), "AccessControlUnauthorizedAccount");
    await rejects(token.connect(upgrader).setEntitlementKeyApproved(OTHER, true), "AccessControlUnauthorizedAccount");
    assert.equal(await token.hasRole(ethers.ZeroHash, admin.address), false);
    assert.equal(await token.getRoleAdmin(await token.PDT_ADMIN()), await token.PDT_ADMIN());
  });

  it("mints only approved keys, to valid recipients, with unique IDs", async () => {
    await rejects(token.connect(minter).mint(alice.address, OTHER), "EntitlementKeyNotApproved");
    await rejects(token.connect(minter).mint(ethers.ZeroAddress, DREAM), "ERC721InvalidReceiver");
    assert.equal(await token.nextTokenId(), 0n);
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(minter).mint(alice.address, DREAM);
    assert.equal(await token.ownerOf(0), alice.address);
    assert.equal(await token.entitlementKeyOf(0), DREAM);
    assert.equal(await token.entitlementKeyOf(1), DREAM);
    assert.equal(await token.balanceOf(alice.address), 2n);
    assert.equal(await token.nextTokenId(), 2n);
    assert.equal(await token.supportsInterface(ethers.id("entitlementKeyOf(uint256)").slice(0, 10)), true);
  });

  it("rejects malformed ASCII/padding but accepts exactly 32 characters", async () => {
    const invalid = [ethers.ZeroHash, key("PDT_"), key("pdt_CAR"), key("PDT_Car"), key("PDT_CAR-1"),
      key("BAD_CAR"), key("PDT_A\0B"), key("PDT_é"), ethers.id("PDT_VEHICLE_DREAM_MOBILE_80TH")];
    for (const value of invalid)
      await rejects(token.connect(entitlementAdmin).setEntitlementKeyApproved(value, true), "InvalidEntitlementKey");
    const full = key("PDT_" + "A".repeat(28));
    await token.connect(entitlementAdmin).setEntitlementKeyApproved(full, true);
    await token.connect(minter).mint(alice.address, full);
    assert.equal(await token.entitlementKeyOf(0), full);
  });

  it("disabling a key prevents new assignments, preserving existing rights", async () => {
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(entitlementAdmin).setEntitlementKeyApproved(DREAM, false);
    assert.equal(await token.entitlementKeyOf(0), DREAM);
    await rejects(token.connect(minter).mint(bob.address, DREAM), "EntitlementKeyNotApproved");
    await rejects(token.connect(entitlementAdmin).setEntitlement(0, DREAM), "EntitlementKeyNotApproved");
    await token.connect(entitlementAdmin).setEntitlementKeyApproved(DREAM, true);
    await token.connect(minter).mint(bob.address, DREAM);
  });

  it("changes only the selected token and emits old/new keys", async () => {
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(entitlementAdmin).setEntitlementKeyApproved(OTHER, true);
    await rejects(token.connect(minter).setEntitlement(0, OTHER), "AccessControlUnauthorizedAccount");
    await rejects(token.connect(alice).setEntitlement(0, OTHER), "AccessControlUnauthorizedAccount");
    await rejects(token.connect(entitlementAdmin).setEntitlement(99, OTHER), "ERC721NonexistentToken");
    const receipt = await (await token.connect(entitlementAdmin).setEntitlement(0, OTHER)).wait();
    const event = receipt.logs.map(log => { try { return token.interface.parseLog(log); } catch { return null; } })
      .find(log => log?.name === "EntitlementChanged");
    assert.deepEqual(Array.from(event.args), [0n, DREAM, OTHER]);
    assert.equal(await token.entitlementKeyOf(1), DREAM);
  });

  it("transfers the token's entitlement and preserves another shared entitlement", async () => {
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(alice).transferFrom(alice.address, bob.address, 0);
    assert.equal(await token.ownerOf(0), bob.address);
    assert.equal(await token.ownerOf(1), alice.address);
    assert.equal(await token.entitlementKeyOf(0), DREAM);
    assert.equal(await token.entitlementKeyOf(1), DREAM);
  });

  it("allows owner and token/operator approval burns, never admin forced burns or ID reuse", async () => {
    for (let i = 0; i < 3; i++) await token.connect(minter).mint(alice.address, DREAM);
    await rejects(token.burn(0), "ERC721InsufficientApproval");
    await token.connect(alice).burn(0);
    await token.connect(alice).approve(operator.address, 1);
    await token.connect(operator).burn(1);
    await token.connect(alice).setApprovalForAll(operator.address, true);
    await token.connect(operator).burn(2);
    for (let i = 0; i < 3; i++) {
      await rejects(token.ownerOf(i), "ERC721NonexistentToken");
      await rejects(token.entitlementKeyOf(i), "ERC721NonexistentToken");
      await rejects(token.connect(entitlementAdmin).setEntitlement(i, DREAM), "ERC721NonexistentToken");
    }
    await token.connect(minter).mint(bob.address, DREAM);
    assert.equal(await token.ownerOf(3), bob.address);
  });

  it("role revocation stops future minting without revoking minted tokens", async () => {
    await token.connect(minter).mint(alice.address, DREAM);
    await token.revokeRole(await token.MINTER(), minter.address);
    await rejects(token.connect(minter).mint(alice.address, DREAM), "AccessControlUnauthorizedAccount");
    assert.equal(await token.entitlementKeyOf(0), DREAM);
  });

  it("establishes entitlements before safe-mint receiver callbacks, even if receiver burns", async () => {
    const receiver = await (await ethers.getContractFactory("EntitlementReceiverTest")).deploy(true);
    await token.connect(minter).mint(await receiver.getAddress(), DREAM);
    assert.equal(await receiver.observedKey(), DREAM);
    await rejects(token.entitlementKeyOf(0), "ERC721NonexistentToken");
    assert.equal(await token.nextTokenId(), 1n);
  });

  it("validates UUPS upgrades and preserves all existing state and ID history", async () => {
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(minter).mint(alice.address, DREAM);
    await token.connect(minter).mint(bob.address, DREAM);
    await token.connect(bob).burn(2);
    await token.connect(alice).approve(operator.address, 0);
    await token.connect(alice).setApprovalForAll(operator.address, true);
    await token.connect(entitlementAdmin).setEntitlementKeyApproved(DREAM, false);
    const factory = await ethers.getContractFactory("PDTTokenUpgradeTest", upgrader);
    await upgrades.validateUpgrade(await token.getAddress(), factory, { kind: "uups" });
    const impl = await upgrades.prepareUpgrade(await token.getAddress(), factory, { kind: "uups" });
    await rejects(token.upgradeToAndCall(impl, "0x"), "AccessControlUnauthorizedAccount");
    await rejects(token.connect(minter).upgradeToAndCall(impl, "0x"), "AccessControlUnauthorizedAccount");
    const upgraded = await upgrades.upgradeProxy(await token.getAddress(), factory, { kind: "uups" });
    await rejects(upgraded.connect(alice).initializeV2(), "AccessControlUnauthorizedAccount");
    await upgraded.initializeV2();
    await rejects(upgraded.initializeV2(), "InvalidInitialization");
    assert.equal(await upgraded.versionMarker(), 2n);
    assert.equal(await upgraded.ownerOf(0), alice.address);
    assert.equal(await upgraded.balanceOf(alice.address), 2n);
    assert.equal(await upgraded.getApproved(0), operator.address);
    assert.equal(await upgraded.isApprovedForAll(alice.address, operator.address), true);
    assert.equal(await upgraded.entitlementKeyOf(0), DREAM);
    assert.equal(await upgraded.isEntitlementKeyApproved(DREAM), false);
    for (const [role, account] of [["PDT_ADMIN", admin], ["MINTER", minter], ["ENTITLEMENT_ADMIN", entitlementAdmin], ["UPGRADE_ADMIN", upgrader]])
      assert.equal(await upgraded.hasRole(await upgraded[role](), account.address), true);
    assert.equal(await upgraded.nextTokenId(), 3n);
    await rejects(upgraded.ownerOf(2), "ERC721NonexistentToken");
    await upgraded.connect(entitlementAdmin).setEntitlementKeyApproved(DREAM, true);
    await upgraded.connect(minter).mint(alice.address, DREAM);
    assert.equal(await upgraded.ownerOf(3), alice.address);
  });
});
