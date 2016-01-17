using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using stdole;

namespace BuildOnSave
{

	sealed class DriverUI : IDisposable
	{
		readonly DTE _dte;
		readonly ImageToPictureDispConverter _pictureConverter;
		readonly CommandBarButton _barButton_;

		public DriverUI(DTE dte)
		{
			_dte = dte;
			_pictureConverter = new ImageToPictureDispConverter();

			// http://stackoverflow.com/questions/12049362/programmatically-add-add-in-button-to-the-standard-toolbar
			// add a toolbar button to the standard toolbar
			var bar = ((CommandBars)_dte.CommandBars)["Standard"];
			if (bar != null)
			{
				var control = (CommandBarButton)bar.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
				control.Style = MsoButtonStyle.msoButtonIcon;
				control.TooltipText = "BuildOnSave Status";
				_barButton_ = control;
			}
			else
			{
				Log.W("failed to add command button, no Standard command bar");
			}

			setBuildStatus(BuildStatus.Indeterminate);
		}

		public void Dispose()
		{
			_barButton_?.Delete(true);
			_pictureConverter.Dispose();
		}

		public void setBuildStatus(BuildStatus status)
		{
			if (_barButton_ == null)
				return;
			var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			var imageName = getImageNameForBuildStatus(status);
			var imagePath = Path.Combine(assemblyDir, imageName);
			var image = Image.FromFile(imagePath);
			var picture = _pictureConverter.getIPicture(image);
			_barButton_.Picture = picture;
		}

		static string getImageNameForBuildStatus(BuildStatus status)
		{
			switch (status)
			{
				case BuildStatus.Indeterminate:
					return "status-indeterminate.png";
				case BuildStatus.Ok:
					return "status-green.png";
				case BuildStatus.Failed:
					return "status-red.png";
				default:
					throw new ArgumentOutOfRangeException(nameof(status), status, null);
			}
		}

		// http://stackoverflow.com/questions/31324924/vs-2013-sdk-how-to-set-a-commandbarbutton-picture

		sealed class ImageToPictureDispConverter : AxHost
		{
			public ImageToPictureDispConverter() : base("{63109182-966B-4e3c-A8B2-8BC4A88D221C}")
			{}

			public StdPicture getIPicture(Image image)
			{
				return (StdPicture) GetIPictureFromPicture(image);
			}
		}
	}
}
