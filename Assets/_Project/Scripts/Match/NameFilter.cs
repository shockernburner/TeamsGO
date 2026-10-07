using System.Text;

namespace ProjectFossil.Match
{
    // Keeps slurs and obscenities off name tags and the worldwide leaderboards. Player and crew names pass through it
    // when typed, when a teammate's name reaches the host, and when names come back from the online boards (a hacked
    // copy could post anything there). It sees through simple disguises: case, l33t digits, spacing and stretched
    // letters ("f u u c k", "5h1t"). Short words that hide inside ordinary ones (ass, cock, rape) only count as a whole
    // word, so Classic, Hancock and Grape stay fine.
    public static class NameFilter
    {
        // Blocked anywhere in the name.
        private static readonly string[] Anywhere =
        {
            "fuck", "fuk", "fck", "shit", "cunt", "nigger", "nigga", "niga", "faggot", "fagot", "bitch", "whore",
            "slut", "nazi", "hitler", "retard", "asshole", "motherf", "pussy", "penis", "vagina", "dildo", "porn",
            "kike", "chink", "tranny", "wank", "twat", "bollock", "jizz", "cumshot", "blowjob", "handjob", "kkk",
            "molest",
        };

        // Blocked only as a whole word.
        private static readonly string[] WholeWord =
        {
            "ass", "arse", "cock", "dick", "cum", "fag", "rape", "raped", "rapist", "sex", "tit", "tits", "spic",
            "gook", "homo", "dyke", "piss", "pedo", "paedo", "hoe", "nig", "coon", "wetback", "negro",
        };

        public static bool IsOffensive(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string plain = Normalize(name, keepSpaces: false);
            string squeezed = Squeeze(plain);
            foreach (var bad in Anywhere)
                if (plain.Contains(bad) || squeezed.Contains(bad)) return true;

            foreach (var word in Normalize(name, keepSpaces: true).Split(' '))
            {
                if (word.Length == 0) continue;
                string w2 = Squeeze(word);
                foreach (var bad in WholeWord)
                    if (word == bad || w2 == bad || word == bad + "s" || w2 == bad + "s") return true;
            }
            return false;
        }

        // The name itself, or the stand-in when it is offensive or empty.
        public static string Clean(string name, string fallback) =>
            string.IsNullOrWhiteSpace(name) || IsOffensive(name) ? fallback : name;

        // Lower case, look-alike digits and symbols to letters, separators to spaces (or dropped), everything else dropped.
        private static string Normalize(string s, bool keepSpaces)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char raw in s.ToLowerInvariant())
            {
                char c = raw;
                switch (raw)
                {
                    case '0': c = 'o'; break;
                    case '1': case '!': case '|': c = 'i'; break;
                    case '3': c = 'e'; break;
                    case '4': case '@': c = 'a'; break;
                    case '5': case '$': c = 's'; break;
                    case '7': c = 't'; break;
                    case '8': c = 'b'; break;
                    case '9': c = 'g'; break;
                }
                if (c >= 'a' && c <= 'z') sb.Append(c);
                else if (keepSpaces && (c == ' ' || c == '-' || c == '_' || c == '.')) sb.Append(' ');
            }
            return sb.ToString();
        }

        // "fuuuck" -> "fuck": runs of one letter become one.
        private static string Squeeze(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (sb.Length == 0 || sb[sb.Length - 1] != c) sb.Append(c);
            return sb.ToString();
        }
    }
}
