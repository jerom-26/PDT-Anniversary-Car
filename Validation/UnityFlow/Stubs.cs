// These replace engine/transport APIs, not PDT's implementation. Tests execute
// the actual repository readers, coordinator, key validator and registry.
using System.Collections;
namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class ScriptableObject : Object {}
    public class Coroutine { public IEnumerator Routine; public bool Stopped; }
    public class MonoBehaviour : Object
    {
        public Coroutine StartCoroutine(IEnumerator routine) { var c = new Coroutine { Routine = routine }; Scheduler.Pending.Add(c); return c; }
        public void StopCoroutine(Coroutine coroutine) => coroutine.Stopped = true;
    }
    public static class Scheduler
    {
        public static readonly List<Coroutine> Pending = new();
        public static Action Tick;
        public static void Drain()
        {
            int budget = 10000;
            while (Pending.Count > 0)
            {
                var c = Pending[0]; Pending.RemoveAt(0);
                var stack = new Stack<IEnumerator>(); stack.Push(c.Routine);
                while (!c.Stopped && stack.Count > 0)
                {
                    if (--budget == 0) throw new Exception("Coroutine did not terminate");
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    if (top.Current is IEnumerator nested) stack.Push(nested);
                    else { Time.realtimeSinceStartup += 1; Tick?.Invoke(); }
                }
            }
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug
    {
        public static bool isDebugBuild = true;
        public static void Log(object value) {}
        public static void LogWarning(object value) {}
        public static void LogError(object value) {}
    }
    public class SerializeField : Attribute {}
    public class HeaderAttribute : Attribute { public HeaderAttribute(string value) {} }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string value) {} }
    public class DisallowMultipleComponent : Attribute {}
    public class CreateAssetMenuAttribute : Attribute { public string fileName; public string menuName; }
}
namespace UnityEngine.Serialization { public class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string value) {} } }
namespace Nethereum.JsonRpc.Client
{
    public class RpcError { public object Data; }
    public class RpcResponseException : Exception { public RpcError RpcError; public RpcResponseException(string data) { RpcError = new RpcError { Data = data }; } }
}
namespace Reown.AppKit.Unity
{
    public static class AppKit
    {
        public static bool IsInitialized = true;
        public static Network NetworkController = new();
        public static EvmReader Evm = new();
    }
    public class Network { public Chain ActiveChain = new(); }
    public class Chain { public string ChainId = "eip155:80002"; }
    public class EvmReader
    {
        public Func<string, string, object[], object> Read;
        public Task<T> ReadContractAsync<T>(string address, string abi, string method, object[] args)
        {
            try { var result = Read(address, method, args); return result is Task<T> task ? task : Task.FromResult((T)result); }
            catch (Exception error) { return Task.FromException<T>(error); }
        }
    }
}
public class ReownWalletConnector
{
    public string ConnectedAddress;
    public string ConnectedChain = "eip155:80002";
    public int ContextVersion;
    public bool IsConnected => ConnectedAddress != null;
    public event Action WalletContextChanged;
    public void Change(string address, string chain)
    {
        ConnectedAddress = address; ConnectedChain = chain; ContextVersion++;
        Reown.AppKit.Unity.AppKit.NetworkController.ActiveChain.ChainId = chain;
        WalletContextChanged?.Invoke();
    }
}
public class VehicleSpawner
{
    public VehicleData Spawned;
    public bool TrySpawn(VehicleData vehicle, OwnedVehicleRegistry authorization)
    {
        if (vehicle == null || authorization == null || !authorization.IsUnlocked(vehicle))
            return false;
        Spawned = vehicle;
        return true;
    }
    public void Despawn() => Spawned = null;
}
