using System;
using Xunit;

namespace IntelOrca.Biohazard.Tests
{
    public class TestPrsFile
    {
        [Fact]
        public void StoreRoundtrip()
        {
            var random = new Random(1);
            foreach (var length in new[] { 0, 1, 2, 6, 7, 8, 9, 15, 16, 17, 1000, 65536 })
            {
                var data = new byte[length];
                random.NextBytes(data);

                var stored = PrsFile.Store(data);
                var back = stored.Uncompressed.ToArray();
                Assert.Equal(data, back);
            }
        }
    }
}
