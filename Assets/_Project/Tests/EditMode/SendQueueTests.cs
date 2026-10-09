using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectFossil.Core;

namespace ProjectFossil.Tests.EditMode
{
    // The online results queue: a match end queues the player's run and then the crew's match while the run is
    // still being sent. Saving a list read before the crew's arrived dropped it, and the crew boards stayed empty.
    public class SendQueueTests
    {
        private static (SendQueue<string> queue, Func<List<string>> stored) Make()
        {
            var store = new List<string>();
            var q = new SendQueue<string>(() => new List<string>(store), l => { store.Clear(); store.AddRange(l); });
            return (q, () => new List<string>(store));
        }

        [Test]
        public void ItemsAddedDuringASend_AreKeptAndSent()
        {
            var (q, stored) = Make();
            var sent = new List<string>();
            q.Add("run");
            var gate = new TaskCompletionSource<bool>();
            var drain = q.Drain(async item => { sent.Add(item); if (item == "run") await gate.Task; });

            q.Add("crew");                // queued while the run is in flight
            Assert.AreEqual(0, q.Drain(_ => Task.CompletedTask).Result, "a second drain waits for the first");
            gate.SetResult(true);
            drain.Wait();

            CollectionAssert.AreEqual(new[] { "run", "crew" }, sent);
            Assert.AreEqual(0, stored().Count);
        }

        [Test]
        public void AFailedSend_KeepsTheItemFirstInLine()
        {
            var (q, stored) = Make();
            q.Add("a"); q.Add("b");
            Assert.Throws<AggregateException>(() => q.Drain(item => throw new InvalidOperationException("offline")).Wait());
            CollectionAssert.AreEqual(new[] { "a", "b" }, stored());
            Assert.IsFalse(q.Sending);
        }

        [Test]
        public void TheQueueKeepsOnlyTheNewest()
        {
            var store = new List<int>();
            var q = new SendQueue<int>(() => new List<int>(store), l => { store.Clear(); store.AddRange(l); }, max: 3);
            for (int i = 0; i < 5; i++) q.Add(i);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, store);
        }
    }
}
