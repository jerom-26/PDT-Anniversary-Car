require("@nomicfoundation/hardhat-ethers");
require("@openzeppelin/hardhat-upgrades");
const { subtask } = require("hardhat/config");
const { TASK_COMPILE_SOLIDITY_GET_SOLC_BUILD } = require("hardhat/builtin-tasks/task-names");

// Use the lockfile-pinned local compiler; no compiler download during tests.
subtask(TASK_COMPILE_SOLIDITY_GET_SOLC_BUILD).setAction(async ({ solcVersion }, hre, runSuper) => {
  if (solcVersion !== "0.8.24") return runSuper();
  return { compilerPath: require.resolve("solc/soljson.js"), isSolcJs: true,
    version: solcVersion, longVersion: require("solc").version() };
});

module.exports = {
  solidity: { version: "0.8.24", settings: { optimizer: { enabled: true, runs: 200 }, evmVersion: "paris" } },
  paths: { sources: "./src", tests: "./test", cache: "./cache", artifacts: "./artifacts" },
  networks: { hardhat: { chainId: 31337 } },
  mocha: { timeout: 60000 }
};
