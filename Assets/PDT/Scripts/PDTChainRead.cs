using System;
using System.Collections;
using System.Numerics;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;

public sealed class PDTReadContext
{
    private readonly ReownWalletConnector wallet;
    private readonly int version;
    public string Address { get; }
    public string Chain { get; }
    public PDTReadContext(ReownWalletConnector wallet, string chain)
    {
        this.wallet = wallet;
        version = wallet != null ? wallet.ContextVersion : -1;
        Address = wallet != null ? wallet.ConnectedAddress : null;
        Chain = chain;
    }
    public bool IsCurrent => wallet != null && wallet.IsConnected && AppKit.IsInitialized &&
        version == wallet.ContextVersion &&
        string.Equals(Address, wallet.ConnectedAddress, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Chain, wallet.ConnectedChain, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Chain, AppKit.NetworkController.ActiveChain?.ChainId, StringComparison.OrdinalIgnoreCase);
}

public enum PDTReadStatus { Pending, Success, Nonexistent, Unavailable, Stale }
public sealed class PDTReadResult<T>
{
    public PDTReadStatus Status;
    public T Value;
}

public static class PDTChainRead
{
    public static IEnumerator Run<T>(Func<Task<T>> begin, PDTReadContext context,
        PDTReadResult<T> result, BigInteger? tokenId = null)
    {
        if (!context.IsCurrent) { result.Status = PDTReadStatus.Stale; yield break; }
        Task<T> task = null;
        Exception failure = null;
        try { task = begin(); } catch (Exception exception) { failure = exception; }
        if (failure != null) { result.Status = Classify(failure, tokenId); yield break; }
        float deadline = Time.realtimeSinceStartup + 30f;
        while (!task.IsCompleted)
        {
            if (!context.IsCurrent) { result.Status = PDTReadStatus.Stale; Observe(task); yield break; }
            if (Time.realtimeSinceStartup >= deadline) { result.Status = PDTReadStatus.Unavailable; Observe(task); yield break; }
            yield return null;
        }
        if (!context.IsCurrent) { result.Status = PDTReadStatus.Stale; Observe(task); yield break; }
        if (task.IsCanceled) { result.Status = PDTReadStatus.Unavailable; yield break; }
        if (task.IsFaulted) { result.Status = Classify(task.Exception.GetBaseException(), tokenId); yield break; }
        result.Value = task.Result;
        result.Status = PDTReadStatus.Success;
    }

    private static void Observe<T>(Task<T> task)
    {
        // Discard late results but observe faults; no Unity API on this thread.
        _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static PDTReadStatus Classify(Exception error, BigInteger? tokenId)
    {
        string data = null;
        // Nethereum versions expose structured revert data through these APIs.
        // Do not infer a burn from an arbitrary human-readable error message.
        if (error is Nethereum.JsonRpc.Client.RpcResponseException rpc)
            data = rpc.RpcError?.Data?.ToString();
        if (data == null)
            data = error.GetType().GetProperty("ExceptionEncodedData")?.GetValue(error) as string;
        return tokenId.HasValue && PDTVerificationPolicy.IsNonexistentToken(data, tokenId.Value)
            ? PDTReadStatus.Nonexistent : PDTReadStatus.Unavailable;
    }
}
