const fs = require("node:fs");
const path = require("node:path");
const { ethers } = require("ethers");

const root = path.resolve(__dirname, "..");
const deployment = JSON.parse(fs.readFileSync(path.join(root, "deployments", "amoy-80002.json"), "utf8"));
const artifact = relative => JSON.parse(fs.readFileSync(path.join(root, "artifacts", relative), "utf8"));
const tokenArtifact = artifact("src/PDTToken.sol/PDTToken.json");
const proxyArtifact = artifact("@openzeppelin/contracts/proxy/ERC1967/ERC1967Proxy.sol/ERC1967Proxy.json");
const transactionHashes = [
  deployment.implementation.transactionHash,
  deployment.proxy.transactionHash,
  deployment.roleTransactions.MINTER.transactionHash,
  deployment.roleTransactions.ENTITLEMENT_ADMIN.transactionHash,
  deployment.roleTransactions.UPGRADE_ADMIN.transactionHash,
  deployment.entitlement.approvalTransactionHash,
  deployment.mint.transactionHash
];

async function verify(provider) {
  const admin = ethers.getAddress(deployment.admin);
  const implementation = ethers.getAddress(deployment.implementation.address);
  const proxy = ethers.getAddress(deployment.proxy.address);
  const tokenId = BigInt(deployment.mint.tokenId);
  const key = ethers.encodeBytes32String(deployment.entitlement.key);
  const [transactions, receipts, implementationCode, proxyCode] = await Promise.all([
    Promise.all(transactionHashes.map(hash => provider.getTransaction(hash))),
    Promise.all(transactionHashes.map(hash => provider.getTransactionReceipt(hash))),
    provider.getCode(implementation), provider.getCode(proxy)
  ]);
  if (transactions.some(value => !value) || receipts.some(value => !value))
    throw new Error("One or more deployment transactions are unavailable.");

  const tokenInterface = new ethers.Interface(tokenArtifact.abi);
  const initializer = tokenInterface.encodeFunctionData("initialize", [
    deployment.name, deployment.symbol, admin
  ]);
  const expectedProxyCreation = (await new ethers.ContractFactory(
    proxyArtifact.abi, proxyArtifact.bytecode
  ).getDeployTransaction(implementation, initializer)).data;
  const token = new ethers.Contract(proxy, tokenArtifact.abi, provider);
  const roles = ["PDT_ADMIN", "MINTER", "ENTITLEMENT_ADMIN", "UPGRADE_ADMIN"];
  const [owner, entitlement, keyApproved, balance, nextTokenId, name, symbol, ...roleChecks] = await Promise.all([
    token.ownerOf(tokenId), token.entitlementKeyOf(tokenId),
    token.isEntitlementKeyApproved(key), token.balanceOf(admin), token.nextTokenId(),
    token.name(), token.symbol(), ...roles.map(role => token.hasRole(ethers.id(role), admin))
  ]);
  const implementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
  const liveImplementation = ethers.getAddress("0x" + (await provider.getStorage(proxy, implementationSlot)).slice(-40));
  const mintEvent = receipts[6].logs
    .map(log => { try { return tokenInterface.parseLog(log); } catch { return null; } })
    .find(log => log?.name === "Transfer" && log.args.from === ethers.ZeroAddress);

  const checks = {
    allReceiptsSuccessful: receipts.every(receipt => receipt.status === 1),
    recordedBlocksMatch: receipts.every((receipt, index) => receipt.blockNumber === [
      deployment.implementation.blockNumber, deployment.proxy.blockNumber,
      deployment.roleTransactions.MINTER.blockNumber, deployment.roleTransactions.ENTITLEMENT_ADMIN.blockNumber,
      deployment.roleTransactions.UPGRADE_ADMIN.blockNumber, deployment.entitlement.approvalBlockNumber,
      deployment.mint.blockNumber
    ][index]),
    allSendersMatchAdmin: transactions.every(transaction => transaction.from.toLowerCase() === admin.toLowerCase()),
    implementationCreationMatchesArtifact: transactions[0].data.toLowerCase() === tokenArtifact.bytecode.toLowerCase(),
    proxyCreationMatchesArtifactAndInitializer: transactions[1].data.toLowerCase() === expectedProxyCreation.toLowerCase(),
    laterTransactionsTargetProxy: transactions.slice(2).every(transaction => transaction.to.toLowerCase() === proxy.toLowerCase()),
    implementationHasCode: implementationCode !== "0x",
    proxyRuntimeMatchesArtifact: proxyCode.toLowerCase() === proxyArtifact.deployedBytecode.toLowerCase(),
    proxyImplementationMatches: liveImplementation.toLowerCase() === implementation.toLowerCase(),
    ownerMatchesRecipient: owner.toLowerCase() === deployment.recipient.toLowerCase(),
    entitlementMatches: entitlement === key && entitlement === deployment.entitlement.encodedBytes32,
    entitlementIsApproved: keyApproved,
    adminBalanceIsOne: balance === 1n,
    nextTokenIdIsOne: nextTokenId === 1n,
    identityMatches: name === deployment.name && symbol === deployment.symbol,
    allRolesGranted: roleChecks.every(Boolean),
    mintEventMatches: !!mintEvent && mintEvent.args.to.toLowerCase() === deployment.recipient.toLowerCase() &&
      mintEvent.args.tokenId === tokenId
  };
  if (Object.values(checks).some(value => !value))
    throw new Error("One or more Amoy deployment invariants failed: " + JSON.stringify(checks));
  return {
    chainId: (await provider.getNetwork()).chainId.toString(), proxy,
    implementation: liveImplementation, tokenId: tokenId.toString(), owner,
    entitlement: ethers.decodeBytes32String(entitlement), roles: Object.fromEntries(
      roles.map((role, index) => [role, roleChecks[index]])
    ), transactionHashes, checks
  };
}

(async () => {
  const endpoints = [];
  if (process.env.PDT_ALCHEMY_API_KEY)
    endpoints.push("https://polygon-amoy.g.alchemy.com/v2/" + process.env.PDT_ALCHEMY_API_KEY);
  endpoints.push("https://polygon-amoy.drpc.org");
  for (let index = 0; index < endpoints.length; index++) {
    try {
      const result = await verify(new ethers.JsonRpcProvider(endpoints[index], deployment.chainId, { staticNetwork: true }));
      console.log(JSON.stringify(result, null, 2));
      return;
    } catch (error) {
      if (index === endpoints.length - 1) throw error;
      console.warn("Primary verification RPC was unavailable; trying the public fallback.");
    }
  }
})().catch(error => {
  console.error(error.shortMessage || error.reason || error.message);
  process.exit(1);
});
