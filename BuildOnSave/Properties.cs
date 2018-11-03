using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms.VisualStyles;

namespace BuildOnSave
{
	/// Extensions for handling property lists.
	static class Properties
	{
		/// Merge property lists (the ones on the right will overwrite the ones on the left).
		public static (string, string)[] Merge(this (string, string)[] left, (string, string)[] right)
		{
			var dict = left.ToDictionary();
			right.ForEach(t => dict[t.Item1] = t.Item2);
			return dict.ToProperties();
		}

		public static (string, string)[] ToProperties(this IDictionary<string, string> dict)
		{
			return dict.Select(kv => (kv.Key, kv.Value)).ToArray();
		}
		public static IDictionary<string, string> ToDictionary(this (string, string)[] properties)
		{
			return properties.ToDictionary(t => t.Item1, t => t.Item2);
		}

		public static (string, string)[] Empty() => Array.Empty<(string,string)>();
	}
}
