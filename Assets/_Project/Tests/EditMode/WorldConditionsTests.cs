using NUnit.Framework;
using ProjectFossil.Core;

namespace ProjectFossil.Tests.EditMode
{
    public class WorldConditionsTests
    {
        [Test]
        public void Resolve_IsDeterministicForASeed()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var a = WorldConditions.Resolve(seed, DayTime.Random, Weather.Random);
                var b = WorldConditions.Resolve(seed, DayTime.Random, Weather.Random);
                Assert.AreEqual(a.Time, b.Time);
                Assert.AreEqual(a.Weather, b.Weather);
                Assert.AreEqual(a.WindDegrees, b.WindDegrees);
            }
        }

        [Test]
        public void Resolve_KeepsSpecificChoices_AndNeverReturnsRandom()
        {
            for (int seed = 0; seed < 500; seed++)
            {
                var chosen = WorldConditions.Resolve(seed, DayTime.Night, Weather.Fog);
                Assert.AreEqual(DayTime.Night, chosen.Time);
                Assert.AreEqual(Weather.Fog, chosen.Weather);

                var rolled = WorldConditions.Resolve(seed, DayTime.Random, Weather.Random);
                Assert.AreNotEqual(DayTime.Random, rolled.Time);
                Assert.AreNotEqual(Weather.Random, rolled.Weather);
                Assert.That(rolled.WindDegrees, Is.InRange(0f, 360f));
            }
        }

        [Test]
        public void Resolve_FixingOneChoice_DoesNotShiftTheOther()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var both = WorldConditions.Resolve(seed, DayTime.Random, Weather.Random);
                Assert.AreEqual(both.Weather, WorldConditions.Resolve(seed, DayTime.Dusk, Weather.Random).Weather);
                Assert.AreEqual(both.Time, WorldConditions.Resolve(seed, DayTime.Random, Weather.Storm).Time);
            }
        }

        [Test]
        public void Random_ProducesVariety_WithDayMostLikely()
        {
            var times = new int[5];
            var weathers = new int[6];
            for (int seed = 0; seed < 2000; seed++)
            {
                var c = WorldConditions.Resolve(seed, DayTime.Random, Weather.Random);
                times[(int)c.Time]++;
                weathers[(int)c.Weather]++;
            }
            for (int t = 1; t < times.Length; t++) Assert.Greater(times[t], 0, ((DayTime)t).ToString());
            for (int w = 1; w < weathers.Length; w++) Assert.Greater(weathers[w], 0, ((Weather)w).ToString());
            Assert.Greater(times[(int)DayTime.Day], times[(int)DayTime.Night]);
        }

        [Test]
        public void NightAndFog_HideYou_RainAndStorm_MaskYourSteps()
        {
            var clearDay = new Conditions { Time = DayTime.Day, Weather = Weather.Clear };
            Assert.AreEqual(1f, WorldConditions.Sight(clearDay));
            Assert.AreEqual(1f, WorldConditions.Hearing(clearDay));

            var foggyNight = new Conditions { Time = DayTime.Night, Weather = Weather.Fog };
            Assert.Less(WorldConditions.Sight(foggyNight), WorldConditions.Sight(new Conditions { Time = DayTime.Night, Weather = Weather.Clear }));
            Assert.Less(WorldConditions.Sight(foggyNight), 0.4f);

            Assert.Less(WorldConditions.Hearing(new Conditions { Time = DayTime.Day, Weather = Weather.Storm }),
                        WorldConditions.Hearing(new Conditions { Time = DayTime.Day, Weather = Weather.Rain }));
            Assert.Less(WorldConditions.Hearing(new Conditions { Time = DayTime.Day, Weather = Weather.Rain }), 1f);
        }

        [Test]
        public void Override_IsUsedOnce_ThenChoicesApplyAgain()
        {
            WorldConditions.ChosenTime = DayTime.Day;
            WorldConditions.ChosenWeather = Weather.Clear;
            WorldConditions.Override(DayTime.Night, Weather.Storm, 90f);
            WorldConditions.Begin(7);
            Assert.AreEqual(DayTime.Night, WorldConditions.Current.Time);
            Assert.AreEqual(Weather.Storm, WorldConditions.Current.Weather);

            WorldConditions.Begin(7);
            Assert.AreEqual(DayTime.Day, WorldConditions.Current.Time);
            Assert.AreEqual(Weather.Clear, WorldConditions.Current.Weather);
            WorldConditions.ChosenTime = DayTime.Random;
            WorldConditions.ChosenWeather = Weather.Random;
        }
    }
}
