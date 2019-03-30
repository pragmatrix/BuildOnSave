using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using System.Linq;
using System.Threading;

namespace BuildOnSave
{
	[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
	[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
	// why is this needed?
	[ProvideMenuResource("Menus.ctmenu", 1)]
	[Guid(PackageGuidString)]

	// This package needs to be loaded _before_ the user interacts with its UI.
	[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
	public sealed class BuildOnSavePackage : AsyncPackage
	{
		const string PackageGuidString = "ce5fb4cb-f9c4-469e-ac59-647eb754148c";
		BuildOnSave _buildOnSave_;

		// Settings history
		// 1: 
		// 2: BuildType enum got extended by ProjectsOfSavedFiles
		// 3: BuildType enum got extended by AffectedProjectsOfSavedFiles
		// 4: BuildType enum got ProjectsOfSavedFiles and AffectedProjectsOfSavedFiles removed.

		// this is what we will write.
		const string CurrentSolutionSettingsKey = "BuildOnSave_4";
		// this is what we can load
		readonly string[] LoadableSolutionSettingKeys = new [] {
			CurrentSolutionSettingsKey,
			"BuildOnSave_1" };

		// Initialize()
		DTE _dte;
		Events _events;
		SolutionEvents _solutionEvents;

		public BuildOnSavePackage()
		{
			LoadableSolutionSettingKeys.ForEach(AddOptionKey);
		}

		/// <summary>
		/// Initialization of the package; this method is called right after the package is sited, so this is the place
		/// where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
		{
			// OnLoadOptions is called in base.Initialize(), so we need to set up _buildOnSave_ now
			_dte = (DTE) await GetServiceAsync(typeof (DTE));
			_events = _dte.Events;
			_solutionEvents = _events.SolutionEvents;
			_solutionEvents.Opened += solutionOpened;
			_solutionEvents.AfterClosing += solutionClosed;

			try
			{
				var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
				// BuildOnSave constructor calls GetService() indirectly, so according to MS a
				// switch to the main thread is required.
				// Now I am asking why we need InitializeAsync() at all if we could just switch
				// to the main thread?
				await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
				_buildOnSave_ = new BuildOnSave(_dte, commandService);
			}
			catch (Exception e)
			{
				Log.E(e, "setting up BuildOnSave failed");
				throw;
			}

			await base.InitializeAsync(cancellationToken, progress);
		}

		void solutionOpened()
		{
			_buildOnSave_.solutionOpened();
		}

		void solutionClosed()
		{
			// reset default options (starts driver though)
			Log.D("solution closed, resetting to default options");
			_buildOnSave_.solutionClosed();
		}

		protected override void OnLoadOptions(string key, Stream stream)
		{
			if (!LoadableSolutionSettingKeys.Contains(key))
				return;

			if (_buildOnSave_ == null)
			{
				Log.E("can not handle OnLoadOptions, we are not initialized yet");
				return;
			}

			Log.D("loading options for key: {key}", key);

			try
			{
				var serialized = streamToString(stream);
				if (serialized == "" && key != CurrentSolutionSettingsKey)
				{
					Log.D("ignored key {key} without data in OnLoadOptions", key);
					return;
				}
				Log.D("deserializing and applying solution options {options}", serialized);
				var options = JsonConvert.DeserializeObject<SolutionOptions>(serialized);
				if (options != null) // this happened once (serialized == "null"), but I don't know why yet.
					_buildOnSave_.SolutionOptions = options;
			}
			catch (Exception e)
			{
				Log.E("failed to deserialize options: {exception}", e);
			}
		}

		protected override void OnSaveOptions(string key, Stream stream)
		{
			if (key != CurrentSolutionSettingsKey)
				return;

			if (_buildOnSave_ == null)
			{
				Log.E("can not handle OnSaveOptions, we are not initialized yet");
				return;
			}

			try
			{
				var options = _buildOnSave_.SolutionOptions;
				var serialized = JsonConvert.SerializeObject(options);
				Log.D("serialized and saving solution options: {options}", serialized);
				var bytes = Encoding.UTF8.GetBytes(serialized);
				stream.Write(bytes, 0, bytes.Length);
			}
			catch (Exception e)
			{
				Log.E("failed to serialize options: {exception}", e);
			}
		}

		static string streamToString(Stream stream)
		{
			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				return reader.ReadToEnd();
			}
		}
	}
}
