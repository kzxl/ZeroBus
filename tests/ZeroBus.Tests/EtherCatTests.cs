using System;
using System.Threading.Tasks;
using Xunit;
using ZeroBus.EtherCat;

namespace ZeroBus.Tests
{
    public class EtherCatTests
    {
        [Fact]
        public void EtherCatDatagram_SerializesAndDeserializesAccurately()
        {
            byte[] payload = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            var original = new EtherCatDatagram(
                command: EtherCatConstants.CmdLrw,
                index: 5,
                address: 0x00010000,
                data: payload
            )
            {
                WorkingCounter = 3,
                MoreDatagrams = true
            };

            Span<byte> buffer = stackalloc byte[64];
            int written = original.Serialize(buffer);
            Assert.Equal(10 + payload.Length + 2, written);

            var decoded = EtherCatDatagram.Deserialize(buffer.Slice(0, written), out int consumed);
            Assert.Equal(written, consumed);
            Assert.Equal(original.Command, decoded.Command);
            Assert.Equal(original.Index, decoded.Index);
            Assert.Equal(original.Address, decoded.Address);
            Assert.Equal(original.Data, decoded.Data);
            Assert.Equal(original.WorkingCounter, decoded.WorkingCounter);
            Assert.Equal(original.MoreDatagrams, decoded.MoreDatagrams);
        }

        [Fact]
        public async Task EtherCatMaster_DiscoversSlavesAndTransitionsStates()
        {
            using var transport = new VirtualEtherCatTransport
            {
                SimulatedSlaveCount = 4
            };
            await transport.ConnectAsync();

            var master = new EtherCatMaster(transport);

            // 1. Scan Slaves
            int count = await master.ScanSlavesAsync();
            Assert.Equal(4, count);

            // 2. ESM State Transitions: Init -> PreOp -> SafeOp -> Op
            await master.TransitionStateAsync(EtherCatState.PreOp);
            Assert.Equal(EtherCatState.PreOp, master.MasterState);
            Assert.Equal(EtherCatState.PreOp, await master.ReadAlStateAsync());

            await master.TransitionStateAsync(EtherCatState.SafeOp);
            Assert.Equal(EtherCatState.SafeOp, master.MasterState);
            Assert.Equal(EtherCatState.SafeOp, await master.ReadAlStateAsync());

            await master.TransitionStateAsync(EtherCatState.Op);
            Assert.Equal(EtherCatState.Op, master.MasterState);
            Assert.Equal(EtherCatState.Op, await master.ReadAlStateAsync());

            // 3. Cyclic PDO Exchange (LRW)
            byte[] tx = new byte[16];
            byte[] rx = new byte[16];
            tx[0] = 0xAA;

            ushort wkc = await master.ExchangeProcessDataAsync(tx, rx);
            Assert.Equal(3, wkc);
        }
    }
}
