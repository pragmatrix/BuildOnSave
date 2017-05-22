using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using static BuildOnSave.DevTools;

namespace BuildOnSave
{
	/// The second iteration of the background builder that uses ProjectInstances instead of complete solutions. 
	/// This is because we need to provide properties per project and not only per solution.

	sealed class BackgroundBuild2
	{
		readonly DTE _dte;
		readonly OutputWindowPane _pane;
		readonly SynchronizationContext _mainThread;
		static BuildManager BuildManager = new BuildManager();

		// State as seen from the main thread.
		CancellationTokenSource _buildCancellation_;
		public bool IsRunning => _buildCancellation_ != null;

		// State as seen from the background thread.
		readonly object _coreSyncRoot = new object();
		bool _coreRunning;

		public BackgroundBuild2(DTE dte, OutputWindowPane pane)
		{
			_dte = dte;
			_pane = pane;
			_mainThread = SynchronizationContext.Current;
			BuildManager = new BuildManager();
		}

		public struct BuildRequest
		{
			public BuildRequest(
				ProjectInstance[] primaryProjects, 
				ProjectInstance[] skippedProjects, 
				string configuration, 
				string platform)
			{
				PrimaryProjects = primaryProjects;
				AllProjectsToBuildOrdered = sortByBuildOrder(primaryProjects.Concat(skippedProjects).ToArray());
				Configuration = configuration;
				Platform = platform;

				_skipped = new HashSet<ProjectInstance>(skippedProjects);
			}

			public readonly ProjectInstance[] PrimaryProjects;
			public readonly ProjectInstance[] AllProjectsToBuildOrdered;
			public readonly string Configuration;
			public readonly string Platform;

			readonly HashSet<ProjectInstance> _skipped;

			public bool mustBeSkipped(ProjectInstance instance)
			{
				return _skipped.Contains(instance);
			}

			public BuildRequestData createBuildRequestData(ProjectInstance instance)
			{
				return new BuildRequestData(instance, instance.DefaultTargets.ToArray(), null);
			}

			static ProjectInstance[] sortByBuildOrder(ProjectInstance[] instances)
			{
				var rootProjects = instances.ToDictionary(getProjectGuid);

				var ordered = 
					rootProjects.Keys.SortTopologicallyReverse(
						g1 => getDependentProjectGuids(rootProjects[g1]).Where(rootProjects.ContainsKey));

				return ordered.Select(g => rootProjects[g]).ToArray();
			}
		}

		/// <summary>
		/// Asynchronously begins a background build.
		/// </summary>
		/// <param name="onCompleted"></param>
		/// <param name="startupProject_">The startup project to build. If null, the whole solution is built.
		/// Note that the explicitly passed projects are always built, independently of the solution configuration, but dependencies may not.</param>
		/// <param name="changedProjects">The changed projects.</param>

		public void beginBuild(Action<BuildStatus> onCompleted, BuildRequest request)
		{
			if (IsRunning)
			{
				Log.E("internal error: build is running");
				return;
			}
				
			_buildCancellation_ = new CancellationTokenSource();

			Action<BuildStatus> completed = status =>
			{
				_buildCancellation_.Dispose();
				_buildCancellation_ = null;
				onCompleted(status);
			};

			ThreadPool.QueueUserWorkItem(_ => build(request, _buildCancellation_.Token, completed));
		}

		public BuildRequest? tryMakeBuildRequest(string startupProject_, string[] changedProjectPaths)
		{
			var allProjectsFast =
				ProjectCollection
					.GlobalProjectCollection
					.LoadedProjects
					.Select(p => p.CreateProjectInstance())
					// this removes csproj.user projects
					.Where(instance => instance.DefaultTargets.Count != 0)
					.ToArray();

			var solution = (Solution2)_dte.Solution;

			var loadedDTEProjects =
				solution.GetAllProjects()
					.Where(p => p.IsLoaded())
					.ToArray();

			var configuration = (SolutionConfiguration2)solution.SolutionBuild.ActiveConfiguration;

			var globalProperties = new Dictionary<string, string>
			{
				{ "Configuration", configuration.Name },
				{ "Platform", configuration.PlatformName }
			};

			var allProjects =
				loadedDTEProjects.Select(p => new ProjectInstance(p.FullName, globalProperties, null)).ToArray();


			// note: fullpath may note be accessible if the project is not loaded!
			var uniqueNameToProject =
				solution.Projects
				.Cast<EnvDTE.Project>()
				.ToDictionary(p => p.UniqueName, p => p);

			var solutionSelectedPaths =
				configuration
					.SolutionContexts
					.Cast<SolutionContext>()
					.Where(sc => sc.ShouldBuild)
					.Where(sc => uniqueNameToProject.ContainsKey(sc.ProjectName))
					.Select(sc => uniqueNameToProject[sc.ProjectName].FullName)
					.ToArray();

			var solutionSelectedInstances = filterProjectInstancesByPaths(allProjects, solutionSelectedPaths);
			var solutionSkippedInstances = allProjects.Except(solutionSelectedInstances).ToArray();

			var changedProjects = projectInstancesOfPaths(allProjects, changedProjectPaths);

			if (startupProject_ == null)
			{
				var affectedProjects = getAffectedProjects(allProjects, changedProjects);

				var selected = affectedProjects.Intersect(solutionSelectedInstances).ToArray();
				if (selected.Length == 0)
					return null;
				var skipped = affectedProjects.Intersect(solutionSkippedInstances).ToArray();

				return new BuildRequest(
					selected,
					skipped,
					configuration.Name,
					configuration.PlatformName);
			}
			else
			{
				var startupProjects = filterProjectInstancesByPaths(allProjects, new [] { startupProject_});
				var startupProjectsClosure =
					getDependencies(allProjects, startupProjects)
						.Concat(startupProjects)
						.ToArray();

				var changedProjectsInStartupProjectsClosure = changedProjects.Intersect(startupProjectsClosure).ToArray();

				var affectedProjects = getAffectedProjects(startupProjectsClosure, changedProjectsInStartupProjectsClosure);

				var selected = affectedProjects.Intersect(solutionSelectedInstances).ToArray();
				if (selected.Length == 0)
					return null;
				var skipped = affectedProjects.Intersect(solutionSkippedInstances).ToArray();

				return new BuildRequest(
					selected,
					skipped,
					configuration.Name,
					configuration.PlatformName);
			}
		}

		/// <summary>
		/// Cancel an outstanding build and wait for it to end. 
		/// Note that this method may return at the time the build actually ends but shortly before IsRunning is set to false.
		/// </summary>
		public void cancelAndWait()
		{
			_buildCancellation_?.Cancel();

			lock (_coreSyncRoot)
			{
				while (_coreRunning)
					Monitor.Wait(_coreSyncRoot);
			}
		}

		void build(BuildRequest request, CancellationToken cancellation, Action<BuildStatus> onCompleted)
		{
			var status = BuildStatus.Failed;

			try
			{
				lock (_coreSyncRoot)
				{
					_coreRunning = true;
					Monitor.PulseAll(_coreSyncRoot);
				}

				// cancelAndWait() may cancel _and_ return before _coreRunning was set to true, we
				// want to be sure to not start another build then.
				cancellation.ThrowIfCancellationRequested();

				coreToIDE(() =>
				{
					_pane.Clear();
					_pane.Activate();
				});

				status = buildCore(request, cancellation);
			}
			catch (Exception e)
			{
				Log.E(e, "build crashed");
				coreToIDE(() => {
					_pane.reportException(e);
				});
			}
			finally
			{
				lock (_coreSyncRoot)
				{
					_coreRunning = false;
					Monitor.PulseAll(_coreSyncRoot);
				}

				_mainThread.Post(_ => onCompleted(status), null);
			}
		}

		BuildStatus buildCore(BuildRequest request, CancellationToken cancellation)
		{
#if DEBUG
			var verbosity = LoggerVerbosity.Minimal;
#else
			var verbosity = LoggerVerbosity.Quiet;
#endif
			var consoleLogger = new ConsoleLogger(verbosity,
				str =>
					coreToIDE(() => _pane.OutputString(str)),
				color => { },
				() => { })
			{
				SkipProjectStartedText = true,
				ShowSummary = false
			};

			var summaryLogger = new SummaryLogger();

			var parameters = new BuildParameters()
			{
				Loggers = new ILogger[] {consoleLogger, summaryLogger},
				EnableNodeReuse = true,
				ShutdownInProcNodeOnBuildFinish = false,
				DetailedSummary = false,
			};
			
			printIntro(request);
			var status = buildCore(request, cancellation, parameters);
			printSummary(summaryLogger, request.AllProjectsToBuildOrdered);
			return status;
		}

		BuildStatus buildCore(BuildRequest request, CancellationToken cancellation, BuildParameters parameters)
		{
			using (measureBlock("build time"))
			using (beginBuild(BuildManager, parameters))
			using (cancellation.Register(() =>
			{
				Log.I("cancelling background build");
				BuildManager.CancelAllSubmissions();
			}))
			{
				var projects = request.AllProjectsToBuildOrdered;
				
				foreach (var project in projects)
				{
					if (cancellation.IsCancellationRequested)
						return BuildStatus.Indeterminate;

					if (request.mustBeSkipped(project))
					{
						notifyProjectSkipped(project);
						continue;
					}

					var buildData = request.createBuildRequestData(project);
					var result = BuildManager.BuildRequest(buildData);
					switch (result.OverallResult)
					{
						case BuildResultCode.Success:
							continue;
						case BuildResultCode.Failure:
							return BuildStatus.Failed;
						default:
							throw new ArgumentOutOfRangeException();
					}
				}

				return BuildStatus.Ok;
			}
		}

		static IDisposable beginBuild(BuildManager buildManager, BuildParameters parameters)
		{
			buildManager.BeginBuild(parameters);
			return new DisposeAction(buildManager.EndBuild);
		}

		void printIntro(BuildRequest request)
		{
			var projectInfo = 
				request.PrimaryProjects.Length == 1
					? ("Project: " + nameOfProject(request.PrimaryProjects[0]))
					: ("Projects: " + request.PrimaryProjects.Select(nameOfProject).Aggregate((a, b) => a + ";" + b));
			var configurationInfo = "Configuration: " + request.Configuration + " " + request.Platform;

			coreToIDE(() => _pane.OutputString($"---------- BuildOnSave: {projectInfo}, {configurationInfo} ----------\n"));
		}

		void notifyProjectSkipped(ProjectInstance project)
		{
			var projectName = nameOfProject(project);
			coreToIDE(() => _pane.OutputString($"{projectName} is not built because of the current solution configuration.\n"));
		}

		void printSummary(SummaryLogger logger, ProjectInstance[] allProjects)
		{
			var results = logger.ProjectResults;
			var filenamesOfProjects = new HashSet<string>(allProjects.Select(pi => pi.FullPath));
			// we must group by filename and not by project id, it seems that we get multiple results with different project ids for the same project.
			// and even that list may include projects that we did not actually build explicitly.
			var projectResults = 
				results
				.GroupBy(result => result.Filename)
				.Select(rs => rs.Last())
				.Where(rs => filenamesOfProjects.Contains(rs.Filename))
				.ToArray();
			var succeded = projectResults.Count(result => result.Succeeded);
			var failed = projectResults.Count(result => !result.Succeeded);

			coreToIDE(() => _pane.OutputString($"========== BuildOnSave: {succeded} succeeded, {failed} failed ==========\n"));
		}

		// The IDE _does_ marshal function invocation, but we don't want to run in any deadlocks, when the main thread calls
		// cancelAndWait().
		void coreToIDE(Action action)
		{
			_mainThread.Post(_ => action(), null);
		}

		struct BuildResult
		{
			public BuildResult(int projectId, string filename, bool succeeded)
			{
				ProjectId = projectId;
				Filename = filename;
				Succeeded = succeeded;
			}

			public readonly int ProjectId;
			public readonly string Filename;
			public readonly bool Succeeded;

			public override string ToString()
			{
				return $"id: {ProjectId}, fn: {Filename}, succeeded: {Succeeded}";
			}
		}

		sealed class SummaryLogger : ILogger
		{
			readonly List<BuildResult> _projectResults = new List<BuildResult>();
			public IEnumerable<BuildResult> ProjectResults => _projectResults.ToArray();

			public void Initialize(IEventSource eventSource)
			{
				eventSource.ProjectFinished += onProjectFinished;
			}

			void onProjectFinished(object sender, ProjectFinishedEventArgs e)
			{
				_projectResults.Add(new BuildResult(e.BuildEventContext.ProjectInstanceId, e.ProjectFile, e.Succeeded));
			}

			public void Shutdown()
			{ }

			public LoggerVerbosity Verbosity { get; set; }
			public string Parameters { get; set; }
		}

#region ProjectInstance helpers

		/// Returns all the dependencies of a number of projects. Never returns a root, even if roots contain references
		/// to each other.
		static ProjectInstance[] getDependencies(ProjectInstance[] allInstances, ProjectInstance[] roots)
		{
			var allGuids = allInstances.ToDictionary(getProjectGuid);
			var todo = new Queue<Guid>(roots.Select(getProjectGuid));
			var rootSet = new HashSet<Guid>(roots.Select(getProjectGuid));
			var dependencies = new HashSet<Guid>();
			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				getDependentProjectGuids(allGuids[next])
					.Where(g => !dependencies.Contains(g) && !rootSet.Contains(g) && allGuids.ContainsKey(g))
					.ForEach(g =>
					{
						todo.Enqueue(g);
						dependencies.Add(g);
					});
			}

			return dependencies.Select(g => allGuids[g]).ToArray();
		}

		/// Returns all the affected projects of the given list of projects. 
		/// Since the roots may refer to each other, the roots are included in the result set.
		static ProjectInstance[] getAffectedProjects(ProjectInstance[] allInstances, ProjectInstance[] roots)
		{
			var dependentMap = createDependentMap(allInstances);
			var allGuids = allInstances.ToDictionary(getProjectGuid);
			var rootGuids = roots.Select(getProjectGuid).ToArray();
			var todo = new Queue<Guid>(rootGuids);
			var affected = new HashSet<Guid>(rootGuids);

			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				HashSet<Guid> dependents = null;
				if (!dependentMap.TryGetValue(next, out dependents))
					continue;

				dependents.ForEach(dep => {
					if (affected.Add(dep))
						todo.Enqueue(dep); }
				);
			}

			return affected.Select(g => allGuids[g]).ToArray();
		}


		static Dictionary<Guid, Guid[]> createDependencyMap(ProjectInstance[] allInstances)
		{
			var allGuids = allInstances.ToDictionary(getProjectGuid);
			var dict = new Dictionary<Guid, Guid[]>();
			foreach (var inst in allInstances)
			{
				var guid = getProjectGuid(inst);
				var deps = getDependentProjectGuids(inst).Where(allGuids.ContainsKey).ToArray();
				dict.Add(guid, deps);
			}
			return dict;
		}

		static Dictionary<Guid, HashSet<Guid>> createDependentMap(ProjectInstance[] allInstances)
		{
			var allGuids = allInstances.ToDictionary(getProjectGuid);
			var dict = new Dictionary<Guid, HashSet<Guid>>();
			foreach (var inst in allInstances)
			{
				var guid = getProjectGuid(inst);
				var deps = getDependentProjectGuids(inst).Where(allGuids.ContainsKey).ToArray();
				foreach (var dep in deps)
				{
					HashSet<Guid> dependents = null;
					if (!dict.TryGetValue(dep, out dependents))
					{
						dependents = new HashSet<Guid>();
						dict.Add(dep, dependents);
					}
					dependents.Add(guid);
				}
			}
			return dict;
		}


		static Guid getProjectGuid(ProjectInstance instance)
		{
			var projectGuid = instance.GetPropertyValue("ProjectGuid");
			if (projectGuid == "")
				throw new Exception("project has no Guid");
			return Guid.Parse(projectGuid);
		}

		static Guid[] getDependentProjectGuids(ProjectInstance instance)
		{
			var refs = instance.GetItems("ProjectReference").Select(item => Guid.Parse(item.GetMetadataValue("Project"))).ToArray();
			return refs;
		}

		static ProjectInstance[] projectInstancesOfPaths(ProjectInstance[] allInstances, IEnumerable<string> paths)
		{
			var dictByPath = allInstances.ToDictionary(instance => instance.FullPath.ToLowerInvariant());
			return paths.Select(path => dictByPath[path.ToLowerInvariant()]).ToArray();
		}

		static string nameOfProject(ProjectInstance instance)
		{
			return Path.GetFileNameWithoutExtension(instance.FullPath);
		}

		static ProjectInstance[] filterProjectInstancesByPaths(ProjectInstance[] instances, IEnumerable<string> paths)
		{
			var lookup = new HashSet<string>(paths.Select(path => path.ToLowerInvariant()));
			return instances.Where(instance => lookup.Contains(instance.FullPath.ToLowerInvariant())).ToArray();
		}

#endregion
	}
}
