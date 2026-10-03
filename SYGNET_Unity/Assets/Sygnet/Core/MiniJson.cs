using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sygnet.Core
{
    /// <summary>
    /// Minimalny parser JSON dla rdzenia bez UnityEngine (JsonUtility) i bez zewnętrznych pakietów.
    /// Wynik: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, long, double, bool, null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("Nadmiarowe dane po wartości JSON");
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string s) => this.s = s ?? throw new ArgumentNullException(nameof(s));
            public bool End => i >= s.Length;

            public FormatException Error(string msg) => new FormatException(msg + " (pozycja " + i + ")");

            public void SkipWs()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++;
            }

            public object ReadValue()
            {
                SkipWs();
                if (End) throw Error("Nieoczekiwany koniec JSON");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Nieoczekiwany znak '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("Oczekiwano " + word);
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; // {
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Error("Oczekiwano klucza");
                    var key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw Error("Oczekiwano ':'");
                    i++;
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw Error("Niezamknięty obiekt");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("Oczekiwano ',' lub '}'");
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                i++; // [
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("Niezamknięta tablica");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Error("Oczekiwano ',' lub ']'");
                }
            }

            string ReadString()
            {
                i++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("Niezamknięty string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Niezamknięty string");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("Ucięte \\u");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw Error("Nieznana sekwencja \\" + e);
                    }
                }
            }

            object ReadNumber()
            {
                int start = i;
                bool isFloat = false;
                if (s[i] == '-') i++;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c >= '0' && c <= '9') { i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; i++; continue; }
                    break;
                }
                var t = s.Substring(start, i - start);
                if (!isFloat && long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
                    return l;
                return double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
