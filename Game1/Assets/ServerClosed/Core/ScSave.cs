using System;
using System.Collections.Generic;
namespace ServerClosed.Core
{
        public static class ScJson
        {
            public static string ToJson(object obj) { var sb = new System.Text.StringBuilder(); WriteValue(sb, obj); return sb.ToString(); }

            public static T FromJson<T>(string json) where T : new()
            {
                int pos = 0;
                var v = ParseValue(json, ref pos);
                var res = new T();
                if (!(v is Dictionary<string, object>)) throw new FormatException("JSON root must be an object.");
                SkipWs(json, ref pos);
                if (pos != json.Length) throw new FormatException("Trailing JSON content.");
                Assign(res, v);
                return res;
            }

            // --- запись ---
            static void WriteValue(System.Text.StringBuilder sb, object v)
            {
                if (v == null) { sb.Append("null"); return; }
                if (v is string) { WriteString(sb, (string)v); return; }
                if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
                if (v is double) { sb.Append(((double)v).ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                if (v is float) { sb.Append(((float)v).ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                if (v is int) { sb.Append(((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                if (v is long) { sb.Append(((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                var dict = v as System.Collections.IDictionary;
                if (dict != null)
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (System.Collections.DictionaryEntry e in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, (string)e.Key);
                        sb.Append(':');
                        WriteValue(sb, e.Value);
                    }
                    sb.Append('}');
                    return;
                }
                var list = v as System.Collections.IEnumerable;
                if (list != null)
                {
                    sb.Append('[');
                    bool first2 = true;
                    foreach (var it in list)
                    {
                        if (!first2) sb.Append(',');
                        first2 = false;
                        WriteValue(sb, it);
                    }
                    sb.Append(']');
                    return;
                }
                // POCO: публичные поля (кроме [NonSerialized] — рантайм-состояние) + свойства get;set;
                sb.Append('{');
                var t = v.GetType();
                bool first3 = true;
                foreach (var f in t.GetFields())
                {
                    if (f.IsInitOnly || f.IsStatic) continue;
                    if (f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    if (!first3) sb.Append(',');
                    first3 = false;
                    WriteString(sb, f.Name);
                    sb.Append(':');
                    WriteValue(sb, f.GetValue(v));
                }
                foreach (var pr in t.GetProperties())
                {
                    if (!pr.CanRead || !pr.CanWrite || pr.GetIndexParameters().Length > 0) continue;
                    if (first3) { } else sb.Append(',');
                    first3 = false;
                    WriteString(sb, pr.Name);
                    sb.Append(':');
                    WriteValue(sb, pr.GetValue(v, null));
                }
                sb.Append('}');
            }

            static void WriteString(System.Text.StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }

            // --- разбор ---
            static object ParseValue(string s, ref int pos)
            {
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new FormatException("Unexpected end of JSON.");
                char c = s[pos];
                if (c == '{') return ParseObj(s, ref pos);
                if (c == '[') return ParseArr(s, ref pos);
                if (c == '"') return ParseStr(s, ref pos);
                if (c == 't' && s.Substring(pos).StartsWith("true")) { pos += 4; return (object)true; }
                if (c == 'f' && s.Substring(pos).StartsWith("false")) { pos += 5; return (object)false; }
                if (c == 'n' && s.Substring(pos).StartsWith("null")) { pos += 4; return null; }
                return ParseNum(s, ref pos);
            }

            static void SkipWs(string s, ref int pos) { while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\n' || s[pos] == '\r' || s[pos] == '\t')) pos++; }

            static Dictionary<string, object> ParseObj(string s, ref int pos)
            {
                var res = new Dictionary<string, object>();
                pos++; // {
                SkipWs(s, ref pos);
                if (pos < s.Length && s[pos] == '}') { pos++; return res; }
                while (pos < s.Length)
                {
                    SkipWs(s, ref pos);
                    if (pos >= s.Length || s[pos] != '"') throw new FormatException("Expected object key.");
                    string key = ParseStr(s, ref pos);
                    SkipWs(s, ref pos);
                    if (pos >= s.Length || s[pos] != ':') throw new FormatException("Expected colon.");
                    pos++; // :
                    object v = ParseValue(s, ref pos);
                    res[key] = v;
                    SkipWs(s, ref pos);
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    if (pos < s.Length && s[pos] == '}') { pos++; return res; }
                    throw new FormatException("Unterminated object.");
                }
                throw new FormatException("Unterminated object.");
            }

            static List<object> ParseArr(string s, ref int pos)
            {
                var res = new List<object>();
                pos++; // [
                SkipWs(s, ref pos);
                if (pos < s.Length && s[pos] == ']') { pos++; return res; }
                while (pos < s.Length)
                {
                    object v = ParseValue(s, ref pos);
                    res.Add(v);
                    SkipWs(s, ref pos);
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    if (pos < s.Length && s[pos] == ']') { pos++; return res; }
                    throw new FormatException("Unterminated array.");
                }
                throw new FormatException("Unterminated array.");
            }

            static string ParseStr(string s, ref int pos)
            {
                var sb = new System.Text.StringBuilder();
                pos++; // "
                while (pos < s.Length && s[pos] != '"')
                {
                    char c = s[pos];
                    if (c == '\\')
                    {
                        pos++;
                        char e = s[pos];
                        if (e == 'n') sb.Append('\n');
                        else if (e == 'r') sb.Append('\r');
                        else if (e == 't') sb.Append('\t');
                        else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(pos + 1, 4), 16)); pos += 4; }
                        else sb.Append(e);
                    }
                    else sb.Append(c);
                    pos++;
                }
                if (pos >= s.Length) throw new FormatException("Unterminated string.");
                pos++; // "
                return sb.ToString();
            }

            static object ParseNum(string s, ref int pos)
            {
                int start = pos;
                while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '-' || s[pos] == '+' || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E')) pos++;
                string num = s.Substring(start, pos - start);
                if (num.Contains(".") || num.Contains("e") || num.Contains("E"))
                    return double.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
                return long.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
            }

            // --- присвоение разобранного в POCO ---
            public static void Assign(object target, object src)
            {
                var dict = src as Dictionary<string, object>;
                if (dict == null) return;
                var t = target.GetType();
                foreach (var kv in dict)
                {
                    var f = t.GetField(kv.Key);
                    if (f != null && !f.IsStatic && !f.IsInitOnly && !f.IsDefined(typeof(NonSerializedAttribute), false)) { SetMember(f.FieldType, f.GetValue(target), f, null, target, kv.Value); continue; }
                    var pr = t.GetProperty(kv.Key);
                    if (pr != null && pr.CanWrite && pr.GetIndexParameters().Length == 0)
                        SetMember(pr.PropertyType, pr.GetValue(target, null), null, pr, target, kv.Value);
                }
            }

            static void SetMember(Type mt, object curVal, System.Reflection.FieldInfo f, System.Reflection.PropertyInfo pr, object target, object src)
            {
                if (src == null) { return; }
                try
                {
                    if (mt == typeof(string)) { Set(target, f, pr, (string)src); return; }
                    if (mt == typeof(bool)) { Set(target, f, pr, src is bool ? (bool)src : Convert.ToBoolean(src)); return; }
                    if (mt == typeof(int)) { Set(target, f, pr, src is int ? (int)src : (int)Convert.ToDouble(src, System.Globalization.CultureInfo.InvariantCulture)); return; }
                    if (mt == typeof(long)) { Set(target, f, pr, Convert.ToInt64(src)); return; }
                    if (mt == typeof(double)) { Set(target, f, pr, src is double ? (double)src : Convert.ToDouble(src, System.Globalization.CultureInfo.InvariantCulture)); return; }
                    if (mt == typeof(float)) { Set(target, f, pr, (float)Convert.ToDouble(src, System.Globalization.CultureInfo.InvariantCulture)); return; }
                    if (mt == typeof(double[])) { var arr = ToDoubleArr(src); Set(target, f, pr, arr); return; }
                    if (mt == typeof(string[])) { Set(target, f, pr, ToStrArr(src)); return; }
                    if (mt == typeof(List<List<int>>)) { var rows = new List<List<int>>(); foreach (var row in (List<object>)src) { var ints = new List<int>(); foreach (var n in (List<object>)row) ints.Add(Convert.ToInt32(n)); rows.Add(ints); } Set(target, f, pr, rows); return; }
                    if (mt == typeof(List<int>)) { var l = new List<int>(); foreach (var v in (List<object>)src) l.Add(v is int ? (int)v : (int)Convert.ToDouble(v)); Set(target, f, pr, l); return; }
                    if (mt == typeof(List<string>)) { var l = new List<string>(); foreach (var v in (List<object>)src) l.Add((string)v); Set(target, f, pr, l); return; }
                    if (mt == typeof(List<Deco>)) { SetList<Deco>(src, target, f, pr); return; }
                    if (mt == typeof(List<Poi>)) { SetList<Poi>(src, target, f, pr); return; }
                    if (mt == typeof(List<Creature>)) { SetList<Creature>(src, target, f, pr); return; }
                    if (mt == typeof(List<HistoryEntry>)) { SetList<HistoryEntry>(src, target, f, pr); return; }
                    if (mt == typeof(List<KarmaEvent>)) { SetList<KarmaEvent>(src, target, f, pr); return; }
                    if (mt == typeof(List<MemoryFragment>)) { SetList<MemoryFragment>(src, target, f, pr); return; }
                    if (mt == typeof(List<Achievement>)) { SetList<Achievement>(src, target, f, pr); return; }
                    if (mt == typeof(List<PastLifeRecord>)) { SetList<PastLifeRecord>(src, target, f, pr); return; }
                    if (mt == typeof(List<RegionOdds>)) { SetList<RegionOdds>(src, target, f, pr); return; }
                    if (mt == typeof(List<DungeonFloor>)) { SetList<DungeonFloor>(src, target, f, pr); return; }
                    if (mt == typeof(List<DungeonCache>)) { SetList<DungeonCache>(src, target, f, pr); return; }
                    if (mt == typeof(Dictionary<string, DungeonState>)) { var d2 = new Dictionary<string, DungeonState>(); foreach (var kv in (Dictionary<string, object>)src) { var ds = new DungeonState(); Assign(ds, kv.Value); d2[kv.Key] = ds; } Set(target, f, pr, d2); return; }
                    // вложенный POCO
                    if (curVal != null && !(src is List<object>) && !(src is Dictionary<string, object>)) { Assign(curVal, src); return; }
                    var inst = Activator.CreateInstance(mt);
                    Assign(inst, src);
                    Set(target, f, pr, inst);
                }
                catch (Exception e) { throw new FormatException("Invalid save field: " + (f != null ? f.Name : pr.Name), e); }
            }

            static void SetList<T>(object src, object target, System.Reflection.FieldInfo f, System.Reflection.PropertyInfo pr) where T : new()
            {
                var l = new List<T>();
                foreach (var v in (List<object>)src) { var it = new T(); Assign(it, v); l.Add(it); }
                Set(target, f, pr, l);
            }

            static void Set(object target, System.Reflection.FieldInfo f, System.Reflection.PropertyInfo pr, object v)
            {
                if (f != null) f.SetValue(target, v);
                else if (pr != null) pr.SetValue(target, v, null);
            }

            static double[] ToDoubleArr(object src)
            {
                var l = (List<object>)src;
                var arr = new double[l.Count];
                for (int i = 0; i < l.Count; i++) arr[i] = l[i] is double ? (double)l[i] : Convert.ToDouble(l[i], System.Globalization.CultureInfo.InvariantCulture);
                return arr;
            }

            static string[] ToStrArr(object src)
            {
                var l = (List<object>)src;
                var arr = new string[l.Count];
                for (int i = 0; i < l.Count; i++) arr[i] = l[i] as string;
                return arr;
            }
        }
}
