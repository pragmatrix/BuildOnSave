using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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
		readonly SynchronizationContext _context;
		bool _isRunning;
		public bool IsRunning => _isRunning;

		public BackgroundBuild(DTE dte, OutputWindowPane pane)
		{
			_dte = dte;
			_pane = pane;
			_context = SynchronizationContext.Current;
		}

		public void beginBuild(Action onCompleted, string target_)
		{
			if (_isRunning)
			{
				Log.E("internal error: build is running");
				return;
			}

			var solution = (Solution2)_dte.Solution;
			var configuration = (SolutionConfiguration2)solution.SolutionBuild.ActiveConfiguration;
			var file = _dte.Solution.FullName;

			var globalProperties = new Dictionary<string, string> { ["Configuration"] = configuration.Name, ["Platform"] = configuration.PlatformName };
			var targets = target_ != null ? new[] {target_} : new string[0];
			var request = new BuildRequestData(file, globalProperties, "14.0", targets, null);

			_isRunning = true;

			Action completed = () =>
			{
				_isRunning = false;
				onCompleted();
			};

			ThreadPool.QueueUserWorkItem(_ => build(request, completed));
		}

		void build(BuildRequestData request, Action onCompleted)
		{
			try
			{
				buildCore(request);
			}
			catch (Exception e)
			{
				Log.E(e, "build crashed");
			}
			finally
			{
				_context.Post(_ => onCompleted(), null);
			}
		}

		void buildCore(BuildRequestData request)
		{
			_pane.Clear();
			_pane.Activate();

			var consoleLogger = new ConsoleLogger(LoggerVerbosity.Quiet, str => _pane.OutputString(str), color => { }, () => { })
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

			using (var buildManager = new BuildManager())
			{
				buildManager.Build(parameters, request);
			}

			printSummary(summaryLogger);
		}

		void printSummary(SummaryLogger logger)
		{
			var results = 
				logger
				.ProjectResults
				.Where(result => !isSolutionFilename(result.Filename))
				.ToArray();
			var succeded = results.Count(result => result.Succeeded);
			var failed = results.Count(result => !result.Succeeded);

			_pane.OutputString($"========== BuildOnSave: {succeded} succeeded, {failed} failed ==========\n");
		}

		static bool isSolutionFilename(string fn)
		{
			return fn.EndsWith(".sln", true, CultureInfo.InvariantCulture);
		}

		struct ProjectBuildResult
		{
			public ProjectBuildResult(string filename, bool succeeded)
			{
				Filename = filename;
				Succeeded = succeeded;
			}

			public readonly string Filename;
			public readonly bool Succeeded;
		}

		sealed class SummaryLogger : ILogger
		{
			readonly List<ProjectBuildResult> _projectResults = new List<ProjectBuildResult>();
			public IEnumerable<ProjectBuildResult> ProjectResults => _projectResults.ToArray();

			public void Initialize(IEventSource eventSource)
			{
				eventSource.ProjectFinished += onProjectFinished;
			}

			void onProjectFinished(object sender, ProjectFinishedEventArgs e)
			{
				_projectResults.Add(new ProjectBuildResult(e.ProjectFile, e.Succeeded));
			}

			public void Shutdown()
			{
			}

			public LoggerVerbosity Verbosity { get; set; }
			public string Parameters { get; set; }
		}
	}
}
