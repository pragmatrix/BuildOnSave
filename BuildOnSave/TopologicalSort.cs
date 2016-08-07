using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildOnSave
{
	static class TopologicalSort
	{
		// returns roots first.

		public static IEnumerable<NodeT> SortTopologically<NodeT>(this IEnumerable<NodeT> roots, Func<NodeT, IEnumerable<NodeT>> edges)
		{
			return TopologicallyReverse(roots, edges).Reverse();
		}

		// returns roots last.

		public static IEnumerable<NodeT> TopologicallyReverse<NodeT>(this IEnumerable<NodeT> roots, Func<NodeT, IEnumerable<NodeT>> edges)
		{
			var res = new List<NodeT>();
			var marked = new HashSet<NodeT>();
			foreach (var n in roots)
				addNode(res, marked, n, edges);

			return res;
		}

		static void addNode<NodeT>(ICollection<NodeT> list, HashSet<NodeT> marked, NodeT node, Func<NodeT, IEnumerable<NodeT>> edges)
		{
			if (!marked.Add(node))
				return;

			foreach (var more in edges(node))
				addNode(list, marked, more, edges);

			list.Add(node);
		}
	}
}

