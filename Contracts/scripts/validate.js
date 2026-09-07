const { ethers, upgrades } = require("hardhat");
async function main() {
  await upgrades.validateImplementation(await ethers.getContractFactory("PDTToken"), { kind: "uups" });
  console.log("PDTToken UUPS implementation validation passed.");
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
