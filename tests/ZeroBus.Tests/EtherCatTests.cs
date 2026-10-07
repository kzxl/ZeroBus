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

        [Fact]
        public void EtherCatCoeMailbox_BuildsAndParsesSdoFramesAccurately()
        {
            // 1. Build SDO Download: Index 0x6040, Subindex 0, Value 0x000F (2 bytes)
            byte[] val = new byte[] { 0x0F, 0x00 };
            var frame = EtherCatCoeMailbox.BuildSdoDownload(0x6040, 0, val, slaveAddress: 1001, mailboxCounter: 2);

            Assert.Equal(16, frame.Length);
            Assert.Equal(10, frame[0]); // CoE data len = 10
            Assert.Equal(0x03, frame[5] & 0x0F); // CoE mailbox type
            Assert.Equal(EtherCatCoeConstants.SdoCsDownloadExpedited2B, frame[8]); // CS = 0x2B
            Assert.Equal(0x40, frame[9]);
            Assert.Equal(0x60, frame[10]);
            Assert.Equal(0, frame[11]);
            Assert.Equal(0x0F, frame[12]);
            Assert.Equal(0x00, frame[13]);

            // 2. Build SDO Upload: Index 0x607A, Subindex 0
            var uploadFrame = EtherCatCoeMailbox.BuildSdoUpload(0x607A, 0);
            Assert.Equal(EtherCatCoeConstants.SdoCsUploadRequest, uploadFrame[8]); // CS = 0x40
            Assert.Equal(0x7A, uploadFrame[9]);
            Assert.Equal(0x60, uploadFrame[10]);

            // 3. Parse SDO Response
            byte[] responseFrame = new byte[16];
            responseFrame[5] = EtherCatCoeConstants.MailboxTypeCoe;
            ushort coeRespHeader = (ushort)(EtherCatCoeConstants.CoeServiceSdoResponse << 12);
            responseFrame[6] = (byte)(coeRespHeader & 0xFF);
            responseFrame[7] = (byte)((coeRespHeader >> 8) & 0xFF);
            responseFrame[8] = EtherCatCoeConstants.SdoCsDownloadResponse; // 0x60
            responseFrame[9] = 0x40;
            responseFrame[10] = 0x60;
            responseFrame[11] = 0;
            responseFrame[12] = 0xAA;
            responseFrame[13] = 0xBB;

            bool parsed = EtherCatCoeMailbox.TryParseSdoResponse(
                responseFrame,
                out byte cmd,
                out ushort idx,
                out byte sub,
                out byte[] payload
            );

            Assert.True(parsed);
            Assert.Equal(EtherCatCoeConstants.SdoCsDownloadResponse, cmd);
            Assert.Equal(0x6040, idx);
            Assert.Equal(0, sub);
            Assert.Equal(0xAA, payload[0]);
            Assert.Equal(0xBB, payload[1]);
        }
    }
}
