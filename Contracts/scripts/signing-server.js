// Local-only MetaMask workflow. This server never holds a signer/private key and
// never broadcasts transactions. All signing happens in the user's wallet.
const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const { ethers } = require("ethers");
const root = path.resolve(__dirname, "..");
const address = ethers.getAddress("0xd679d38406570ff5cfa05d6857963e5baa329bcb");
const artifact = relative => JSON.parse(fs.readFileSync(path.join(root, "artifacts", relative), "utf8"));
const token = artifact("src/PDTToken.sol/PDTToken.json");
const proxy = artifact("@openzeppelin/contracts/proxy/ERC1967/ERC1967Proxy.sol/ERC1967Proxy.json");
const manifest = {
  chainId: 80002, admin: address, recipient: address,
  name: "PDT V2 Amoy", symbol: "PDTV2", entitlement: "PDT_VEHICLE_DREAM_MOBILE_80TH",
  token: { abi: token.abi, bytecode: token.bytecode },
  proxy: { abi: proxy.abi, bytecode: proxy.bytecode }
};
let progress = null;
const server = http.createServer((request, response) => {
  if (request.headers.host !== "127.0.0.1:8787") { response.writeHead(403); response.end(); return; }
  response.setHeader("Cache-Control", "no-store");
  response.setHeader("X-Content-Type-Options", "nosniff");
  response.setHeader("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'");
  const routes = {
    "/": ["text/html", path.join(__dirname, "signing", "index.html")],
    "/app.js": ["text/javascript", path.join(__dirname, "signing", "app.js")],
    "/style.css": ["text/css", path.join(__dirname, "signing", "style.css")],
    "/ethers.js": ["text/javascript", path.join(root, "node_modules/ethers/dist/ethers.umd.min.js")]
  };
  if (request.method === "GET" && request.url === "/manifest") {
    response.setHeader("Content-Type", "application/json"); response.end(JSON.stringify(manifest)); return;
  }
  if (request.method === "GET" && request.url === "/status") {
    response.setHeader("Content-Type", "application/json"); response.end(JSON.stringify(progress)); return;
  }
  if (request.method === "POST" && request.url === "/status" && request.headers.origin === "http://127.0.0.1:8787") {
    let body = "";
    request.on("data", chunk => { body += chunk; if (body.length > 32768) request.destroy(); });
    request.on("end", () => {
      try { progress = JSON.parse(body); response.end("ok"); }
      catch { response.writeHead(400); response.end(); }
    }); return;
  }
  const route = routes[request.url];
  if (request.method !== "GET" || !route) { response.writeHead(404); response.end(); return; }
  response.setHeader("Content-Type", route[0]); fs.createReadStream(route[1]).pipe(response);
});
server.listen(8787, "127.0.0.1", () => console.log("PDT Amoy signing workflow: http://127.0.0.1:8787"));
