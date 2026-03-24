using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MasterServerToolkit.MasterServer
{
    public class Transliterator
    {
        private static readonly Dictionary<string, string> CyrToLat = new Dictionary<string, string>
        {

        };

        private static readonly Dictionary<string, string> LatToCyr = new Dictionary<string, string>
        {

        };

        public static string CyrillicToLatin(string input)
        {
            if (string.IsNullOrEmpty(input)) 
                return input;

            var sb = new StringBuilder();

            foreach (char c in input)
            {
                string key = c.ToString();
                sb.Append(CyrToLat.TryGetValue(key, out var value) ? value : c);
            }

            return sb.ToString();
        }

        public static string LatinToCyrillic(string input)
        {
            if (string.IsNullOrEmpty(input)) 
                return input;

            var sb = new StringBuilder();
            int pos = 0;

            while (pos < input.Length)
            {
                bool replaced = false;

                foreach (var key in LatToCyr.Keys.OrderByDescending(k => k.Length))
                {
                    if (pos + key.Length <= input.Length)
                    {
                        string substr = input.Substring(pos, key.Length);

                        if (LatToCyr.TryGetValue(substr, out var value))
                        {
                            sb.Append(value);
                            pos += key.Length;
                            replaced = true;
                            break;
                        }
                    }
                }

                if (!replaced)
                {
                    sb.Append(input[pos++]);
                }
            }

            return sb.ToString();
        }
    }
}