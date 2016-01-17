using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace BuildOnSave
{
	sealed class BackgroundBuild
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

		public BackgroundBuild(DTE dte, OutputWindowPane pane)
		{
			_dte = dte;
			_pane = pane;
			_mainThread = SynchronizationContext.Current;
		}

		struct BuildRequest
		{
			public BuildRequest(string solutionFilename, string project, string configuration, string platform)
			{
				SolutionFilename = solutionFilename;
				Project_ = project;
				Configuration = configuration;
				Platform = platform;
			}

			public readonly string SolutionFilename;
			public readonly string Project_;
			public readonly string Configuration;
			public readonly string Platform;

			public BuildRequestData createData()
			{
				var globalProperties = new Dictionary<string, string> { ["Configuration"] = Configuration, ["Platform"] = Platform };
				var targets = Project_ != null ? new[] { Project_ } : new string[0];
				return new BuildRequestData(SolutionFilename, globalProperties, "14.0", targets, null);
			}
		}

		/// <summary>
		/// Asynchronously begins a background build.
		/// </summary>
		/// <param name="onCompleted"></param>
		/// <param name="target_">The optional target to build</param>

		public void beginBuild(Action<BuildStatus> onCompleted, string target_)
		{
			if (IsRunning)
			{
				Log.E("internal error: build is running");
				return;
			}

			var solution = (Solution2)_dte.Solution;
			var configuration = (SolutionConfiguration2)solution.SolutionBuild.ActiveConfiguration;
			var solutionFilename = solution.FullName;
			var request = new BuildRequest(solutionFilename, target_, configuration.Name, configuration.PlatformName);

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

			var consoleLogger = new ConsoleLogger(LoggerVerbosity.Quiet, str => 
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
			using (DevTools.measureBlock("build time"))
			using (var buildManager = new BuildManager())
			{
				var status = BuildStatus.Failed;

				buildManager.BeginBuild(parameters);
				using (cancellation.Register(() =>
				{
					Log.I("cancelling background build");
					buildManager.CancelAllSubmissions();
				}))
				{
					var result = buildManager.BuildRequest(request.createData());
					if (cancellation.IsCancellationRequested)
						status = BuildStatus.Indeterminate;
					else
						switch (result.OverallResult)
						{
							case BuildResultCode.Success:
								status = BuildStatus.Ok;
								break;
							case BuildResultCode.Failure:
								status = BuildStatus.Failed;
								break;
						}
				}
				buildManager.EndBuild();
				return status;
			}
		}

		void printIntro(BuildRequest request)
		{
			var isSolution = request.Project_ == null;
			var projectInfo = isSolution ? "Solution: " + getSolutionNameFromFilename(request.SolutionFilename) : "Project: " + request.Project_;
			var configurationInfo = "Configuration: " + request.Configuration + " " + request.Platform;

			coreToIDE(() => _pane.OutputString($"---------- BuildOnSave: {projectInfo}, {configurationInfo} ----------\n"));
		}

		void printSummary(SummaryLogger logger)
		{
			var results = logger.ProjectResults.Where(result => !isSolutionFilename(result.Filename)).ToArray();

			var projectResults = results.GroupBy(result => result.ProjectId).Select(rs => rs.Last());

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

		static bool isSolutionFilename(string fn)
		{
			return fn.EndsWith(".sln", true, CultureInfo.InvariantCulture);
		}

		static string getSolutionNameFromFilename(string fn)
		{
			return Path.GetFileNameWithoutExtension(fn);
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
			{}

			public LoggerVerbosity Verbosity { get; set; }
			public string Parameters { get; set; }
		}
	}
}
