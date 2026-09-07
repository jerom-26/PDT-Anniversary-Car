using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text;
using Reown.AppKit.Unity;
using UnityEngine;

static class Test
{
    const string Owner = "0x1111111111111111111111111111111111111111";
    const string Other = "0x2222222222222222222222222222222222222222";
    const string Proxy = "0x3333333333333333333333333333333333333333";
    static int passed;
    static void Equal<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Expected {b}, got {a}"); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    static void Enable(object target) => target.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    static byte[] Key(string name = EntitlementKeys.DreamMobile80th) { var bytes = new byte[32]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0); return bytes; }
    static TokenReference Token(string id, string collection = Proxy, string chain = "eip155:80002") => new(chain, collection, id);
    static void Run(string name, Action test)
    {
        Scheduler.Pending.Clear(); Scheduler.Tick = null; Time.realtimeSinceStartup = 0;
        AppKit.NetworkController.ActiveChain.ChainId = "eip155:80002";
        test(); passed++; Console.WriteLine("PASS " + name);
    }
    static Fixture Setup(params TokenReference[] tokens)
    {
        var f = new Fixture();
        f.Discovery.Tokens = tokens;
        Set(f.Source, "proxyAddress", Proxy);
        Set(f.Reader, "walletConnector", f.Wallet); Set(f.Reader, "approvedSource", f.Source);
        Set(f.Reader, "tokenDiscoveryServiceSource", f.Discovery);
        Set(f.Entitlement, "walletConnector", f.Wallet); Set(f.Entitlement, "approvedSource", f.Source);
        var vehicle = new VehicleData(); Set(vehicle, "entitlementKey", EntitlementKeys.DreamMobile80th);
        var catalog = new VehicleCatalog(); Set(catalog, "vehicles", new[] { vehicle }); Set(f.Registry, "vehicleCatalog", catalog);
        Set(f.Coordinator, "ownershipReader", f.Reader); Set(f.Coordinator, "tokenEntitlementServiceSource", f.Entitlement);
        Set(f.Coordinator, "ownedVehicleRegistry", f.Registry); Set(f.Coordinator, "vehicleSpawner", f.Spawner);
        Enable(f.Reader); Enable(f.Coordinator);
        AppKit.Evm.Read = (address, method, args) => method switch {
            "balanceOf" => new BigInteger(tokens.Length), "ownerOf" => Owner, "entitlementKeyOf" => Key(), _ => throw new Exception("Unexpected method") };
        return f;
    }
    public static void Main()
    {
        Run("duplicate candidates and entitlements unlock once", () => {
            var f = Setup(Token("0"), Token("00"), Token("1")); f.Go();
            Equal(f.Reader.VerifiedTokens.Count, 2); Equal(f.Registry.UnlockedVehicles.Count, 1); Equal(f.Spawner.Spawned != null, true);
        });
        Run("wrong chain and copied contract candidates are never read", () => {
            var f = Setup(Token("0", Other), Token("1", Proxy, "eip155:1")); int reads = 0;
            AppKit.Evm.Read = (a, m, args) => { if (m != "balanceOf") reads++; return BigInteger.Zero; }; f.Go(); Equal(reads, 0); Equal(f.Registry.UnlockedVehicles.Count, 0);
        });
        Run("zero balance is advisory and cannot defeat proven token", () => {
            var f = Setup(Token("0")); var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => m == "balanceOf" ? BigInteger.Zero : read(a, m, args);
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 1); Equal(f.Reader.HasUnavailableResults, true);
        });
        Run("burned token and timeout do not defeat valid NFT", () => {
            var f = Setup(Token("0"), Token("1"), Token("2")); var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => {
                if (m == "ownerOf" && (BigInteger)args[0] == 0) throw new Nethereum.JsonRpc.Client.RpcResponseException("0x7e273289" + new string('0', 64));
                if (m == "ownerOf" && (BigInteger)args[0] == 1) return new TaskCompletionSource<string>().Task;
                return read(a, m, args);
            }; f.Go(); Equal(f.Reader.VerifiedTokens.Count, 1); Equal(f.Registry.UnlockedVehicles.Count, 1); Equal(f.Reader.HasUnavailableResults, true);
        });
        Run("transfer during entitlement lookup fails final ownership check", () => {
            var f = Setup(Token("0")); int owners = 0; var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => m == "ownerOf" ? (++owners == 1 ? Owner : Other) : read(a, m, args);
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 0);
        });
        Run("unsupported key grants nothing with no metadata request", () => {
            var f = Setup(Token("0")); var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => m == "entitlementKeyOf" ? Key("PDT_UNKNOWN") : read(a, m, args);
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 0);
        });
        Run("entitlement timeout grants no fresh access and reports unavailable", () => {
            var f = Setup(Token("0")); var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => m == "entitlementKeyOf" ? new TaskCompletionSource<byte[]>().Task : read(a, m, args);
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 0); Equal(f.Coordinator.HasUnavailableResults, true);
        });
        Run("account change discards a pending old-wallet result", () => {
            var f = Setup(Token("0")); var pending = new TaskCompletionSource<string>(); var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => m == "ownerOf" ? pending.Task : read(a, m, args);
            Scheduler.Tick = () => { if (Time.realtimeSinceStartup != 2) return; f.Wallet.Change(Other, "eip155:80002"); pending.SetResult(Owner); };
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 0);
        });
        Run("unsupported network invalidates pending authorization", () => {
            var f = Setup(Token("0")); Scheduler.Tick = () => { if (Time.realtimeSinceStartup == 1) f.Wallet.Change(Owner, "eip155:1"); };
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 0); Equal(f.Reader.IsScanning, false);
        });
        Run("disconnect clears spawned content and cannot resurrect it", () => {
            var f = Setup(Token("0")); f.Go(); Equal(f.Spawner.Spawned != null, true);
            f.Wallet.Change(null, "eip155:80002"); Scheduler.Drain(); Equal(f.Spawner.Spawned == null, true); Equal(f.Registry.UnlockedVehicles.Count, 0);
        });
        Run("discovery outage can use cached identity but must reprove ownership", () => {
            var f = Setup(Token("0")); f.Go(); f.Discovery.Unavailable = true; var read = AppKit.Evm.Read;
            AppKit.Evm.Read = (a, m, args) => m == "ownerOf" ? Other : read(a, m, args);
            f.Go(); Equal(f.Registry.UnlockedVehicles.Count, 0); Equal(f.Reader.HasUnavailableResults, true);
        });
        Run("strict bytes32, uint256 bounds and exact nonexistent error", () => {
            Equal(ReownEntitlementKeyService.TryDecodeBytes32(Key(), out _), true);
            Equal(ReownEntitlementKeyService.TryDecodeBytes32(Key("PDT_A\0B"), out _), false);
            Equal(ReownEntitlementKeyService.TryDecodeBytes32(Key("PDT_"), out _), false);
            Equal(PDTVerificationPolicy.TryTokenId((BigInteger.One << 256).ToString(), out _), false);
            Equal(PDTVerificationPolicy.IsNonexistentToken("network timeout", 0), false);
            Equal(PDTVerificationPolicy.IsNonexistentToken("0x7e273289" + new string('0', 64), 1), false);
        });
        Console.WriteLine($"{passed} Unity flow tests passed (engine/transport doubles; real PDT code).");
    }
    sealed class Fixture
    {
        public ReownWalletConnector Wallet = new() { ConnectedAddress = Owner };
        public ApprovedPDTCollection Source = new(); public Discovery Discovery = new();
        public ERC721OwnershipReader Reader = new(); public ReownEntitlementKeyService Entitlement = new();
        public VerifiedVehicleUnlockCoordinator Coordinator = new(); public OwnedVehicleRegistry Registry = new(); public VehicleSpawner Spawner = new();
        public void Go() { Coordinator.BeginProtectedSession(); Scheduler.Drain(); }
    }
    sealed class Discovery : MonoBehaviour, ITokenDiscoveryService
    {
        public TokenReference[] Tokens; public bool Unavailable;
        public IEnumerator DiscoverOwnedTokens(string owner, string chain, string collection, Action<IReadOnlyList<TokenReference>> found, Action<string> error)
        { if (Unavailable) error("timeout"); else found(Tokens); yield break; }
    }
}
