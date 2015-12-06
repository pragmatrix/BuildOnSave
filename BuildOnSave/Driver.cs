using System;
using System.Runtime.CompilerServices;
using System.Threading;
using EnvDTE;

namespace BuildOnSave
{
	sealed class Driver
	{
		readonly DTE _dte;
		readonly Solution _solution;
		public readonly BuildType BuildType;
		readonly BackgroundBuild _backgroundBuild;
		readonly SynchronizationContext _context;

		public Driver(DTE dte, BuildType buildType, BackgroundBuild backgroundBuild)
		{
			_dte = dte;
			_solution = _dte.Solution;
			BuildType = buildType;
			_backgroundBuild = backgroundBuild;
			_context = SynchronizationContext.Current;
		}

		// state
		bool _ignoreDocumentSaves;
		bool _buildAgain;

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

			if (scope != vsBuildScope.vsBuildScopeSolution || action != vsBuildAction.vsBuildActionBuild)
				return;
			buildCompleted();
		}

		public void onDocumentSaved(Document document)
		{
			dumpState();

			if (_ignoreDocumentSaves || !document.belongsToAnOpenProject())
				return;
			Log.D("document saved {path}:", document.FullName);
			schedule(beginBuild);
		}

		void schedule(Action action)
		{
			_context.Post(_ => action(), null);
		}

		void beginBuild()
		{
			if (!IsVSOrOurBuildRunning)
			{
				saveAllSolutionFiles();
				beginBuild(_dte.Solution, BuildType);
			}
			else
			{
				_buildAgain = true;
			}
		}

		void saveAllSolutionFiles()
		{
			try
			{
				_ignoreDocumentSaves = true;
				saveAllOpenDocumentsBelongingToAProject();
				saveAllOpenProjects();
				saveSolution();
			}
			finally
			{
				_ignoreDocumentSaves = false;
			}
		}

		void saveAllOpenDocumentsBelongingToAProject()
		{
			// note:
			// _dte.Documents.SaveAll();
			// saves also documents that do not belong to any project, we don't want to do that.

			foreach (Document document in _dte.Documents)
			{
				if (document.Saved || !document.belongsToAnOpenProject())
					continue;
				Log.D("document {name} is not saved, saving now", document.Name);
				document.Save();
			}
		}

		void saveAllOpenProjects()
		{
			foreach (Project project in _dte.Solution.Projects)
			{
				if (!project.Saved)
				{
					Log.D("project {name} is not saved, saving now", project.Name);
					project.Save();
				}
			}
		}

		void saveSolution()
		{
			if (!_solution.Saved)
			{
				Log.D("solution is not saved, saving now");
				_solution.SaveAs(_dte.Solution.FullName);
			}
		}

		bool IsVSOrOurBuildRunning => IsVSBuildRunning || IsOurBuildRunning;
		bool IsVSBuildRunning => _solution.IsOpen && _solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress;
		bool IsOurBuildRunning => _backgroundBuild.IsRunning;

		void beginBuild(Solution solution, BuildType buildType)
		{
			dumpState();

			switch (buildType)
			{
				case BuildType.Solution:
					_backgroundBuild.beginBuildSolution(buildCompleted);
					break;
				case BuildType.StartUpProject:
					_backgroundBuild.beginBuildSolution(buildCompleted);
					break;
			}
		}

		void buildCompleted()
		{
			if (!_buildAgain)
				return;
			_buildAgain = false;
			schedule(beginBuild);
		}

		void dumpState([CallerMemberName] string context = "")
		{
			Log.D("{context}: state: {state}, again: {again}, thread: {thread}", context, _dte.Solution.SolutionBuild.BuildState, _buildAgain, System.Threading.Thread.CurrentThread.ManagedThreadId);
		}
	}
}
