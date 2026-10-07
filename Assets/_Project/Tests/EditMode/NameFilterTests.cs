using NUnit.Framework;
using ProjectFossil.Match;

namespace ProjectFossil.Tests.EditMode
{
    public class NameFilterTests
    {
        [TestCase("Kestrel")]
        [TestCase("Night Owls")]
        [TestCase("Quiet Talons")]
        [TestCase("Classic Crew")]
        [TestCase("Hancock")]
        [TestCase("Grape Juice")]
        [TestCase("Therapist")]
        [TestCase("Assassins")]
        [TestCase("Dickens")]
        [TestCase("Rex 2")]
        [TestCase("Torpedo")]
        [TestCase("Crisis Team")]
        public void OrdinaryNames_Pass(string name) => Assert.IsFalse(NameFilter.IsOffensive(name), name);

        [TestCase("fuck")]
        [TestCase("F U C K")]
        [TestCase("fuuuck crew")]
        [TestCase("5h1t")]
        [TestCase("xXbitchXx")]
        [TestCase("big ass")]
        [TestCase("Rape Squad")]
        [TestCase("n1gg3r")]
        [TestCase("c0ck")]
        public void Slurs_AndDisguisedOnes_AreCaught(string name) => Assert.IsTrue(NameFilter.IsOffensive(name), name);

        [Test]
        public void Clean_SwapsOffensiveOrEmptyNames_ForTheStandIn()
        {
            Assert.AreEqual("Survivor", NameFilter.Clean("sh1t", "Survivor"));
            Assert.AreEqual("Survivor", NameFilter.Clean("  ", "Survivor"));
            Assert.AreEqual("Vega", NameFilter.Clean("Vega", "Survivor"));
        }
    }
}
