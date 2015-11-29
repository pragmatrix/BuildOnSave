using System;
using System.ComponentModel.Design;
using System.Runtime.CompilerServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace BuildOnSave
{
	sealed class BuildOnSave
	{
		const int CommandId = 0x0100;
		static readonly Guid CommandSet = new Guid("e2f191eb-1c5a-4d3c-adfb-d5b14dc47078");

		readonly DTE _dte;
		readonly SynchronizationContext _context;

		// stored to prevent GC from collecting
		readonly Events _events;
		readonly DocumentEvents _documentEvents;
		readonly BuildEvents _buildEvents;
		readonly CommandEvents _buildSolutionEvent;

		// state
		bool _buildPending;
		bool _ignoreDocumentSaves;

		public BuildOnSave(Package package)
		{
			_context = SynchronizationContext.Current;
			IServiceProvider serviceProvider = package;
			_dte = serviceProvider.GetService(typeof(DTE)) as DTE;
			_events = _dte.Events;
			_documentEvents = _events.DocumentEvents;
			_buildEvents = _events.BuildEvents;

			var commandService = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (commandService == null)
				return;
			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(delegate { }, menuCommandID);
			commandService.AddCommand(menuItem);

			_documentEvents.DocumentSaved += onDocumentSaved;
			_buildEvents.OnBuildBegin += onBuildBegin;
			_buildEvents.OnBuildDone += onBuildDone;

			// intercept build solution command
			var guid = typeof (VSConstants.VSStd97CmdID).GUID.ToString("B");

			_buildSolutionEvent = _dte.Events.CommandEvents[guid, (int)VSConstants.VSStd97CmdID.BuildSln];
			_buildSolutionEvent.BeforeExecute += onBeforeBuildSolutionCommand;
			_buildSolutionEvent.AfterExecute += onAfterBuildSolutionCommand;

			Log.I("BuildOnSave initialized");
		}

		void onBeforeBuildSolutionCommand(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
		{
			dumpState();
			_ignoreDocumentSaves = true;
		}

		void onAfterBuildSolutionCommand(string guid, int id, object customIn, object customOut)
		{
			dumpState();
			_ignoreDocumentSaves = false;
		}

		void onBuildBegin(vsBuildScope scope, vsBuildAction action)
		{
			dumpState();
			Log.D("build begin {scope}, {action}", scope, action);
		}

		void onBuildDone(vsBuildScope scope, vsBuildAction action)
		{
			dumpState();
			Log.D("build done {scope}, {action}", scope, action);

			if (scope == vsBuildScope.vsBuildScopeSolution && action == vsBuildAction.vsBuildActionBuild)
			{
				// need to schedul Build(), otherwise build output gets invisible.
				schedule(mayBuildAfterBuildDone);
			}
		}

		void onDocumentSaved(Document document)
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
				Log.D("cant build");
				return false;
			}
			Log.I("initiating build");

			try
			{
				_ignoreDocumentSaves = true;
				build.Build();
			}
			finally
			{
				_ignoreDocumentSaves = false;
			}
			return true;
		}

		void dumpState([CallerMemberName] string context = "")
		{
			Log.D("state: {state}, pending: {pending}, thread: {thread}, context: {context}",
				_dte.Solution.SolutionBuild.BuildState,
				_buildPending,
				System.Threading.Thread.CurrentThread.ManagedThreadId, context);
		}
	}
}
