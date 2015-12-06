using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EnvDTE;
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

		public void beginBuildSolution(Action onCompleted)
		{
			if (_isRunning)
			{
				Log.E("internal error: build is running");
				return;
			}

			var solution = _dte.Solution;
			var configuration = solution.SolutionBuild.ActiveConfiguration.Name;
			var file = _dte.Solution.FullName;

			_pane.Activate();
			_isRunning = true;

			Action completed = () =>
			{
				_isRunning = false;
				onCompleted();
			};

			ThreadPool.QueueUserWorkItem(_ => buildSolution(file, configuration, completed));
		}

		void buildSolution(string solutionFilePath, string configuration, Action onCompleted)
		{
			try
			{
				buildSolutionCore(solutionFilePath, configuration);
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

		void buildSolutionCore(string solutionFilePath, string configuration)
		{
			_pane.Clear();
			var logger = new ConsoleLogger(LoggerVerbosity.Minimal, str => _pane.OutputString(str), color => { }, () => { });

			var parameters = new BuildParameters
			{
				Loggers = new ILogger[] {logger},
				EnableNodeReuse = false,
				ShutdownInProcNodeOnBuildFinish = true
			};

			using (var buildManager = new BuildManager())
			{
				var globalProperties = new Dictionary<string, string> {["Configuration"] = configuration};
				var request = new BuildRequestData(solutionFilePath, globalProperties, "14.0", new string[0], null);
				buildManager.Build(parameters, request);
			}
		}
	}
}
