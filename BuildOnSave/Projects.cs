using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.Build.Execution;

namespace BuildOnSave
{
	/// Helper to work with DTE Projects
	static class Projects
	{
		public static Project[] SortByBuildOrder(BuildDependencies dependencies, Project[] instances)
		{
			var rootProjects = instances.ToDictionary(GetProjectKey);

			var ordered =
				rootProjects.Keys.SortTopologicallyReverse(key =>
					DependentProjectKeys(dependencies, rootProjects[key])
						.Where(rootProjects.ContainsKey));

			return ordered.Select(key => rootProjects[key]).ToArray();
		}

		/// Returns all the dependencies of a number of projects (direct and transitive).
		/// Never returns a root, even if roots hold references to each other.
		public static Project[] Dependencies(BuildDependencies buildDependencies, Project[] allProjects, Project[] roots)
		{
			var allKeys = allProjects.ToDictionary(GetProjectKey);
			var todo = new Queue<string>(roots.Select(GetProjectKey));
			var rootSet = new HashSet<string>(roots.Select(GetProjectKey));
			var dependencies = new HashSet<string>();
			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				DependentProjectKeys(buildDependencies, allKeys[next])
					.Where(g => !dependencies.Contains(g) && !rootSet.Contains(g) && allKeys.ContainsKey(g))
					.ForEach(g =>
					{
						todo.Enqueue(g);
						dependencies.Add(g);
					});
			}

			return dependencies
				.Select(g => allKeys[g])
				.ToArray();
		}

		/// Returns all the affected projects of the given list of projects. 
		/// Since the roots may refer to each other, the roots are included in the result set.
		public static Project[] AffectedProjects(this BuildDependencies dependencies, Project[] all, Project[] roots)
		{
			var dependentMap = DependentMap(dependencies, all);
			var allKeys = all.ToDictionary(GetProjectKey);
			var rootGuids = roots.Select(GetProjectKey).ToArray();
			var todo = new Queue<string>(rootGuids);
			var affected = new HashSet<string>(rootGuids);

			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				if (!dependentMap.TryGetValue(next, out var dependents))
					continue;

				dependents.ForEach(dep => {
						if (affected.Add(dep))
							todo.Enqueue(dep);
					});
			}

			return affected.Select(g => allKeys[g]).ToArray();
		}

		static Dictionary<string, HashSet<string>> DependentMap(BuildDependencies dependencies, Project[] all)
		{
			var allKeys = all.ToDictionary(GetProjectKey);
			var dict = new Dictionary<string, HashSet<string>>();
			foreach (var project in all)
			{
				var key = project.GetProjectKey();
				var deps = DependentProjectKeys(dependencies, project).Where(allKeys.ContainsKey).ToArray();
				foreach (var dep in deps)
				{
					if (!dict.TryGetValue(dep, out var dependents))
					{
						dependents = new HashSet<string>();
						dict.Add(dep, dependents);
					}
					dependents.Add(key);
				}
			}
			return dict;
		}

		/// Returns the keys of the direct dependencies of a project.
		public static string[] DependentProjectKeys(BuildDependencies dependencies, Project project)
		{
			var uniqueName = project.UniqueName;
			var dependency = dependencies.Item(uniqueName);
			// dependency might be null, see #52.
			if (dependency == null)
				return new string[0];
			return ((IEnumerable)dependency.RequiredProjects)
				.Cast<Project>()
				.Select(rp => rp.UniqueName)
				.ToArray();
		}

		public static Project[] FilterByPaths(Project[] projects, IEnumerable<string> paths)
		{
			var lookup = new HashSet<string>(paths.Select(path => path.ToLowerInvariant()));
			return projects.Where(project => lookup.Contains(project.FullName.ToLowerInvariant())).ToArray();
		}

		public static Project[] OfPaths(Project[] allProjects, IEnumerable<string> paths)
		{
			var dictByPath = allProjects.ToDictionary(project => project.FullName.ToLowerInvariant());
			return paths.Select(path => dictByPath[path.ToLowerInvariant()]).ToArray();
		}

		public static string GetProjectKey(this Project project)
		{
			return project.UniqueName;
		}

		public static Dictionary<string, (string, string)[]> GlobalProjectConfigurationProperties(this SolutionContexts solutionContexts)
		{
			return solutionContexts
				.Cast<SolutionContext>()
				.ToDictionary(context => context.ProjectName, GlobalProjectConfigurationProperties);
		}

		static (string, string)[] GlobalProjectConfigurationProperties(this SolutionContext context)
		{
			// not sure why that is.
			var platformName = 
				context.PlatformName == "Any CPU" 
				? "AnyCPU" 
				: context.PlatformName;

			return new[]
			{
				("Configuration", context.ConfigurationName),
				("Platform", platformName)
			};
		}

		public static ProjectInstance CreateInstance(this Project project, (string, string)[] globalProperties)
		{
			var properties = globalProperties.ToDictionary(kv => kv.Item1, kv => kv.Item2);
			return new ProjectInstance(project.FullName, properties, null);
		} 
	}
}
