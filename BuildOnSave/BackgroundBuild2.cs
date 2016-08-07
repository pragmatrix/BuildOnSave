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
		}

		struct BuildRequest
		{
			public BuildRequest(ProjectInstance[] projectInstances, string solutionPath, bool wholeSolutionBuid, string configuration, string platform)
			{
				ProjectInstances = sortByBuildOrder(projectInstances);
				SolutionPath = solutionPath;
				WholeSolutionBuild = wholeSolutionBuid;
				Configuration = configuration;
				Platform = platform;
			}

			public readonly ProjectInstance[] ProjectInstances;
			public readonly string SolutionPath;
			public readonly bool WholeSolutionBuild;
			public readonly string Configuration;
			public readonly string Platform;

			public BuildRequestData[] createBuildRequestDatas()
			{
				return
					ProjectInstances
						.Select(instance => new BuildRequestData(instance, instance.DefaultTargets.ToArray(), null))
						.ToArray();
			}

			static ProjectInstance[] sortByBuildOrder(ProjectInstance[] instances)
			{
				var rootProjects = instances.ToDictionary(getProjectGuid);

				var ordered = 
					rootProjects.Keys.SortTopologically(
						g1 => getDependentProjectGuids(rootProjects[g1]).Where(rootProjects.ContainsKey));

				return ordered.Select(g => rootProjects[g]).ToArray();
			}
		}

		/// <summary>
		/// Asynchronously begins a background build.
		/// </summary>
		/// <param name="onCompleted"></param>
		/// <param name="projects">The projects to build. If this array is empty, the whole solution is built.</param>

		public void beginBuild(Action<BuildStatus> onCompleted, params string[] projects)
		{
			if (IsRunning)
			{
				Log.E("internal error: build is running");
				return;
			}

			var allProjectInstances =
				ProjectCollection
					.GlobalProjectCollection
					.LoadedProjects
					.Select(p => p.CreateProjectInstance())
					// this removes csproj.user projects
					.Where(instance => instance.DefaultTargets.Count != 0)
					.ToArray();

			var wholeSolutionBuild = projects.Length == 0;
			var projectInstances = !wholeSolutionBuild
				? reduceBuildToProjects(allProjectInstances, projectInstancesOfPaths(allProjectInstances, projects)) 
				: allProjectInstances;

			var solution = (Solution2)_dte.Solution;
			var configuration = (SolutionConfiguration2)solution.SolutionBuild.ActiveConfiguration;
			var request = new BuildRequest(projectInstances, solution.FullName, wholeSolutionBuild, configuration.Name, configuration.PlatformName);
			_buildCancellation_ = new CancellationTokenSource();

			Action<BuildStatus> completed = status =>
			{
				_buildCancellation_.Dispose();
				_buildCancellation_ = null;
				onCompleted(status);
			};

			ThreadPool.QueueUserWorkItem(_ => build(request, _buildCancellation_.Token, completed));
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

				status = buildCore(request, cancellation);
			}
			catch (Exception e)
			{
				Log.E(e, "build crashed");
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
			coreToIDE(() =>
			{
				_pane.Clear();
				_pane.Activate();
			});

			var consoleLogger = new ConsoleLogger(LoggerVerbosity.Quiet,
				str =>
					coreToIDE(() => _pane.OutputString(str)),
				color => { },
				() => { })
			{
				SkipProjectStartedText = true,
				ShowSummary = false
			};

			var summaryLogger = new SummaryLogger();

			var parameters = new BuildParameters
			{
				Loggers = new ILogger[] {consoleLogger, summaryLogger},
				EnableNodeReuse = false,
				ShutdownInProcNodeOnBuildFinish = true,
				DetailedSummary = false
			};

			printIntro(request);
			var status = buildCore(request, cancellation, parameters);
			printSummary(summaryLogger);
			return status;
		}

		static BuildStatus buildCore(BuildRequest request, CancellationToken cancellation, BuildParameters parameters)
		{
			using (measureBlock("build time"))
			using (var buildManager = new BuildManager())
			using (beginBuild(buildManager, parameters))
			using (cancellation.Register(() =>
			{
				Log.I("cancelling background build");
				buildManager.CancelAllSubmissions();
			}))
			{
				var buildDatas = request.createBuildRequestDatas();

				foreach (var buildData in buildDatas)
				{
					var result = buildManager.BuildRequest(buildData);
					if (cancellation.IsCancellationRequested)
						return BuildStatus.Indeterminate;
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
			var isSolution = request.WholeSolutionBuild;
			var projectInfo = isSolution ?
				"Solution: " + getSolutionNameFromFilename(request.SolutionPath)
				: (request.ProjectInstances.Length == 1
					? ("Project: " + nameOfProject(request.ProjectInstances[0]))
					: ("Projects: " + request.ProjectInstances.Select(nameOfProject).Aggregate((a, b) => a + ";" + b)));
			var configurationInfo = "Configuration: " + request.Configuration + " " + request.Platform;

			coreToIDE(() => _pane.OutputString($"---------- BuildOnSave: {projectInfo}, {configurationInfo} ----------\n"));
		}
		static string getSolutionNameFromFilename(string fn)
		{
			return Path.GetFileNameWithoutExtension(fn);
		}

		void printSummary(SummaryLogger logger)
		{
			var results = logger.ProjectResults;
			// we must group by filename and not by project id, it seems that we get multiple results with different project ids for the same project.
			var projectResults = results.GroupBy(result => result.Filename).Select(rs => rs.Last());
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

		/// Reduce build to all the given project and its dependencies only (the closure of all the projects referenced limited to allInstances).
		static ProjectInstance[] reduceBuildToProjects(ProjectInstance[] allInstances, ProjectInstance[] limitTo)
		{
			var allGuids = allInstances.ToDictionary(getProjectGuid);
			var todo = new Queue<Guid>(limitTo.Select(getProjectGuid));
			var done = new HashSet<Guid>(todo);
			while (todo.Count != 0)
			{
				var next = todo.Dequeue();

				getDependentProjectGuids(allGuids[next])
					.Where(g => !done.Contains(g) && allGuids.ContainsKey(g))
					.ForEach(todo.Enqueue);
			}

			return done.Select(g => allGuids[g]).ToArray();
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

		#endregion
	}
}
