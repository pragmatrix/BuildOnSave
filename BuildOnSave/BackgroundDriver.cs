using System.Collections.Generic;
using System.Threading;
using EnvDTE;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace BuildOnSave
{
	sealed class BackgroundDriver
	{
		readonly DTE _dte;
		readonly OutputWindowPane _pane;

		public BackgroundDriver(DTE dte, OutputWindowPane pane)
		{
			_dte = dte;
			_pane = pane;
		}

		public void beginBuildSolution()
		{
			var solution = _dte.Solution;
			var configuration = _dte.Solution.SolutionBuild.ActiveConfiguration.Name;
			var file = _dte.Solution.FullName;

			ThreadPool.QueueUserWorkItem(_ => buildSolution(file, configuration));
		}

		void buildSolution(string solutionFilePath, string configuration)
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
				var globalProperties = new Dictionary<string, string> { ["Configuration"] = configuration };
				var request = new BuildRequestData(solutionFilePath, globalProperties, "14.0", new string[0], null);
				buildManager.Build(parameters, request);
			}
		}
	}
}
