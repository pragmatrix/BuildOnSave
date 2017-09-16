using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Execution;

namespace BuildOnSave
{
	/// Helpers for working with ProjectInstances.
	static class ProjectInstances
	{
		/// Returns all the dependencies of a number of projects (direct and transitive).
		/// Never returns a root, even if roots hold references to each other.
		public static ProjectInstance[] Dependencies(ProjectInstance[] allInstances, ProjectInstance[] roots)
		{
			var allGuids = allInstances.ToDictionary(GetProjectGUID);
			var todo = new Queue<Guid>(roots.Select(GetProjectGUID));
			var rootSet = new HashSet<Guid>(roots.Select(GetProjectGUID));
			var dependencies = new HashSet<Guid>();
			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				DependentProjectGUIDs(allGuids[next])
					.Where(g => !dependencies.Contains(g) && !rootSet.Contains(g) && allGuids.ContainsKey(g))
					.ForEach(g =>
					{
						todo.Enqueue(g);
						dependencies.Add(g);
					});
			}

			return dependencies
				.Select(g => allGuids[g])
				.ToArray();
		}

		/// Returns all the affected projects of the given list of projects. 
		/// Since the roots may refer to each other, the roots are included in the result set.
		public static ProjectInstance[] AffectedProjects(ProjectInstance[] allInstances, ProjectInstance[] roots)
		{
			var dependentMap = DependentMap(allInstances);
			var allGuids = allInstances.ToDictionary(GetProjectGUID);
			var rootGuids = roots.Select(GetProjectGUID).ToArray();
			var todo = new Queue<Guid>(rootGuids);
			var affected = new HashSet<Guid>(rootGuids);

			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				if (!dependentMap.TryGetValue(next, out var dependents))
					continue;

				dependents.ForEach(dep => {
						if (affected.Add(dep))
							todo.Enqueue(dep); }
				);
			}

			return affected.Select(g => allGuids[g]).ToArray();
		}

		static Dictionary<Guid, HashSet<Guid>> DependentMap(ProjectInstance[] allInstances)
		{
			var allGuids = allInstances.ToDictionary(GetProjectGUID);
			var dict = new Dictionary<Guid, HashSet<Guid>>();
			foreach (var inst in allInstances)
			{
				var guid = GetProjectGUID(inst);
				var deps = DependentProjectGUIDs(inst).Where(allGuids.ContainsKey).ToArray();
				foreach (var dep in deps)
				{
					if (!dict.TryGetValue(dep, out var dependents))
					{
						dependents = new HashSet<Guid>();
						dict.Add(dep, dependents);
					}
					dependents.Add(guid);
				}
			}
			return dict;
		}

		public static Guid GetProjectGUID(this ProjectInstance instance)
		{
			var projectGuid = instance.GetPropertyValue("ProjectGuid");
			if (projectGuid == "")
				throw new Exception("project has no Guid");
			return Guid.Parse(projectGuid);
		}

		public static ProjectInstance[] OfPaths(ProjectInstance[] allInstances, IEnumerable<string> paths)
		{
			var dictByPath = allInstances.ToDictionary(instance => instance.FullPath.ToLowerInvariant());
			return paths.Select(path => dictByPath[path.ToLowerInvariant()]).ToArray();
		}

		public static string NameOf(this ProjectInstance instance)
		{
			return Path.GetFileNameWithoutExtension(instance.FullPath);
		}

		public static ProjectInstance[] FilterByPaths(ProjectInstance[] instances, IEnumerable<string> paths)
		{
			var lookup = new HashSet<string>(paths.Select(path => path.ToLowerInvariant()));
			return instances.Where(instance => lookup.Contains(instance.FullPath.ToLowerInvariant())).ToArray();
		}
		public static Guid[] DependentProjectGUIDs(this ProjectInstance instance)
		{
			var refs = instance.GetItems("ProjectReference").Select(item => Guid.Parse(item.GetMetadataValue("Project"))).ToArray();
			return refs;
		}
	}
}
