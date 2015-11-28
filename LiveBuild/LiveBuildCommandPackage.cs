//------------------------------------------------------------------------------
// <copyright file="LiveBuildCommandPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace LiveBuild
{
	[PackageRegistration(UseManagedResourcesOnly = true)]
	[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
	// why is this needed?
	[ProvideMenuResource("Menus.ctmenu", 1)]
	[Guid(PackageGuidString)]
	public sealed class LiveBuildCommandPackage : Package
	{
		const string PackageGuidString = "ce5fb4cb-f9c4-469e-ac59-647eb754148c";

		/// <summary>
		/// Initialization of the package; this method is called right after the package is sited, so this is the place
		/// where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		protected override void Initialize()
		{
			LiveBuildCommand.Initialize(this);
			base.Initialize();
		}
	}
}
