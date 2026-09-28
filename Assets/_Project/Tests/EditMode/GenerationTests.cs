using NUnit.Framework;
using ProjectFossil.Core;

namespace ProjectFossil.Tests.EditMode
{
    public class GenerationTests
    {
        [Test]
        public void RNGService_SameSeed_ProducesSameSequence()
        {
            var a = new RNGService(42);
            var b = new RNGService(42);
            Assert.AreEqual(a.NextFloat(), b.NextFloat());
            Assert.AreEqual(a.Next(100), b.Next(100));
        }

        [Test]
        public void RNGService_DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new RNGService(1);
            var b = new RNGService(2);
            Assert.AreNotEqual(a.NextFloat(), b.NextFloat());
        }
    }
}
