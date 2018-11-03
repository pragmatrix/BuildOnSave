using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Execution;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using static BuildOnSave.DevTools;
using Project = EnvDTE.Project;

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
				BuildDependencies dependencies,
				Project[] primaryProjects,
				Project[] skippedProjects,
				string solutionConfiguration,
				string solutionPlatform,
				SolutionContexts solutionContexts,
				(string, string)[] solutionProperties
				)
			{
				var allProjects = primaryProjects.Concat(skippedProjects).ToArray();
				var allOrdered = Projects.SortByBuildOrder(dependencies, allProjects);
				var globalProjectConfigurationProperties = solutionContexts.GlobalProjectConfigurationProperties();
				var instanceMap = allProjects.ToDictionary(
					project => project, 
					project => project.CreateInstance(resolveProperties(project)));

				PrimaryProjects = primaryProjects.Select(p => instanceMap[p]).ToArray();
				AllProjectsToBuildOrdered = allOrdered.Select(p => instanceMap[p]).ToArray();

				Log.D("projects to build (ordered): {0}", 
					string.Join(", ", AllProjectsToBuildOrdered.Select(instance => instance.NameOf()).ToArray()));

				SolutionConfiguration = solutionConfiguration;
				SolutionPlatform = solutionPlatform;

				_projectsToSkip = new HashSet<ProjectInstance>(skippedProjects.Select(p => instanceMap[p]));

				(string, string)[] resolveProperties(Project project)
				{
					// If the project is managed with the new build system,
					// take specific properties from there.
					var specificProperties = resolveSpecificProperties(project);
					var configurationProperties = globalProjectConfigurationProperties[project.UniqueName];

					return 
						// note that specific properties may already contain solution and
						// configuration properties, if so, overwrite them with the
						// ones retrieved from the IDE or computed ones.
						specificProperties
							.Merge(solutionProperties)
							.Merge(configurationProperties);
				}

				// Note that these may contain solution _and_ configuration properties.
				(string, string)[] resolveSpecificProperties(Project project)
				{
					var existingProject = 
						ProjectCollection
							.GlobalProjectCollection
							.GetLoadedProjects(project.FullName)
							.SingleOrDefault();

					if (existingProject == null)
						return Properties.Empty();

					Log.D("resolved specific project properties for project: {0}", project.Name);
					return existingProject.GlobalProperties.ToProperties();
				}
			}

			public readonly ProjectInstance[] PrimaryProjects;
			public readonly ProjectInstance[] AllProjectsToBuildOrdered;
			public readonly string SolutionConfiguration;
			public readonly string SolutionPlatform;

			readonly HashSet<ProjectInstance> _projectsToSkip;

			public bool mustBeSkipped(ProjectInstance instance)
			{
				return _projectsToSkip.Contains(instance);
			}

			public BuildRequestData createBuildRequestData(ProjectInstance instance)
			{
				return new BuildRequestData(instance, instance.DefaultTargets.ToArray(), null);
			}
		}

		/// Asynchronously begins a background build.
		public void beginBuild(Action<BuildStatus> onCompleted, BuildRequest request)
		{
			if (IsRunning)
			{
				Log.E("internal error: build is running");
				return;
			}
				
			_buildCancellation_ = new CancellationTokenSource();

			void OnCompleted(BuildStatus status)
			{
				_buildCancellation_.Dispose();
				_buildCancellation_ = null;
				onCompleted(status);
			}

			ThreadPool.QueueUserWorkItem(_ => build(request, _buildCancellation_.Token, OnCompleted));
		}

		public BuildRequest? tryMakeBuildRequest(string startupProject_, string[] changedProjectPaths)
		{
			var solution = (Solution2)_dte.Solution;

			var solutionPath = solution.FullName;

			var solutionProperties = new[]
			{
				("SolutionPath", solutionPath),
				("SolutionDir", Path.GetDirectoryName(solutionPath)),
				("SolutionName", Path.GetFileNameWithoutExtension(solutionPath)),
				("SolutionFileName", Path.GetFileName(solutionPath)),
				("SolutionExt", Path.GetExtension(solutionPath))
			};

			var loadedProjects =
				solution.GetAllProjects()
					.Where(p => p.IsLoaded())
					.ToArray();

			var dependencies = solution.SolutionBuild.BuildDependencies;

			var configuration = (SolutionConfiguration2)solution.SolutionBuild.ActiveConfiguration;

			var uniqueNameToProject =
				loadedProjects
				.ToDictionary(p => p.UniqueName, p => p);

			var solutionSelectedPaths =
				configuration
					.SolutionContexts
					.Cast<SolutionContext>()
					.Where(sc => sc.ShouldBuild)
					.Where(sc => uniqueNameToProject.ContainsKey(sc.ProjectName))
					.Select(sc => uniqueNameToProject[sc.ProjectName].FullName)
					.ToArray();

			var solutionSelectedInstances = Projects.FilterByPaths(loadedProjects, solutionSelectedPaths);
			var solutionSkippedInstances = loadedProjects.Except(solutionSelectedInstances).ToArray();

			var changedProjects = Projects.OfPaths(loadedProjects, changedProjectPaths);

			if (startupProject_ == null)
			{
				var affectedProjects = dependencies.AffectedProjects(loadedProjects, changedProjects);

				var selected = affectedProjects.Intersect(solutionSelectedInstances).ToArray();
				if (selected.Length == 0)
					return null;
				var skipped = affectedProjects.Intersect(solutionSkippedInstances).ToArray();

				return new BuildRequest(
					dependencies,
					selected,
					skipped,
					configuration.Name,
					configuration.PlatformName, 
					configuration.SolutionContexts,
					solutionProperties
					);
			}
			else
			{
				var startupProjects = Projects.FilterByPaths(loadedProjects, new [] { startupProject_});
				var startupProjectsClosure = Projects.Dependencies(dependencies, loadedProjects, startupProjects)
						.Concat(startupProjects)
						.ToArray();

				var changedProjectsInStartupProjectsClosure = changedProjects.Intersect(startupProjectsClosure).ToArray();

				var affectedProjects = dependencies.AffectedProjects(startupProjectsClosure, changedProjectsInStartupProjectsClosure);

				var selected = affectedProjects.Intersect(solutionSelectedInstances).ToArray();
				if (selected.Length == 0)
					return null;
				var skipped = affectedProjects.Intersect(solutionSkippedInstances).ToArray();

				return new BuildRequest(
					dependencies,
					selected,
					skipped,
					configuration.Name,
					configuration.PlatformName,
					configuration.SolutionContexts,
					solutionProperties);
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
					? ("Project: " + request.PrimaryProjects[0].NameOf())
					: ("Projects: " +
						// request.PrimaryProjects contains the projects that will actually be built, but in the wrong order, so
						// we filter them from AllProjects (tbd: this could be prepared in the BuildRequest constructor).
						string.Join(";", request.AllProjectsToBuildOrdered
							.Where(instance => !request.mustBeSkipped(instance))
							.Select(ProjectInstances.NameOf)));
			var configurationInfo = "Configuration: " + request.SolutionConfiguration + " " + request.SolutionPlatform;

			coreToIDE(() => _pane.OutputString($"---------- BuildOnSave: {projectInfo}, {configurationInfo} ----------\n"));
		}

		void notifyProjectSkipped(ProjectInstance project)
		{
			var projectName = ProjectInstances.NameOf(project);
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
	}
}
