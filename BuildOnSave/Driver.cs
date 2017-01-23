using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using EnvDTE;
using Project = EnvDTE.Project;
using System.Diagnostics;

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
		readonly BackgroundBuild2 _backgroundBuild;
		readonly DriverUI _ui;
		readonly SynchronizationContext _context;
		readonly List<Document> _savedDocuments = new List<Document>();

		public Driver(DTE dte, BuildType buildType, BackgroundBuild2 backgroundBuild, DriverUI ui)
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
			_savedDocuments.Add(document);
			Log.D("document saved {path}:", document.FullName);
			schedule(beginBuild);
		}

		void schedule(Action action)
		{
			_context.Post(_ => action(), null);
		}

		void beginBuild()
		{
			try
			{
				if (IsVSOrBackgroundBuildRunning)
				{
					_buildAgain = true;
					return;
				}

				var savedDocuments = _savedDocuments.ToArray();
				if (savedDocuments.Length == 0)
					// two builds got scheduled?
					// This can be reproduced by renaming an F# type name that is used in several dependent F# projects.
					return;

				_savedDocuments.Clear();

				var projectsChangedBeforeBuild =
					savedDocuments
						// note: this looks redundant, but it isn't: because only documents that belong to a project are added to _savedDocuments does
						// not mean that they have not removed from a project in the meantime (though this is probably very unlikely)
						.Where(document => document.belongsToAnOpenProject())
						.Select(document => document.ProjectItem.ContainingProject)
						.Distinct()
						.ToArray();

				var projectsChanged =
					projectsChangedBeforeBuild.Concat(
						projectsThatHaveChangedFilesAfterSaving()).Distinct().ToArray();

				saveSolutionFiles();
				beginBuild(_dte.Solution, BuildType, projectsChanged);
			}
			catch (Exception e)
			{
				_ui.reportError(e);
				Log.E("beginBuild failed: {message}", e.Message);
			}
		}

		void saveSolutionFiles()
		{
			_ignoreDocumentSaves = true;
			try
			{
				saveOpenDocumentsBelongingToAProject();
				saveOpenProjects();
				saveSolution();
			}
			finally
			{
				_ignoreDocumentSaves = false;
			}
		}

		IEnumerable<Project> projectsThatHaveChangedFilesAfterSaving()
		{
			var projectsOfUnsavedDocuments =
				_dte.unsavedDocumentsBelongingToAProject()
				.Select(document => document.ProjectItem.ContainingProject);

			var unsavedProjects =
				_dte.unsavedOpenProjects();

			return projectsOfUnsavedDocuments.Concat(unsavedProjects).Distinct();
		}

		void saveOpenDocumentsBelongingToAProject()
		{
			// note:
			// _dte.Documents.SaveAll();
			// saves also documents that do not belong to any project, we don't want to do that.

			// Sometimes a COM exception might happen here. 
			// Sometimes There is a problem after opening a project and when there is a project page open, 
			// and a a file is saved, this project page comes up and somehone screws everything up. 
			
			_dte.unsavedDocumentsBelongingToAProject()
				.ForEach(document =>
				{
					Log.D("document {name} is not saved, saving now", document.Name);
					document.Save();
				});
		}


		void saveOpenProjects()
		{
			_dte.unsavedOpenProjects()
				.ForEach(project =>
				{
					Log.D("project {name} is not saved, saving now", project.Name);
					project.Save();
				});
		}

		void saveSolution()
		{
			if (_solution.Saved)
				return;
			Log.D("solution is not saved, saving now");
			_solution.SaveAs(_dte.Solution.FullName);
		}

		bool IsVSOrBackgroundBuildRunning => IsVSBuildRunning || IsBackgroundBuildRunning;
		bool IsVSBuildRunning => _solution.IsOpen && _solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress;
		bool IsBackgroundBuildRunning => _backgroundBuild.IsRunning;

		void beginBuild(Solution solution, BuildType buildType, IEnumerable<Project> projects)
		{
			dumpState();
			_ui.notifyBeginBuild();

			var changedProjects = projects.Select(p => p.FullName).ToArray();

			switch (buildType)
			{
				case BuildType.Solution:
					_backgroundBuild.beginBuild(buildCompleted, null, changedProjects);
					break;

				case BuildType.StartupProject:
					// this seems to be a path relative to the solution's directory.
					var relativeStartupProjectPath = (string)((object[])solution.SolutionBuild.StartupProjects)[0];
					var solutionDirectory = Path.GetDirectoryName(solution.FullName);
					Debug.Assert(solutionDirectory != null);
					var startupProjectPath = Path.Combine(solutionDirectory, relativeStartupProjectPath);
					_backgroundBuild.beginBuild(buildCompleted, startupProjectPath, changedProjects);
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
			Log.D("{context}: state: {state}, again: {again}, thread: {thread}, saved: {saved}", 
				context, 
				_dte.Solution.SolutionBuild.BuildState, 
				_buildAgain, 
				System.Threading.Thread.CurrentThread.ManagedThreadId,
				_savedDocuments.Count);
		}
	}
}
