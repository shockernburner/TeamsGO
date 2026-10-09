using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectFossil.Core
{
    // Things waiting to be sent, kept wherever the caller stores them (load/save), oldest first. The stored list is
    // read again before and after every send, so anything added while a send is under way is kept and sent too.
    // A send that throws stops the drain and leaves that item first in line for next time.
    public sealed class SendQueue<T>
    {
        private readonly Func<List<T>> _load;
        private readonly Action<List<T>> _save;
        private readonly int _max;

        public SendQueue(Func<List<T>> load, Action<List<T>> save, int max = 50)
        {
            _load = load;
            _save = save;
            _max = max;
        }

        public bool Sending { get; private set; }
        public int Count => _load().Count;

        public void Add(T item)
        {
            var list = _load();
            list.Add(item);
            if (list.Count > _max) list.RemoveRange(0, list.Count - _max);
            _save(list);
        }

        // Sends until the list is empty. Returns how many went; a drain already under way returns 0 at once.
        public async Task<int> Drain(Func<T, Task> send)
        {
            if (Sending) return 0;
            Sending = true;
            int sent = 0;
            try
            {
                while (true)
                {
                    var list = _load();
                    if (list.Count == 0) return sent;
                    await send(list[0]);
                    list = _load(); // re-read: something may have been added meanwhile
                    list.RemoveAt(0);
                    _save(list);
                    sent++;
                }
            }
            finally { Sending = false; }
        }
    }
}
