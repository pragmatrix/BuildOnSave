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
		readonly MenuCommand _menuItem;

		// stored to prevent GC from collecting
		readonly Events _events;
		readonly DocumentEvents _documentEvents;
		readonly BuildEvents _buildEvents;
		readonly CommandEvents _buildSolutionEvent;

		// state
		Driver _driver_;

		public BuildOnSave(Package package)
		{
			IServiceProvider serviceProvider = package;
			_dte = serviceProvider.GetService(typeof(DTE)) as DTE;
			_events = _dte.Events;
			_documentEvents = _events.DocumentEvents;
			_buildEvents = _events.BuildEvents;

			var commandService = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (commandService == null)
				return;
			var menuCommandID = new CommandID(CommandSet, CommandId);
			_menuItem = new MenuCommand(enableDisableBuildOnSave, menuCommandID);

			commandService.AddCommand(_menuItem);
			_menuItem.Visible = true;
			_menuItem.Checked = true;

			// intercept build solution command
			var guid = typeof (VSConstants.VSStd97CmdID).GUID.ToString("B");

			_buildSolutionEvent = _dte.Events.CommandEvents[guid, (int)VSConstants.VSStd97CmdID.BuildSln];

			Log.I("BuildOnSave initialized");
			connectDriver();
		}

		void enableDisableBuildOnSave(object sender, EventArgs e)
		{
			if (_driver_ == null)
			{
				connectDriver();
			}
			else
			{
				disconnectDriver();
			}

			_menuItem.Checked = _driver_ != null;
		}

		void connectDriver()
		{
			if (_driver_ != null)
				return;

			var driver = new Driver(_dte);

			_documentEvents.DocumentSaved += driver.onDocumentSaved;

			_buildEvents.OnBuildBegin += driver.onBuildBegin;
			_buildEvents.OnBuildDone += driver.onBuildDone;

			_buildSolutionEvent.BeforeExecute += driver.onBeforeBuildSolutionCommand;
			_buildSolutionEvent.AfterExecute += driver.onAfterBuildSolutionCommand;

			_driver_ = driver;

			Log.D("driver connected");
		}

		void disconnectDriver()
		{
			var driver = _driver_;
			if (driver == null)
				return;

			_documentEvents.DocumentSaved -= driver.onDocumentSaved;

			_buildEvents.OnBuildBegin -= driver.onBuildBegin;
			_buildEvents.OnBuildDone -= driver.onBuildDone;

			_buildSolutionEvent.BeforeExecute -= driver.onBeforeBuildSolutionCommand;
			_buildSolutionEvent.AfterExecute -= driver.onAfterBuildSolutionCommand;

			_driver_ = null;

			Log.D("driver disconnected");
		}
	}
}
