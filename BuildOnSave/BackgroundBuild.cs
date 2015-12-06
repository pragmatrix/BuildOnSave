using System;
using System.Collections.Generic;
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
				var result = buildManager.Build(parameters, request);
				printSummary(result);
			}
		}

		void printSummary(BuildResult result)
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
