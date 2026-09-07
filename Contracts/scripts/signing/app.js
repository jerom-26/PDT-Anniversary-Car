/* global ethers */
"use strict";
const labels = ["Deploy locked implementation", "Deploy and initialize stable UUPS proxy", "Grant MINTER", "Grant ENTITLEMENT_ADMIN", "Grant UPGRADE_ADMIN", "Approve Dream Mobile entitlement", "Mint one Dream Mobile NFT"];
const status = document.querySelector("#status");
const next = document.querySelector("#next");
let manifest, provider, signer, wallet, state;
const storageKey = "pdt-v2-amoy-deployment-2026-09";
const amoyPriorityFeeFloor = ethers.parseUnits("30", "gwei");
const setStatus = value => { status.textContent = value; };
function describeError(error) {
  const values = [
    error?.shortMessage,
    error?.reason,
    error?.info?.error?.message,
    error?.error?.message,
    error?.data?.message,
    error?.message
  ].filter(Boolean);
  return [...new Set(values)].join(" — ") || "Unknown wallet/provider error.";
}
function draw() {
  document.querySelector("#steps").replaceChildren(...labels.map((label, index) => {
    const li = document.createElement("li"); li.textContent = (state.receipts[index] ? "✓ " : "○ ") + label; return li;
  }));
  document.querySelector("#details").textContent = JSON.stringify(state, null, 2);
}
async function save() {
  localStorage.setItem(storageKey, JSON.stringify(state)); draw();
  await fetch("/status", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(state) });
}
async function correctContext() {
  const accounts = await wallet.request({ method: "eth_accounts" });
  const chain = await wallet.request({ method: "eth_chainId" });
  if (Number(chain) !== manifest.chainId) throw new Error("Select Polygon Amoy (80002) in MetaMask.");
  if (accounts[0]?.toLowerCase() !== manifest.admin.toLowerCase()) throw new Error("Select the designated PDT admin account in MetaMask.");
}
async function amoyFeeOverrides(extra = {}) {
  const [feeData, latestBlock] = await Promise.all([
    provider.getFeeData(),
    provider.getBlock("latest")
  ]);
  const quotedPriority = feeData.maxPriorityFeePerGas || 0n;
  const maxPriorityFeePerGas = quotedPriority > amoyPriorityFeeFloor
    ? quotedPriority : amoyPriorityFeeFloor;
  const quotedMaxFee = feeData.maxFeePerGas || feeData.gasPrice || 0n;
  const baseFee = latestBlock?.baseFeePerGas || 0n;
  const baseAwareMaxFee = (baseFee * 2n) + maxPriorityFeePerGas;
  const maxFeePerGas = quotedMaxFee > baseAwareMaxFee
    ? quotedMaxFee : baseAwareMaxFee;
  state.latestFeePolicy = {
    priorityFloorGwei: "30",
    maxPriorityFeeGwei: ethers.formatUnits(maxPriorityFeePerGas, "gwei"),
    maxFeeGwei: ethers.formatUnits(maxFeePerGas, "gwei"),
    blockNumber: latestBlock?.number ?? null
  };
  return { ...extra, maxPriorityFeePerGas, maxFeePerGas };
}
async function verify() {
  await correctContext();
  const token = new ethers.Contract(state.proxy, manifest.token.abi, provider);
  const entitlementKey = ethers.encodeBytes32String(manifest.entitlement);
  const roles = ["PDT_ADMIN", "MINTER", "ENTITLEMENT_ADMIN", "UPGRADE_ADMIN"];
  const [owner, entitlement, keyApproved, contractName, contractSymbol, code, ...roleChecks] = await Promise.all([
    token.ownerOf(state.tokenId), token.entitlementKeyOf(state.tokenId),
    token.isEntitlementKeyApproved(entitlementKey), token.name(), token.symbol(),
    provider.getCode(state.proxy),
    ...roles.map(role => token.hasRole(ethers.id(role), manifest.admin))
  ]);
  const implementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
  const implementationWord = await provider.getStorage(state.proxy, implementationSlot);
  const liveImplementation = ethers.getAddress("0x" + implementationWord.slice(-40));
  if (owner.toLowerCase() !== manifest.recipient.toLowerCase() || entitlement !== entitlementKey ||
      !keyApproved || contractName !== manifest.name || contractSymbol !== manifest.symbol || code === "0x" ||
      liveImplementation.toLowerCase() !== state.implementation.toLowerCase() || roleChecks.some(value => !value))
    throw new Error("On-chain deployment verification failed.");
  state.verified = {
    owner, entitlement, chainId: manifest.chainId, proxy: state.proxy,
    implementation: liveImplementation, roles: Object.fromEntries(roles.map((role, index) => [role, roleChecks[index]])),
    entitlementKeyApproved: keyApproved, name: contractName, symbol: contractSymbol
  };
  await save(); setStatus("Verified on Amoy: one NFT minted with the direct Dream Mobile entitlement."); next.disabled = true;
}
async function runNext() {
  next.disabled = true;
  try {
    await correctContext();
    const i = labels.findIndex((_, index) => !state.receipts[index]);
    if (i < 0) { await verify(); return; }
    setStatus(labels[i] + ": review the transaction in MetaMask.");
    let hash = state.transactions[i];
    if (!hash) {
      let tx;
      if (i === 0) {
        const instance = await new ethers.ContractFactory(manifest.token.abi, manifest.token.bytecode, signer)
          .deploy(await amoyFeeOverrides());
        tx = instance.deploymentTransaction();
      } else if (i === 1) {
        if (!ethers.isAddress(state.implementation)) throw new Error("Missing implementation receipt.");
        const init = new ethers.Interface(manifest.token.abi).encodeFunctionData("initialize", [manifest.name, manifest.symbol, manifest.admin]);
        // The exact payload uses about 389,843 gas on Amoy. Supplying a 30%
        // buffer avoids a transient wallet-RPC eth_estimateGas failure; unused
        // gas is not charged.
        const instance = await new ethers.ContractFactory(manifest.proxy.abi, manifest.proxy.bytecode, signer)
          .deploy(state.implementation, init, await amoyFeeOverrides({ gasLimit: 506795n }));
        tx = instance.deploymentTransaction();
      } else {
        const token = new ethers.Contract(state.proxy, manifest.token.abi, signer);
        const fees = await amoyFeeOverrides();
        if (i >= 2 && i <= 4) tx = await token.grantRole(ethers.id(["MINTER", "ENTITLEMENT_ADMIN", "UPGRADE_ADMIN"][i - 2]), manifest.admin, fees);
        if (i === 5) tx = await token.setEntitlementKeyApproved(ethers.encodeBytes32String(manifest.entitlement), true, fees);
        if (i === 6) tx = await token.mint(manifest.recipient, ethers.encodeBytes32String(manifest.entitlement), fees);
      }
      hash = tx.hash; state.transactions[i] = hash; await save();
    }
    setStatus("Waiting for Amoy confirmation: " + hash);
    const receipt = await provider.waitForTransaction(hash, 1, 180000);
    if (!receipt || receipt.status !== 1) {
      state.transactions[i] = null;
      await save();
      throw new Error("Transaction was not confirmed successfully. Inspect its hash before retrying.");
    }
    if (i === 0) state.implementation = receipt.contractAddress;
    if (i === 1) state.proxy = receipt.contractAddress;
    if (i === 6) {
      const iface = new ethers.Interface(manifest.token.abi);
      const transfer = receipt.logs.filter(log => log.address.toLowerCase() === state.proxy.toLowerCase())
        .map(log => { try { return iface.parseLog(log); } catch { return null; } })
        .find(log => log?.name === "Transfer" && log.args.from === ethers.ZeroAddress);
      if (!transfer) throw new Error("Mint receipt contains no mint Transfer event.");
      state.tokenId = transfer.args.tokenId.toString();
    }
    state.receipts[i] = { hash, blockNumber: receipt.blockNumber };
    state.lastError = null;
    await save();
    if (i === 6) await verify(); else setStatus("Confirmed. Prepare the next transaction when ready.");
  } catch (error) {
    const step = labels.findIndex((_, index) => !state.receipts[index]);
    const message = describeError(error);
    state.lastError = { step: step < 0 ? "verification" : step + 1, message, code: error?.code || null };
    try { await save(); } catch { /* Keep the original wallet/provider error visible. */ }
    setStatus(message);
  }
  finally { next.disabled = !signer || !!state.verified; }
}
document.querySelector("#connect").onclick = async () => {
  try {
    wallet = window.ethereum?.providers?.find(p => p.isMetaMask) || window.ethereum;
    if (!wallet) throw new Error("Open this local page in the browser where MetaMask is installed.");
    await wallet.request({ method: "eth_requestAccounts" });
    await correctContext(); provider = new ethers.BrowserProvider(wallet); signer = await provider.getSigner();
    const balance = await provider.getBalance(manifest.admin);
    state.walletBalancePOL = ethers.formatEther(balance);
    await save();
    if (balance === 0n) throw new Error("This Amoy account has no test POL for transaction fees.");
    next.disabled = false; setStatus("Connected to the designated Amoy account. Ready for the next step.");
    if (state.receipts.length === labels.length) await verify();
  } catch (error) { setStatus(describeError(error)); }
};
next.onclick = runNext;
(async () => {
  manifest = await (await fetch("/manifest")).json();
  state = JSON.parse(localStorage.getItem(storageKey) || "null") || { transactions: [], receipts: [], admin: manifest.admin, recipient: manifest.recipient };
  const config = document.querySelector("#config");
  for (const [name, value] of Object.entries({ Network: "Polygon Amoy · 80002", Admin: manifest.admin, Recipient: manifest.recipient, Name: manifest.name, Entitlement: manifest.entitlement })) {
    const dt = document.createElement("dt"), dd = document.createElement("dd"); dt.textContent = name; dd.textContent = value; config.append(dt, dd);
  }
  await save(); setStatus("Connect MetaMask to begin. No transaction is sent until you choose the next step and sign.");
})().catch(error => setStatus(error.message));
