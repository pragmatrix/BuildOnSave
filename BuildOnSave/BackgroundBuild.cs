using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

		public void beginBuild(Action onCompleted, string target_)
		{
			if (_isRunning)
			{
				Log.E("internal error: build is running");
				return;
			}

			var solution = (Solution2)_dte.Solution;
			var configuration = (SolutionConfiguration2)solution.SolutionBuild.ActiveConfiguration;
			var solutionFilename = solution.FullName;
			var request = new BuildRequest(solutionFilename, target_, configuration.Name, configuration.PlatformName);

			_isRunning = true;

			Action completed = () =>
			{
				_isRunning = false;
				onCompleted();
			};

			ThreadPool.QueueUserWorkItem(_ => build(request, completed));
		}

		void build(BuildRequest request, Action onCompleted)
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

		void buildCore(BuildRequest request)
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

			printIntro(request);

			using (var buildManager = new BuildManager())
			{
				buildManager.Build(parameters, request.createData());
			}

			printSummary(summaryLogger);
		}

		void printIntro(BuildRequest request)
		{
			var isSolution = request.Project_ == null;
			var projectInfo = isSolution
				? "Solution: " + getSolutionNameFromFilename(request.SolutionFilename)
				: "Project: " + request.Project_;
			var configurationInfo = "Configuration: " + request.Configuration + " " + request.Platform;

			_pane.OutputString($"---------- BuildOnSave: {projectInfo}, {configurationInfo} ----------\n");
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

		static string getSolutionNameFromFilename(string fn)
		{
			return Path.GetFileNameWithoutExtension(fn);
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
