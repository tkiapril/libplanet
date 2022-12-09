using System.Threading.Tasks;
using Xunit;

namespace Libplanet.Explorer.Tests.Indexing;

public class RocksDbBlockChainIndexTest: BlockChainIndexTest
{
    public RocksDbBlockChainIndexTest()
    {
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
    }

    protected sealed override IBlockChainIndexFixture<SimpleAction> Fx { get; set; }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetBlockHashesMultiByteIndex(bool offsetPresent, bool limitPresent, bool desc)
    {

        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), byte.MaxValue + 2, 1, 1);
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
        await GetBlockHashes(offsetPresent, limitPresent, desc);
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetBlockHashesByMinerMultiByteIndex(
        bool offsetPresent, bool limitPresent, bool desc)
    {
        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), byte.MaxValue + 2, 1, 1);
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
        await GetBlockHashesByMiner(offsetPresent, limitPresent, desc);
    }

    [Fact]
    public async Task TipMultiByteIndex()
    {
        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), byte.MaxValue + 2, 1, 1);
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
        var tip = await Fx.Index.GetTipAsync();
        Assert.Equal(tip, Fx.Index.Tip);
        Assert.Equal(ChainFx.Chain.Tip.Hash, tip.Hash);
        Assert.Equal(ChainFx.Chain.Tip.Index, tip.Index);
    }

    [Fact]
    public async Task GetLastNonceByAddressMultiByteIndex()
    {
        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), 2, byte.MaxValue + 2, 1);
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
        await GetLastNonceByAddress();
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetSignedTxIdsByAddressMultiByteIndex(
        bool offsetPresent, bool limitPresent, bool desc)
    {
        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), 2, byte.MaxValue + 2, 1);
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
        await GetSignedTxIdsByAddress(offsetPresent, limitPresent, desc);
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetInvolvedTxIdsByAddressMultiByteIndex(bool offsetPresent, bool limitPresent, bool desc)
    {
        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), 2, byte.MaxValue + 2, 1);
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
        await GetInvolvedTxIdsByAddress(offsetPresent, limitPresent, desc);
    }
}
