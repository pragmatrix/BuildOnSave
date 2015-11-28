//------------------------------------------------------------------------------
// <copyright file="LiveBuildCommandPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
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

		static LiveBuildCommandPackage()
		{
			RedirectSerilog14();
		}

		static void RedirectSerilog14()
		{
			ResolveEventHandler handler = null;

			handler = (sender, args) => {
				// Use latest strong name & version when trying to load SDK assemblies
				var requestedAssembly = new AssemblyName(args.Name);
				if (requestedAssembly.Name != "Serilog"  || requestedAssembly.Version.Major != 1 || requestedAssembly.Version.Minor != 4)
					return null;

				AppDomain.CurrentDomain.AssemblyResolve -= handler;

				return Assembly.GetAssembly(typeof (Serilog.LoggerConfiguration));
			};
			AppDomain.CurrentDomain.AssemblyResolve += handler;
		}
	}
}
