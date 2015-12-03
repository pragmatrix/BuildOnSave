using System;
using System.Runtime.CompilerServices;
using System.Threading;
using EnvDTE;

namespace BuildOnSave
{
	sealed class Driver
	{
		readonly DTE _dte;
		public readonly BuildType BuildType;
		readonly OutputWindowPane _outputPane;
		readonly SynchronizationContext _context;

		public Driver(DTE dte, BuildType buildType, OutputWindowPane outputPane)
		{
			_dte = dte;
			BuildType = buildType;
			_outputPane = outputPane;
			_context = SynchronizationContext.Current;
		}

		// state
		bool _buildPending;
		bool _ignoreDocumentSaves;

		public void onBeforeBuildSolutionCommand(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
		{
			dumpState();
			_ignoreDocumentSaves = true;
		}

		public void onAfterBuildSolutionCommand(string guid, int id, object customIn, object customOut)
		{
			dumpState();
			_ignoreDocumentSaves = false;
		}

		public void onBuildBegin(vsBuildScope scope, vsBuildAction action)
		{
			dumpState();
			Log.D("build begin {scope}, {action}", scope, action);
		}

		public void onBuildDone(vsBuildScope scope, vsBuildAction action)
		{
			dumpState();
			Log.D("build done {scope}, {action}", scope, action);

			if (scope == vsBuildScope.vsBuildScopeSolution && action == vsBuildAction.vsBuildActionBuild)
			{
				// need to schedul Build(), otherwise build output gets invisible.
				schedule(mayBuildAfterBuildDone);
			}
		}

		public void onDocumentSaved(Document document)
		{
			dumpState();

			// ignore if we are called back from Build().
			if (!_ignoreDocumentSaves)
			{
				Log.D("document saved {path}:", document.FullName);
				schedule(buildAfterSave);
			}
			else
			{
				Log.D("document saved because of build, ignored");
			}
		}

		void schedule(Action action)
		{
			_context.Post(_ => action(), null);
		}

		void buildAfterSave()
		{
			dumpState();
			if (tryBeginBuild())
				return;
			_buildPending = true;
			Log.D("Can't build now, build pending");
		}

		void mayBuildAfterBuildDone()
		{
			dumpState();

			if (_buildPending)
			{
				_buildPending = false;
				Log.D("retrying build");
				tryBeginBuild();
			}
		}

		bool tryBeginBuild()
		{
			dumpState();

			var solution = _dte.Solution;
			if (!solution.IsOpen)
			{
				Log.W("solution is not open");
				return false;
			}

			var build = solution.SolutionBuild;
			if (build.BuildState == vsBuildState.vsBuildStateInProgress)
			{
				Log.D("build in progress, can't build");
				return false;
			}

			Log.I("initiating build");

			try
			{
				_ignoreDocumentSaves = true;
				beginBuild(solution, BuildType);
			}
			finally
			{
				_ignoreDocumentSaves = false;
			}
			return true;
		}

		void beginBuild(Solution solution, BuildType buildType)
		{
			var solutionBuild = solution.SolutionBuild;
			switch (buildType)
			{
				case BuildType.Solution:
					// solutionBuild.Build();
					var driver = new BackgroundDriver(solution.DTE, _outputPane);
					driver.beginBuildSolution();
					break;
				case BuildType.StartUpProject:
					var startupProject = (string)((object[])solutionBuild.StartupProjects)[0];
					solutionBuild.BuildProject(solutionBuild.ActiveConfiguration.Name, startupProject);
					break;
			}
		}

		void dumpState([CallerMemberName] string context = "")
		{
			Log.D("state: {state}, pending: {pending}, thread: {thread}, context: {context}", _dte.Solution.SolutionBuild.BuildState, _buildPending, System.Threading.Thread.CurrentThread.ManagedThreadId, context);
		}
	}
}
