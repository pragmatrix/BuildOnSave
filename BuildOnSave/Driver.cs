using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using EnvDTE;

namespace BuildOnSave
{
	enum BuildStatus
	{
		Indeterminate,
		Ok,
		Failed
	}

	sealed class Driver : IDisposable
	{
		readonly DTE _dte;
		readonly Solution _solution;
		public readonly BuildType BuildType;
		readonly BackgroundBuild _backgroundBuild;
		readonly DriverUI _ui;
		readonly SynchronizationContext _context;

		public Driver(DTE dte, BuildType buildType, BackgroundBuild backgroundBuild, DriverUI ui)
		{
			_dte = dte;
			_solution = _dte.Solution;
			BuildType = buildType;
			_backgroundBuild = backgroundBuild;
			_ui = ui;
			_context = SynchronizationContext.Current;

		}

		public void Dispose()
		{
			_ui.Dispose();
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
			Log.D("VS build begin {scope}, {action}", scope, action);

			prepareForVSBuild();
		}

		void prepareForVSBuild()
		{
			// immediately reflect in the UI that we are not wanted anymore!
			_ui.setBuildStatus(BuildStatus.Indeterminate);

			_buildAgain = false;
			_backgroundBuild.cancelAndWait();
		}

		public void onBuildDone(vsBuildScope scope, vsBuildAction action)
		{
			dumpState();
			Log.D("VS build done {scope}, {action}", scope, action);

			if ((scope != vsBuildScope.vsBuildScopeSolution && scope != vsBuildScope.vsBuildScopeProject) || action != vsBuildAction.vsBuildActionBuild)
				return;

			// note: we can not retrieve the build status for the VS build right now, more info here:
			// http://stackoverflow.com/questions/2801985/how-to-get-notification-when-a-successful-build-has-finished

			buildCompleted(BuildStatus.Indeterminate);
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
			if (!IsVSOrBackgroundBuildRunning)
			{
				saveSolutionFiles();
				beginBuild(_dte.Solution, BuildType);
			}
			else
			{
				_buildAgain = true;
			}
		}

		void saveSolutionFiles()
		{
			try
			{
				_ignoreDocumentSaves = true;
				saveOpenDocumentsBelongingToAProject();
				saveOpenProjects();
				saveSolution();
			}
			catch (Exception e)
			{
				Log.E(e, "failed to save documents");
			}
			finally
			{
				_ignoreDocumentSaves = false;
			}
		}

		void saveOpenDocumentsBelongingToAProject()
		{
			// note:
			// _dte.Documents.SaveAll();
			// saves also documents that do not belong to any project, we don't want to do that.

			// I received once a COM exception here. There is a problem after starting up and when there is a project page open, 
			// then, when the file is saved, this project page comes up and somehone screws everything up. Probably related to 
			// the combination of Extensions I've installed (reproduced in the SharedSafe project).

			foreach (Document document in _dte.Documents)
			{
				if (document.Saved || !document.belongsToAnOpenProject())
					continue;
				Log.D("document {name} is not saved, saving now", document.Name);
				document.Save();
			}
		}

		void saveOpenProjects()
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

		bool IsVSOrBackgroundBuildRunning => IsVSBuildRunning || IsBackgroundBuildRunning;
		bool IsVSBuildRunning => _solution.IsOpen && _solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress;
		bool IsBackgroundBuildRunning => _backgroundBuild.IsRunning;

		void beginBuild(Solution solution, BuildType buildType)
		{
			dumpState();

			_ui.notifyBeginBuild();

			switch (buildType)
			{
				case BuildType.Solution:
					_backgroundBuild.beginBuild(buildCompleted, null);
					break;
				case BuildType.StartupProject:
					var startupProject = (string)((object[])solution.SolutionBuild.StartupProjects)[0];
					var startupProjectName = Path.GetFileNameWithoutExtension(startupProject);
					_backgroundBuild.beginBuild(buildCompleted, startupProjectName);
					break;
			}
		}

		void buildCompleted(BuildStatus status)
		{
			if (!_buildAgain)
			{
				_ui.setBuildStatus(status);
				return;
			}
			_buildAgain = false;
			schedule(beginBuild);
		}

		void dumpState([CallerMemberName] string context = "")
		{
			Log.D("{context}: state: {state}, again: {again}, thread: {thread}", context, _dte.Solution.SolutionBuild.BuildState, _buildAgain, System.Threading.Thread.CurrentThread.ManagedThreadId);
		}
	}
}
