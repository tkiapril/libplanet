#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Execution;
using Libplanet.Action;
using Libplanet.Explorer.Interfaces;
using Libplanet.Explorer.Queries;
using Libplanet.Tx;
using Xunit;
using static Libplanet.Explorer.Tests.GraphQLTestUtils;

namespace Libplanet.Explorer.Tests.Queries;

public class TransactionQueryGeneratedTest
{
    protected readonly GeneratedBlockChainFixture Fx;
    protected MockBlockChainContext<PolymorphicAction<SimpleAction>> Source;
    protected TransactionQuery<PolymorphicAction<SimpleAction>> QueryGraph;

    public TransactionQueryGeneratedTest()
    {
        Fx = new GeneratedBlockChainFixture(
            new System.Random().Next(),
            txActionsForSuffixBlocks:
            ImmutableArray<ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>>
                .Empty
                .Add(ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>
                    .Empty
                    .Add(ImmutableArray<PolymorphicAction<SimpleAction>>
                        .Empty
                        .Add(new SimpleAction0())))
                .Add(ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>
                    .Empty
                    .Add(ImmutableArray<PolymorphicAction<SimpleAction>>
                        .Empty
                        .Add(new SimpleAction0Fail()))));
        Source = new MockBlockChainContext<PolymorphicAction<SimpleAction>>(Fx.Chain);
        var _ = new ExplorerQuery<PolymorphicAction<SimpleAction>>(Source);
        QueryGraph = new TransactionQuery<PolymorphicAction<SimpleAction>>(Source);
    }

    [Fact]
    public async Task TransactionResult()
    {
        var failBlock = Fx.Chain.Tip;
        var failTx = failBlock.Transactions.First();
        var successBlock =
            Fx.Chain.Store.GetBlock<PolymorphicAction<SimpleAction>>(failBlock.PreviousHash!.Value);
        var successTx = successBlock.Transactions.First();
        var pk = Fx.PrivateKeys[0];
        var stagingTx = Transaction<PolymorphicAction<SimpleAction>>.Create(
            Fx.Chain.GetNextTxNonce(pk.ToAddress()),
            pk,
            Fx.Chain.Genesis.Hash,
            ImmutableArray<PolymorphicAction<SimpleAction>>.Empty.Add(new SimpleAction1()));
        Fx.Chain.StageTransaction(stagingTx);

        var queryResult = await ExecuteTransactionResultQueryAsync(successTx.Id);
        Assert.Equal("SUCCESS", queryResult.TxStatus);
        Assert.Equal(successBlock.Index, queryResult.BlockIndex);
        Assert.Equal(successBlock.Hash.ToString(), queryResult.BlockHash);
        Assert.Null(queryResult.ExceptionName);
        queryResult = await ExecuteTransactionResultQueryAsync(failTx.Id);
        Assert.Equal("FAILURE", queryResult.TxStatus);
        Assert.Equal(failBlock.Index, queryResult.BlockIndex);
        Assert.Equal(failBlock.Hash.ToString(), queryResult.BlockHash);
        Assert.Equal(
            "Libplanet.Action.CurrencyPermissionException",
            queryResult.ExceptionName);
        queryResult = await ExecuteTransactionResultQueryAsync(new TxId());
        Assert.Equal("INVALID", queryResult.TxStatus);
        Assert.Null(queryResult.BlockIndex);
        Assert.Null(queryResult.BlockHash);
        Assert.Null(queryResult.ExceptionName);
        queryResult = await ExecuteTransactionResultQueryAsync(stagingTx.Id);
        Assert.Equal("STAGING", queryResult.TxStatus);
        Assert.Null(queryResult.BlockIndex);
        Assert.Null(queryResult.BlockHash);
        Assert.Null(queryResult.ExceptionName);
    }

    [Fact]
    public async Task Transactions()
    {
        var allTxs
            = Fx.Chain.IterateBlocks().SelectMany(block => block.Transactions).ToImmutableArray();
        await AssertTransactionsQueryPermutation(allTxs, null, null);
        foreach (var signer in Fx.PrivateKeys.Select(pk => pk.ToAddress()))
        {
            var signedTxs = allTxs
                .Where(tx => tx.Signer == signer)
                .ToImmutableArray();
            await AssertTransactionsQueryPermutation(signedTxs, signer, null);
            var involvedTxs = allTxs
                .Where(tx => tx.UpdatedAddresses.Contains(signer))
                .ToImmutableArray();
            await AssertTransactionsQueryPermutation(involvedTxs, signer, null);
            foreach (var involved in Fx.PrivateKeys.Select(pk => pk.ToAddress()))
            {
                var signedAndInvolvedTxs = signedTxs
                    .Where(tx => tx.UpdatedAddresses.Contains(signer))
                    .ToImmutableArray();
                await AssertTransactionsQueryPermutation(signedAndInvolvedTxs, signer, involved);
            }
        }
    }

    private async Task AssertTransactionsQueryPermutation(
        ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>> txsToTest,
        Address? signer,
        Address? involvedAddress)
    {
        await AssertAgainstTransactionsQuery(
            txsToTest, signer, involvedAddress, false, null, null);
        var expected = txsToTest.Reverse().ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, true, null, null);
        expected = txsToTest.Skip(txsToTest.Length / 4).ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, false, txsToTest.Length / 4, null);
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, false, -1 * (txsToTest.Length / 4 * 3), null);
        expected = expected.Reverse().ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, true, txsToTest.Length / 4, null);
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, true, -1 * (txsToTest.Length / 4 * 3), null);
        expected = txsToTest.Take(txsToTest.Length / 4).ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, false, null, txsToTest.Length / 4);
        expected = txsToTest.Reverse().Take(txsToTest.Length / 4).ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected, signer, involvedAddress, true, null, txsToTest.Length / 4);
        expected = txsToTest
            .Skip(txsToTest.Length / 3)
            .Take(txsToTest.Length / 4)
            .ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected,
            signer,
            involvedAddress,
            false,
            txsToTest.Length / 3,
            txsToTest.Length / 4);
        await AssertAgainstTransactionsQuery(
            expected,
            signer,
            involvedAddress,
            false,
            -1 * (txsToTest.Length / 4 * 3),
            txsToTest.Length / 4);
        expected = txsToTest
            .Reverse()
            .Skip(txsToTest.Length / 3)
            .Take(txsToTest.Length / 4)
            .ToImmutableArray();
        await AssertAgainstTransactionsQuery(
            expected,
            signer,
            involvedAddress,
            true,
            txsToTest.Length / 3,
            txsToTest.Length / 4);
        await AssertAgainstTransactionsQuery(
            expected,
            signer,
            involvedAddress,
            true,
            -1 * (txsToTest.Length / 4 * 3),
            txsToTest.Length / 4);
    }

    private async Task AssertAgainstTransactionsQuery(
        IReadOnlyList<Transaction<PolymorphicAction<SimpleAction>>> expected,
        Address? signer,
        Address? involvedAddress,
        bool desc,
        int? offset,
        int? limit)
    {
        var actual =
            await ExecuteTransactionsQueryAsync(signer, involvedAddress, desc, offset, limit);
        foreach (var i in Enumerable.Range(0, actual.Length))
        {
            Assert.Equal(expected[i].Id.ToHex(), actual[i].Id);
            if (Source.Index is not null)
            {
                Assert.Equal(
                    (
                        await Source.Index.GetContainedBlockHashByTxIdAsync(expected[i].Id)
                            .ConfigureAwait(false))
                    .ToString(),
                    actual[i].BlockHash);
            }
        }
    }

    private async Task<ImmutableArray<(string Id, string? BlockHash)>>
        ExecuteTransactionsQueryAsync(
            Address? signer,
            Address? involvedAddress,
            bool desc,
            int? offset,
            int? limit)
    {
        ExecutionResult result = await ExecuteQueryAsync(@$"
        {{
            transactions(
                {(signer is { } signerVal ? @$"signer: ""{signerVal}""" : "")}
                {(involvedAddress is { } invVal ? @$"involvedAddress: ""{invVal}""" : "")}
                desc: {(desc ? "true" : "false")},
                offset: {offset ?? 0}
                {(limit is { } limitVal ? $"limit: {limitVal}" : "")}
            )
            {{
                id
                {(Source.Index is not null ? "blockRef { hash }" : "")}
            }}
         }}
        ", QueryGraph, source: Source);
        Assert.Null(result.Errors);
        ExecutionNode resultData = Assert.IsAssignableFrom<ExecutionNode>(result.Data);
        IDictionary<string, object> resultDict =
            Assert.IsAssignableFrom<IDictionary<string, object>>(resultData.ToValue());
        return ((IReadOnlyList<object>)resultDict["transactions"])
            .Select(txData =>
            (
                (string)((IDictionary<string, object>)txData)["id"],
                (string?)(Source.Index is not null
                    ? ((IDictionary<string, object>)
                        ((IDictionary<string, object>)txData)["blockRef"]
                    )["hash"]
                    : null)))
            .ToImmutableArray();
    }

    private async Task<
            (string TxStatus, long? BlockIndex, string? BlockHash, string? ExceptionName)>
        ExecuteTransactionResultQueryAsync(TxId txId)
    {
        ExecutionResult result = await ExecuteQueryAsync(@$"
        {{
            transactionResult(txId: ""{txId.ToHex()}"")
            {{
                txStatus
                blockIndex
                blockHash
                exceptionName
            }}
         }}
        ", QueryGraph, source: Source);
        Assert.Null(result.Errors);
        ExecutionNode resultData = Assert.IsAssignableFrom<ExecutionNode>(result.Data);
        IDictionary<string, object> resultDict =
            (IDictionary<string, object>)Assert.IsAssignableFrom<IDictionary<string, object>>(
                resultData.ToValue())["transactionResult"];
        return (
            (string)resultDict["txStatus"],
            (long?)resultDict["blockIndex"],
            (string?)resultDict["blockHash"],
            (string?)resultDict["exceptionName"]);
    }
}
