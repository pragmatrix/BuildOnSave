using System;
using System.ComponentModel.Design;
using System.Globalization;
using EnvDTE;
using LiveBuild.Core;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LiveBuild
{
	sealed class LiveBuild
	{
		const int CommandId = 0x0100;
		static readonly Guid CommandSet = new Guid("e2f191eb-1c5a-4d3c-adfb-d5b14dc47078");
		readonly Package _package;
		readonly IServiceProvider _serviceProvider;

		public LiveBuild(Package package)
		{
			_package = package;
			_serviceProvider = package;

			var commandService = _serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (commandService == null)
				return;
			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(MenuItemCallback, menuCommandID);
			commandService.AddCommand(menuItem);
		}

		void MenuItemCallback(object sender, EventArgs e)
		{
			var workspace =
				((IComponentModel) Package.GetGlobalService(typeof (SComponentModel))).GetService<VisualStudioWorkspace>();

			var message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", GetType().FullName);
			const string Title = "LiveBuild";

			VsShellUtilities.ShowMessageBox(
				_package,
				message,
				Title,
				OLEMSGICON.OLEMSGICON_INFO,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
		}
	}
}
