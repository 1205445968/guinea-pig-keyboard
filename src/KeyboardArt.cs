using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace WheekKeys
{
    internal static class KeyboardArt
    {
        private sealed class Key
        {
            public int Scan;
            public string Text;
            public bool Right;
            public Key(int scan, string text, bool right)
            {
                Scan = scan;
                Text = text;
                Right = right;
            }
        }

        private static readonly Key[] Keys = CreateKeys();
        private static readonly Dictionary<int, Key> Lookup = IndexKeys();

        private static Key[] CreateKeys()
        {
            List<Key> keys = new List<Key>();
            Stream data = Assembly.GetExecutingAssembly().GetManifestResourceStream("keyboard-layout.txt");
            if (data == null) throw new InvalidDataException("缺少 keyboard-layout.txt 资源。");
            using (data)
            using (StreamReader reader = new StreamReader(data))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] fields = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length != 7) throw new InvalidDataException("keyboard-layout.txt 第 " + lineNumber + " 行列数无效。");
                    try
                    {
                        int scan = int.Parse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                        bool right;
                        if (fields[3] == "R") right = true;
                        else if (fields[3] == "L") right = false;
                        else throw new FormatException("手别必须是 L 或 R。");
                        keys.Add(new Key(scan, fields[2], right));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("keyboard-layout.txt 第 " + lineNumber + " 行格式无效。", ex);
                    }
                }
            }
            if (keys.Count == 0) throw new InvalidDataException("keyboard-layout.txt 中没有键位数据。");
            return keys.ToArray();
        }

        private static Dictionary<int, Key> IndexKeys()
        {
            Dictionary<int, Key> keys = new Dictionary<int, Key>();
            foreach (Key key in Keys) keys.Add(key.Scan, key);
            keys.Add(0x11c, keys[28]);
            return keys;
        }

        public static bool IsRight(int scan)
        {
            Key key;
            if (scan != 0 && Lookup.TryGetValue(scan, out key)) return key.Right;
            int make = scan & 0xff;
            if (make >= 0x47 && make <= 0x53) return true;
            switch (scan)
            {
                case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14:
                case 21: case 22: case 23: case 24: case 25: case 26: case 27: case 43:
                case 35: case 36: case 37: case 38: case 39: case 40: case 28:
                case 49: case 50: case 51: case 52: case 53: case 54:
                case 0x37: case 0x45: case 0x46: case 0x135: case 0x137:
                case 0x11c: case 0x11d: case 0x138: case 0x15c: case 0x15d:
                    return true;
                default: return false;
            }
        }

        public static string Label(int scan)
        {
            Key key;
            if (scan != 0 && Lookup.TryGetValue(scan, out key)) return key.Text;
            if (scan >= 0x3b && scan <= 0x44) return "F" + (scan - 0x3a);
            switch (scan)
            {
                case 0x57: return "F11";
                case 0x58: return "F12";
                case 0x147: return "Home";
                case 0x148: return "↑";
                case 0x149: return "PgUp";
                case 0x14b: return "←";
                case 0x14d: return "→";
                case 0x14f: return "End";
                case 0x150: return "↓";
                case 0x151: return "PgDn";
                case 0x152: return "Ins";
                case 0x153: return "Del";
                case 0x11c: return "Enter";
            }
            return "";
        }

    }
}
