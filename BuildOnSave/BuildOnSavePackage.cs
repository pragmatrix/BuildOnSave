using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace BuildOnSave
{
	[PackageRegistration(UseManagedResourcesOnly = true)]
	[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
	// why is this needed?
	[ProvideMenuResource("Menus.ctmenu", 1)]
	[Guid(PackageGuidString)]

	// This package needs to be loaded _before_ the user interacts with its UI.
	[ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists)]
	public sealed class BuildOnSavePackage : Package
	{
		const string PackageGuidString = "ce5fb4cb-f9c4-469e-ac59-647eb754148c";
		BuildOnSave _liveBuild;

		/// <summary>
		/// Initialization of the package; this method is called right after the package is sited, so this is the place
		/// where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		protected override void Initialize()
		{
			base.Initialize();
			_liveBuild = new BuildOnSave(this);
		}
	}
}
