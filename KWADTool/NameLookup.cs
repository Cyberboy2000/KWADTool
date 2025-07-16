using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KWADTool
{
	internal static class NameLookup
	{
		private class Mapping
		{
			public List<string> strings = new List<string>();
			public string current = "";
		}

		private static string mappingFile = "symbolMapping.txt";
		private static Dictionary<uint, Mapping> nameLookup = new Dictionary<uint, Mapping>();
		private static int changeCount = 0;

		public static void AddSymbolMapping(uint hash, string str, bool lowPriority)
		{
			int nullTerminatedLength = str.IndexOf('\0');
			if (nullTerminatedLength >= 0)
				str = str.Substring(0, nullTerminatedLength);

			Mapping m;

			if (!nameLookup.TryGetValue(hash, out m))
			{
				m = new Mapping();
				nameLookup.Add(hash, m);
			}

			bool found = false;
			for (int i = 0; i < m.strings.Count; i++)
			{
				if (m.strings[i] == str)
				{
					found = true;
				}
			}

			if (!found)
			{
				changeCount++;

				if (lowPriority)
				{
					m.strings.Insert(0, str);
					return;
				}
				else
				{
					m.strings.Add(str);
				}

				if (m.strings.Count > 1)
				{
					Console.WriteLine(
						String.Format(
							"Warning: Multiple strings mapped to hash {0}, may result in incorrect mapping (previous value {1}, new value {2})",
							hash.ToString(),
							m.current,
							str)
						);
				}
			}

			m.current = str;
		}

		public static string LookUp(uint hash)
		{
			if (hash == 0)
			{
				return "";
			}

			Mapping m;

			if (!nameLookup.TryGetValue(hash, out m))
			{
				m = new Mapping();
				m.current = hash.ToString();
				nameLookup.Add(hash, m);
				changeCount++;
				Console.WriteLine("Failed to look up string " + m.current + ". Try to add it to mappingTable.txt");
			}

			return m.current;
		}

		public static void LoadMappingTable()
		{
			try
			{
				string[] readText = File.ReadAllLines(mappingFile);

				foreach (string s in readText)
				{
					string[] split = s.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

					if (split.Length > 0)
					{
						uint hash = Convert.ToUInt32(split[0]);
						Mapping m = new Mapping();
						m.current = split[split.Length - 1];

						for (int i = 1; i < split.Length; i++)
						{
							m.strings.Add(split[i]);
						}

						nameLookup.Add(hash, m);
					}
				}
			}
			catch (IOException ex)
			{
				Console.WriteLine("Failed to load symbolMapping.txt: " + ex);
			}
		}

		public static void SaveMappingTable()
		{
			if (changeCount == 0)
			{
				return;
			}

			Console.WriteLine("Saving mappingTable.txt with " + changeCount.ToString() + " changes");

			try
			{
				using (StreamWriter sw = new StreamWriter(mappingFile, false))
				{
					foreach (var pair in nameLookup)
					{
						sw.Write(pair.Key);
						foreach (var symbol in pair.Value.strings)
						{
							sw.Write(':');
							sw.Write(symbol);
						}

						sw.WriteLine();
					}
				}
			}
			catch (IOException ex)
			{
				Console.WriteLine("Failed to save symbolMapping.txt: " + ex);
			}
		}
	}
}
