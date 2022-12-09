using System;
using Libplanet.Blockchain;
using Nito.AsyncEx;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An exception raised when a method or a property on an <see cref="IBlockChainIndex"/> is called
/// before it is populated.
/// </summary>
public class IndexNotReadyException : InvalidOperationException
{
    internal IndexNotReadyException()
        : base(GetMessage())
    {
    }

    private static string GetMessage()
    {
        var blockChainTypeName = typeof(BlockChain<>).GetGenericTypeDefinition().Name;
        blockChainTypeName = blockChainTypeName[
            ..blockChainTypeName.IndexOf('`', StringComparison.Ordinal)];

        var prepareIndexServiceTypeName =
            typeof(PrepareIndexService<>).GetGenericTypeDefinition().Name;
        prepareIndexServiceTypeName =
            prepareIndexServiceTypeName[
                ..prepareIndexServiceTypeName.IndexOf("`", StringComparison.Ordinal)];

        return "The index is not initialized yet. Please add the {prepareIndexServiceTypeName}<T>"
               + " service to your service collection and make your other services that alter the"
               + $" {blockChainTypeName}<T> state wait for the {nameof(AsyncManualResetEvent)}"
               + $" that was provided to the {prepareIndexServiceTypeName}<T> instance.";
    }
}
