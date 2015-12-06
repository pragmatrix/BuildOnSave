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
			_pane.Activate();

			var logger = new ConsoleLogger(LoggerVerbosity.Quiet, str => _pane.OutputString(str), color => { }, () => { })
			{
				SkipProjectStartedText = true
			};

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
				var result = buildManager.Build(parameters, request);
				printBuildSummary(result);
			}
		}

		void printBuildSummary(BuildResult result)
		{
			var failureCount = 0;
			var successCount = 0;
			var skippedCount = 0;
			foreach (var r in result.ResultsByTarget)
			{
				var r1 = r.Value;
				var code = r1.ResultCode;
				switch (code)
				{
					case TargetResultCode.Skipped:
						++skippedCount;
						break;
					case TargetResultCode.Success:
						++successCount;
						break;
					case TargetResultCode.Failure:
						++failureCount;
						break;
				}
			}

			_pane.OutputString(
				$"========== BuildOnSave: {successCount} succeeded, {failureCount} failed, {skippedCount} skipped ==========");
		}
	}
}
