using System;
using System.ComponentModel.Design;
using System.Globalization;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LiveBuild
{
	sealed class LiveBuildCommand
	{
		const int CommandId = 0x0100;
		static readonly Guid CommandSet = new Guid("e2f191eb-1c5a-4d3c-adfb-d5b14dc47078");
		readonly Package package;

		LiveBuildCommand(Package package)
		{
			this.package = package;

			var commandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (commandService == null)
				return;
			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(MenuItemCallback, menuCommandID);
			commandService.AddCommand(menuItem);
		}

		static LiveBuildCommand Instance
		{
			get; set;
		}

		IServiceProvider ServiceProvider => package;

		public static void Initialize(Package package)
		{
			Instance = new LiveBuildCommand(package);
		}

		void MenuItemCallback(object sender, EventArgs e)
		{
			var message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", GetType().FullName);
			const string Title = "LiveBuildCommand";

			VsShellUtilities.ShowMessageBox(
				ServiceProvider,
				message,
				Title,
				OLEMSGICON.OLEMSGICON_INFO,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
		}
	}
}
